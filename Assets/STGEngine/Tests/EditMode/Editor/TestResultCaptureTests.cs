using NUnit.Framework;
using System.IO;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.Tests.EditMode.Editor
{
    public class TestResultCaptureTests
    {
        [Test]
        public void Capture_WritesJsonFileWithPassStatusAndSteps()
        {
            var record = new TestRunRecord
            {
                TestName = "sample-test",
                Mode = "EditMode",
                Status = "Passed"
            };
            record.Steps.Add("boot");
            record.Steps.Add("assert");

            var path = TestArtifactPaths.GetResultPath("sample-test");
            TestSnapshotExporter.WriteJson(path, record);

            Assert.That(File.Exists(path), Is.True);
            var json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("sample-test"));
            Assert.That(json, Does.Contain("Passed"));
            Assert.That(json, Does.Contain("boot"));
        }
    }
}
