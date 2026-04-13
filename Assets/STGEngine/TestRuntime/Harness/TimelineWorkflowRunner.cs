using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using STGEngine.Editor.TestTools;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public class TimelineWorkflowRunner : MonoBehaviour
    {
        public TestRunRecord LastRecord { get; private set; }
        public TestWorkflowSnapshot LastSnapshot { get; private set; }

        public IEnumerator RunWorkflow(TimelineWorkflowRequest request, float seconds)
        {
            using var capture = new TestResultCapture(request.RequestName, "PlayMode", SceneManager.GetActiveScene().name);
            capture.AddStep("workflow-start");
            yield return new WaitForSeconds(seconds);
            capture.AddStep("workflow-finish");

            LastSnapshot = TestWorkflowSnapshot.Create(request, capture.Record.Steps.Count);
            var snapshotPath = TestArtifactPaths.GetSnapshotPath(request.RequestName);
            TestSnapshotExporter.WriteJson(snapshotPath, LastSnapshot);

            capture.Record.SnapshotPath = snapshotPath;
            capture.MarkPassed();
            LastRecord = capture.Record;
            TestSnapshotExporter.WriteJson(TestArtifactPaths.GetResultPath(request.RequestName), LastRecord);
        }
    }
}
