namespace WorkflowBAP
{
    public sealed class WorkflowRunResult
    {
        public int SuccessCount { get; set; }

        public int ErrorCount { get; set; }

        public int IgnoredCount { get; set; }
    }
}