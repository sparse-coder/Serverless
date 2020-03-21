using Microsoft.WindowsAzure.Storage.Table;

namespace AsyncRequestReplyPattern.CloudTables
{
    public class TaskStatusEntity : TableEntity
    {
        public bool IsComplete { get; set; }

        public string Result { get; set; }
    }
}
