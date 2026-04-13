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
        private const string DefaultSegmentId = "segment-default";

        public static TimelineWorkflowRequest CreateWorkflowRequest(string segmentId)
        {
            var stableSegmentId = string.IsNullOrWhiteSpace(segmentId) ? DefaultSegmentId : segmentId;
            return new TimelineWorkflowRequest
            {
                SegmentId = stableSegmentId,
                Status = "Prepared",
                RequestName = $"timeline-workflow-{stableSegmentId}",
                WorkflowName = "TimelinePreview",
                EntryClipName = $"{stableSegmentId}-entry"
            };
        }
    }
}
