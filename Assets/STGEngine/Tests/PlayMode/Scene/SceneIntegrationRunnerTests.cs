using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.TestRuntime.Harness;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.Tests.PlayMode.Scene
{
    public class SceneIntegrationRunnerTests
    {
        [UnityTest]
        public IEnumerator RunForSeconds_CapturesBasicSceneState()
        {
            var go = new GameObject("SceneIntegrationRunnerHost");
            var runner = go.AddComponent<SceneIntegrationRunner>();

            yield return runner.RunForSeconds(0.1f);

            Assert.That(runner.LastRecord, Is.Not.Null);
            Assert.That(runner.LastRecord.Status, Is.EqualTo("Passed"));
            Assert.That(runner.LastRecord.SnapshotPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(runner.LastRecord.SnapshotPath), Is.True);
            Assert.That(runner.LastRecord.Attachments, Does.Contain(runner.LastRecord.SnapshotPath));
        }
    }
}
