using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public sealed class TimelineWorkflowRunInput
    {
        public string SegmentId;
        public string Status;
        public string RequestName;
        public string WorkflowName;
        public string EntryClipName;
    }

    public class TimelineWorkflowRunner : MonoBehaviour
    {
        public TestRunRecord LastRecord { get; private set; }
        public TestWorkflowSnapshot LastSnapshot { get; private set; }

        public IEnumerator RunWorkflow(TimelineWorkflowRunInput input, float seconds)
        {
            using var capture = new TestResultCapture(input.RequestName, "PlayMode", SceneManager.GetActiveScene().name);
            capture.AddStep("workflow-start");
            yield return new WaitForSeconds(seconds);
            capture.AddStep("workflow-finish");

            var snapshotPath = TestArtifactPaths.GetSnapshotPath(input.RequestName);
            LastSnapshot = new TestWorkflowSnapshot
            {
                TestName = input.RequestName,
                Mode = input.WorkflowName,
                SnapshotPath = snapshotPath
            };
            TestSnapshotExporter.WriteJson(snapshotPath, LastSnapshot);

            capture.Record.SnapshotPath = snapshotPath;
            capture.MarkPassed();
            LastRecord = capture.Record;
            TestSnapshotExporter.WriteJson(TestArtifactPaths.GetResultPath(input.RequestName), LastRecord);
        }
    }
}
