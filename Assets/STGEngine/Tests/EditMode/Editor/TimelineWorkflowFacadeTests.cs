using NUnit.Framework;
using STGEngine.Editor.TestTools;

namespace STGEngine.Tests.EditMode.Editor
{
    public class TimelineWorkflowFacadeTests
    {
        [Test]
        public void TimelineFacade_CreatesPreparedWorkflowRequest()
        {
            var record = TimelineWorkflowTestFacade.CreateWorkflowRequest("segment-001");
            Assert.That(record.Status, Is.EqualTo("Prepared"));
            Assert.That(record.RequestName, Does.Contain("segment-001"));
            Assert.That(record.WorkflowName, Is.EqualTo("TimelinePreview"));
            Assert.That(record.EntryClipName, Is.EqualTo("segment-001-entry"));
        }

        [Test]
        public void TimelineFacade_UsesStableDefaults_WhenSegmentIdMissing()
        {
            var record = TimelineWorkflowTestFacade.CreateWorkflowRequest(null);
            Assert.That(record.SegmentId, Is.EqualTo("segment-default"));
            Assert.That(record.Status, Is.EqualTo("Prepared"));
            Assert.That(record.RequestName, Is.EqualTo("timeline-workflow-segment-default"));
            Assert.That(record.WorkflowName, Is.EqualTo("TimelinePreview"));
            Assert.That(record.EntryClipName, Is.EqualTo("segment-default-entry"));
        }
    }
}
