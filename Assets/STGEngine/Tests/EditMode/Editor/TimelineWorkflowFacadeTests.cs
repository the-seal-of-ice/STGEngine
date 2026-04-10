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
        }
    }
}
