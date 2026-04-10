using System.Collections;
using UnityEngine;
using STGEngine.Runtime.Preview;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public class PreviewTestRunner : MonoBehaviour
    {
        public TestRunRecord LastRecord { get; private set; }

        public IEnumerator RunPreview(PatternPreviewer previewer, float seconds, string testName)
        {
            using var capture = new TestResultCapture(testName, "PlayMode", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            capture.AddStep("preview-start");
            yield return new WaitForSeconds(seconds);
            capture.AddStep("preview-finish");
            capture.MarkPassed();
            LastRecord = capture.Record;
            TestSnapshotExporter.WriteJson(TestArtifactPaths.GetResultPath(testName), LastRecord);
        }
    }
}
