using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.TestRuntime.Harness;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.Tests.PlayMode.Scene
{
    public class SceneWorkflowRunnerTests
    {
        [UnityTest]
        public IEnumerator SceneRunner_ProducesJsonArtifact()
        {
            var host = new GameObject("SceneWorkflowHost");
            var runner = host.AddComponent<SceneIntegrationRunner>();

            yield return runner.RunForSeconds(0.1f);

            var expectedSnapshotPath = TestArtifactPaths.GetSnapshotPath("scene-runtime-integration");
            Assert.That(runner.LastRecord.ConsoleErrors, Is.Not.Null);
            Assert.That(runner.LastRecord.SnapshotPath, Is.EqualTo(expectedSnapshotPath));
            Assert.That(File.Exists(expectedSnapshotPath), Is.True);
        }
    }
}
