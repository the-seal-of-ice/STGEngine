using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public class SceneIntegrationRunner : MonoBehaviour
    {
        [System.Serializable]
        private class SceneRunnerSnapshot
        {
            public string TestName;
            public string SceneName;
            public float RequestedSeconds;
            public int RootObjectCount;
            public List<string> RootObjectNames = new();
            public List<string> Steps = new();
        }

        public TestRunRecord LastRecord { get; private set; }

        public IEnumerator RunForSeconds(float seconds)
        {
            const string testName = "scene-runtime-integration";
            using var capture = new TestResultCapture(testName, "PlayMode", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            capture.AddStep("start-runner");
            yield return new WaitForSeconds(seconds);
            capture.AddStep("finish-runner");

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();
            var snapshotPath = TestArtifactPaths.GetSnapshotPath(testName);
            var snapshot = new SceneRunnerSnapshot
            {
                TestName = testName,
                SceneName = activeScene.name,
                RequestedSeconds = seconds,
                RootObjectCount = rootObjects.Length
            };
            foreach (var rootObject in rootObjects)
                snapshot.RootObjectNames.Add(rootObject.name);
            snapshot.Steps.AddRange(capture.Record.Steps);
            TestSnapshotExporter.WriteJson(snapshotPath, snapshot);

            capture.Record.SnapshotPath = snapshotPath;
            capture.Record.Attachments.Add(snapshotPath);
            capture.MarkPassed();
            LastRecord = capture.Record;
            TestSnapshotExporter.WriteJson(TestArtifactPaths.GetResultPath(testName), LastRecord);
        }
    }
}
