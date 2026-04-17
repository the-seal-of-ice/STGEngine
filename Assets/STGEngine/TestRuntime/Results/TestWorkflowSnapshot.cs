using System;
using System.Collections.Generic;

namespace STGEngine.TestRuntime.Results
{
    [Serializable]
    public class TestWorkflowSnapshot
    {
        public string TestName;
        public string Mode;
        public string SnapshotPath;
        public string ScreenshotPath;
        public List<string> Attachments = new();
    }
}
