using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using STGEngine.Runtime.Preview;
using STGEngine.TestRuntime.Harness;

namespace STGEngine.Tests.PlayMode.Preview
{
    public class PatternPreviewRunnerTests
    {
        [UnityTest]
        public IEnumerator PreviewRunner_CompletesAndWritesPassRecord()
        {
            var root = new GameObject("PatternPreviewTestRoot");
            var runner = root.AddComponent<PreviewTestRunner>();
            var previewer = new GameObject("Previewer").AddComponent<PatternPreviewer>();

            yield return runner.RunPreview(previewer, 0.1f, "pattern-preview-smoke");

            Assert.That(runner.LastRecord, Is.Not.Null);
            Assert.That(runner.LastRecord.Status, Is.EqualTo("Passed"));
        }
    }
}
