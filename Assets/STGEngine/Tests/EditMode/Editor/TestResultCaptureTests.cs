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

        [Test]
        public void Capture_CanAssignArtifactPathsAndAttachments()
        {
            using var capture = new TestResultCapture("sample-test", "EditMode");

            capture.SetSnapshotPath("artifacts/workflow-snapshot.json");
            capture.SetScreenshotPath("artifacts/workflow-screenshot.png");
            capture.AddAttachment("artifacts/log.txt");
            capture.AddAttachment("artifacts/trace.json");

            Assert.That(capture.Record.SnapshotPath, Is.EqualTo("artifacts/workflow-snapshot.json"));
            Assert.That(capture.Record.ScreenshotPath, Is.EqualTo("artifacts/workflow-screenshot.png"));
            Assert.That(capture.Record.Attachments, Is.EqualTo(new[]
            {
                "artifacts/log.txt",
                "artifacts/trace.json"
            }));
        }

        [Test]
        public void WorkflowSnapshotExporter_WritesWorkflowSnapshotJson()
        {
            var snapshot = new TestWorkflowSnapshot
            {
                TestName = "sample-test",
                Mode = "EditMode",
                SnapshotPath = "artifacts/workflow-snapshot.json",
                ScreenshotPath = "artifacts/workflow-screenshot.png"
            };
            snapshot.Attachments.Add("artifacts/log.txt");
            snapshot.Attachments.Add("artifacts/trace.json");

            var path = TestArtifactPaths.GetWorkflowSnapshotPath("sample-test");
            TestSnapshotExporter.WriteWorkflowSnapshot(path, snapshot);

            Assert.That(File.Exists(path), Is.True);
            var json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("sample-test"));
            Assert.That(json, Does.Contain("workflow-snapshot.json"));
            Assert.That(json, Does.Contain("workflow-screenshot.png"));
            Assert.That(json, Does.Contain("log.txt"));
        }
    }
}
