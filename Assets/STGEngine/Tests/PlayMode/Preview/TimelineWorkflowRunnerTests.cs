using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.Editor.TestTools;
using STGEngine.TestRuntime.Harness;

namespace STGEngine.Tests.PlayMode.Preview
{
    public class TimelineWorkflowRunnerTests
    {
        [UnityTest]
        public IEnumerator RunWorkflow_ProducesPassRecordAndSnapshot()
        {
            var request = TimelineWorkflowTestFacade.CreateWorkflowRequest("segment-001");
            var host = new GameObject("TimelineWorkflowHost");
            var runner = host.AddComponent<TimelineWorkflowRunner>();

            yield return runner.RunWorkflow(request, 0.1f);

            Assert.That(runner.LastRecord, Is.Not.Null);
            Assert.That(runner.LastRecord.Status, Is.EqualTo("Passed"));
            Assert.That(runner.LastRecord.SnapshotPath, Is.Not.Null.And.Not.Empty);
            Assert.That(runner.LastSnapshot, Is.Not.Null);
            Assert.That(runner.LastSnapshot.SegmentId, Is.EqualTo("segment-001"));
            Assert.That(runner.LastSnapshot.Status, Is.EqualTo("Prepared"));
            Assert.That(runner.LastSnapshot.StepCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void CreateWorkflowRequest_ProvidesStableWorkflowInput()
        {
            var request = TimelineWorkflowTestFacade.CreateWorkflowRequest("segment-stable");

            Assert.That(request, Is.Not.Null);
            Assert.That(request.SegmentId, Is.EqualTo("segment-stable"));
            Assert.That(request.Status, Is.EqualTo("Prepared"));
            Assert.That(request.RequestName, Is.EqualTo("timeline-workflow-segment-stable"));
            Assert.That(request.WorkflowName, Is.EqualTo("TimelinePreview"));
            Assert.That(request.EntryClipName, Is.EqualTo("segment-stable-entry"));
        }
    }
}
