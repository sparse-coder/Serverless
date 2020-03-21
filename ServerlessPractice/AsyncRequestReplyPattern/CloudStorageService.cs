using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;

namespace AsyncRequestReplyPattern
{
    public class CloudStorageService
    {
        public  async Task<CloudTable> GetCloudTableReferenceAsync(string tableName, string connectionString = "")
        {
            CloudStorageAccount storageAccount =
                string.IsNullOrWhiteSpace(connectionString)
                ? CloudStorageAccount.DevelopmentStorageAccount : CloudStorageAccount.Parse(connectionString);

            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            CloudTable table = tableClient.GetTableReference(tableName);
            await table.CreateIfNotExistsAsync();
            return table;
        }
    }
}
