namespace STGEngine.Editor.TestTools
{
    public class TimelineWorkflowRequest
    {
        public string SegmentId;
        public string Status;
        public string RequestName;
        public string WorkflowName;
        public string EntryClipName;
    }

    public static class TimelineWorkflowTestFacade
    {
        public static TimelineWorkflowRequest CreateWorkflowRequest(string segmentId)
        {
            return new TimelineWorkflowRequest
            {
                SegmentId = segmentId,
                Status = "Prepared",
                RequestName = $"timeline-workflow-{segmentId}",
                WorkflowName = "TimelinePreview",
                EntryClipName = $"{segmentId}-entry"
            };
        }
    }
}
