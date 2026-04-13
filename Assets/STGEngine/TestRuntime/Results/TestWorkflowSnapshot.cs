using System;
using STGEngine.Editor.TestTools;

namespace STGEngine.TestRuntime.Results
{
    [Serializable]
    public class TestWorkflowSnapshot
    {
        public string WorkflowName;
        public string SegmentId;
        public string Status;
        public string EntryClipName;
        public int StepCount;

        public static TestWorkflowSnapshot Create(TimelineWorkflowRequest request, int stepCount)
        {
            return new TestWorkflowSnapshot
            {
                WorkflowName = request.WorkflowName,
                SegmentId = request.SegmentId,
                Status = request.Status,
                EntryClipName = request.EntryClipName,
                StepCount = stepCount
            };
        }
    }
}
