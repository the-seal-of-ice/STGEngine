using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using STGEngine.Runtime.Preview;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public class PreviewTestRunner : MonoBehaviour
    {
        [System.Serializable]
        private class PreviewRunnerSnapshot
        {
            public string TestName;
            public string PreviewerName;
            public bool HasPreviewer;
            public bool IsPlaying;
            public float RequestedSeconds;
            public float CurrentTime;
            public int BulletCount;
            public List<string> Steps = new();
        }

        public TestRunRecord LastRecord { get; private set; }

        public IEnumerator RunPreview(PatternPreviewer previewer, float seconds, string testName)
        {
            using var capture = new TestResultCapture(testName, "PlayMode", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            capture.AddStep("preview-start");
            yield return new WaitForSeconds(seconds);
            capture.AddStep("preview-finish");

            var snapshotPath = TestArtifactPaths.GetSnapshotPath(testName);
            var snapshot = new PreviewRunnerSnapshot
            {
                TestName = testName,
                PreviewerName = previewer != null ? previewer.name : string.Empty,
                HasPreviewer = previewer != null,
                IsPlaying = previewer != null && previewer.Playback.IsPlaying,
                RequestedSeconds = seconds,
                CurrentTime = previewer != null ? previewer.Playback.CurrentTime : 0f,
                BulletCount = previewer?.CurrentStates?.Count ?? 0
            };
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
