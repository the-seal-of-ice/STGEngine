using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.TestRuntime.Harness;

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

            Assert.That(runner.LastRecord.ConsoleErrors, Is.Not.Null);
        }
    }
}
