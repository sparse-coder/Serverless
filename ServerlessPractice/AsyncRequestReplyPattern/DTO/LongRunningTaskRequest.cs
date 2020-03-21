namespace AsyncRequestReplyPattern.DTO
{
    public class LongRunningTaskRequest
    {
        public int RunForMinutes { get; set; }
        public string Description { get; set; }
    }
}
