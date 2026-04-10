using System.Collections;
using UnityEngine;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public class SceneIntegrationRunner : MonoBehaviour
    {
        public TestRunRecord LastRecord { get; private set; }

        public IEnumerator RunForSeconds(float seconds)
        {
            using var capture = new TestResultCapture("scene-runtime-integration", "PlayMode", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            capture.AddStep("start-runner");
            yield return new WaitForSeconds(seconds);
            capture.AddStep("finish-runner");
            capture.MarkPassed();
            LastRecord = capture.Record;
            TestSnapshotExporter.WriteJson(TestArtifactPaths.GetResultPath("scene-runtime-integration"), LastRecord);
        }
    }
}
