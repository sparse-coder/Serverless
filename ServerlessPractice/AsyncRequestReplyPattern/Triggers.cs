using AsyncRequestReplyPattern.CloudTables;
using AsyncRequestReplyPattern.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncRequestReplyPattern
{
    public class Triggers
    {
        public const int MinimumRunningTime = 1;
        public const int MinimumDescriptionLength = 5;
        public const string StatusTable = "LongRunningTaskStatus";
        public const string TaskQueue = "message";
        private readonly CloudStorageService _service;
        public Triggers(CloudStorageService service)
        {
            _service = service;
        }

        [FunctionName(nameof(AcceptLongRunningTask))]
        public async Task<IActionResult> AcceptLongRunningTask(
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = "expensiveTask")]LongRunningTaskRequest request
            , [Queue(TaskQueue, Connection = "AzureWebJobsStorage")]IAsyncCollector<Message> collector
            , ILogger logger
            )
        {
            logger.LogInformation("New long running task arrived.");
            if ( MinimumRunningTime > request.RunForMinutes || string.IsNullOrEmpty(request.Description))
            {
                return new BadRequestObjectResult(new
                {
                    errors = new[] { $"{nameof(request.RunForMinutes)} should be greater than {MinimumRunningTime}"
                    , $"{nameof(request.Description)} can't be empty." }
                });
            }

            var reqId = Guid.NewGuid().ToString();

            var taskStatus = new TaskStatusEntity()
            {
                PartitionKey = reqId,
                RowKey = reqId,
                IsComplete = false,
            };

            // Status URL.
            string statusUrl = $"http://{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")}/api/status/{reqId}";

            var table = await _service.GetCloudTableReferenceAsync(StatusTable);
            var insertOperation = TableOperation.Insert(taskStatus);

            //add status message to table for keeping track of task completion.
            var res =  await table.ExecuteAsync(insertOperation);
            
            if(res.HttpStatusCode != 204)
            {
                return new ObjectResult("An error occurred at server.");
            }

            var message = new Message()
            {
                RequestId = reqId,
                RequestMessage = request,
                StatusUrl = statusUrl
            };
            // queue the long running task.
            await collector.AddAsync(message);
            logger.LogInformation("Long running task queued.");

            return new AcceptedResult(statusUrl, $"Request accepted {Environment.NewLine} Status: {statusUrl}");
        }

        [FunctionName(nameof(DoLongRunningTask))]
        public async Task DoLongRunningTask(
            [QueueTrigger(TaskQueue, Connection = "AzureWebJobsStorage")]Message message
            , ILogger logger
            )
        {
            logger.LogInformation("Long running task arrived.");

            //do simulation
            await Task.Delay(message.RequestMessage.RunForMinutes * 60000);

            var table = await _service.GetCloudTableReferenceAsync(StatusTable);

            var entity = new TaskStatusEntity()
            {
                PartitionKey = message.RequestId,
                RowKey = message.RequestId,
                IsComplete = true,
                Result = JsonConvert.SerializeObject(message)
            };
            var operation = TableOperation.InsertOrReplace(entity);
            await table.ExecuteAsync(operation);
        }


        //Status endpoint
        [FunctionName(nameof(GetStatusOfLongRunningTask))]
        public async Task<IActionResult> GetStatusOfLongRunningTask(
            [HttpTrigger(AuthorizationLevel.Function, "GET", Route = "status/{reqId}")]HttpRequest req, string reqId
            , ILogger logger
            )
        {
            OnPendingType OnPending = Enum.Parse<OnPendingType>(req.Query["OnPending"].FirstOrDefault() ?? "Accepted");
            var table = await _service.GetCloudTableReferenceAsync(StatusTable);
            var query = new TableQuery<TaskStatusEntity>().Where(
                    TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition(nameof(TaskStatusEntity.PartitionKey), QueryComparisons.Equal, reqId)
                        , TableOperators.And
                        , TableQuery.GenerateFilterCondition(nameof(TaskStatusEntity.RowKey), QueryComparisons.Equal, reqId)
                        )
                    );


            var response = await table.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());

            var url = $"http://{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")}/api/status/{reqId}";
            // as row key inside a partition is unique. This will always return 1 entity.
            var result = response.Results.FirstOrDefault();
            
            if(result == null)
            {
                switch(OnPending)
                {
                    case OnPendingType.Accepted:
                        return new AcceptedResult() { Location = url };
                    case OnPendingType.Synchronous:
                    {
                            int backoff = 100;
                            var retryResponse = (await table.ExecuteQuerySegmentedAsync(query, new TableContinuationToken())).Results.FirstOrDefault();
                            // backoff and retry
                            while (retryResponse == null && backoff < 6000)
                            {
                                logger.LogInformation($"Retrying in {backoff} ms.");
                                backoff = backoff * 2;
                                await Task.Delay(backoff);
                                retryResponse = (await table.ExecuteQuerySegmentedAsync(query, new TableContinuationToken())).Results.FirstOrDefault();
                            }
                            if(retryResponse == null)
                            {
                                return new NotFoundResult();
                            }
                            else if(retryResponse.IsComplete)
                            {
                                return new OkObjectResult(result.Result);
                            }
                            else
                            {
                                return new RedirectResult(url);
                            }
                    }
                    default:
                        throw new InvalidOperationException($"Unexpected value {OnPending}");
                }
            }
            else if (result.IsComplete)
            {
                return new OkObjectResult(result.Result);
            }
            else
            {
                return new ObjectResult(new { status = "processing", location = url});
            }
        }
    }
}
