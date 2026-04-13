using System.IO;
using UnityEngine;

namespace STGEngine.TestRuntime.Runtime
{
    public static class TestArtifactPaths
    {
        public static string RootDirectory => Path.Combine(Application.dataPath, "..", "Temp", "STGEngineTestArtifacts");

        public static string GetSuiteDirectory(string suiteName)
        {
            var dir = Path.Combine(RootDirectory, Sanitize(suiteName));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetResultPath(string testName)
        {
            return Path.Combine(GetSuiteDirectory(testName), "result.json");
        }

        public static string GetWorkflowSnapshotPath(string testName)
        {
            return Path.Combine(GetSuiteDirectory(testName), "workflow-snapshot.json");
        }

        public static string GetSnapshotPath(string testName)
        {
            return GetWorkflowSnapshotPath(testName);
        }

        public static string GetScreenshotPath(string testName)
        {
            return Path.Combine(GetSuiteDirectory(testName), "workflow-screenshot.png");
        }

        private static string Sanitize(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }
    }
}
