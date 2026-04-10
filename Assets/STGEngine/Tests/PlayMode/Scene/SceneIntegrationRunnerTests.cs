using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.TestRuntime.Harness;

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
        }
    }
}
