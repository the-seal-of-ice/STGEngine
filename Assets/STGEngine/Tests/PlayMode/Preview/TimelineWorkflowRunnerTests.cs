using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.TestRuntime.Harness;

namespace STGEngine.Tests.PlayMode.Preview
{
    public class TimelineWorkflowRunnerTests
    {
        [UnityTest]
        public IEnumerator RunWorkflow_ProducesPassRecordAndSnapshot()
        {
            var input = new TimelineWorkflowRunInput
            {
                SegmentId = "segment-001",
                Status = "Prepared",
                RequestName = "timeline-workflow-segment-001",
                WorkflowName = "TimelinePreview",
                EntryClipName = "segment-001-entry"
            };
            var host = new GameObject("TimelineWorkflowHost");
            var runner = host.AddComponent<TimelineWorkflowRunner>();

            yield return runner.RunWorkflow(input, 0.1f);

            Assert.That(runner.LastRecord, Is.Not.Null);
            Assert.That(runner.LastRecord.Status, Is.EqualTo("Passed"));
            Assert.That(runner.LastRecord.SnapshotPath, Is.Not.Null.And.Not.Empty);
            Assert.That(runner.LastSnapshot, Is.Not.Null);
            Assert.That(runner.LastSnapshot.TestName, Is.EqualTo("timeline-workflow-segment-001"));
            Assert.That(runner.LastSnapshot.Mode, Is.EqualTo("TimelinePreview"));
        }
    }
}
