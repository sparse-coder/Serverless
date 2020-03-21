namespace AsyncRequestReplyPattern.DTO
{
    public class Message
    {
        public string RequestId { get; set; }
        public string StatusUrl { get; set; }
        public LongRunningTaskRequest RequestMessage { get; set; }
    }
}
