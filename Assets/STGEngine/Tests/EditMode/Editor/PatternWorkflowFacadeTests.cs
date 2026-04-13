using NUnit.Framework;
using STGEngine.Editor.TestTools;

namespace STGEngine.Tests.EditMode.Editor
{
    public class PatternWorkflowFacadeTests
    {
        [Test]
        public void PatternFacade_UsesStableDefaults_WhenPatternIdMissing()
        {
            var record = PatternWorkflowTestFacade.CreatePreviewRequest(null);
            Assert.That(record.PatternId, Is.EqualTo("pattern-default"));
            Assert.That(record.Status, Is.EqualTo("Prepared"));
            Assert.That(record.RequestName, Is.EqualTo("pattern-preview-pattern-default"));
            Assert.That(record.WorkflowName, Is.EqualTo("PatternPreview"));
            Assert.That(record.EntryPointName, Is.EqualTo("pattern-default-entry"));
        }
    }
}
