using System;
using System.Collections.Generic;

namespace STGEngine.TestRuntime.Results
{
    [Serializable]
    public class TestRunRecord
    {
        public string TestName;
        public string Mode;
        public string SceneName;
        public string Status;
        public string FailureStep;
        public string Exception;
        public string UnityVersion;
        public string Platform;
        public string StartedAtUtc;
        public string FinishedAtUtc;
        public List<string> Steps = new();
        public List<string> ConsoleErrors = new();
        public List<string> Attachments = new();
        public string SnapshotPath;
        public string ScreenshotPath;
    }
}
