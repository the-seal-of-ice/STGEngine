using System;
using UnityEngine;

namespace STGEngine.TestRuntime.Results
{
    public sealed class TestResultCapture : IDisposable
    {
        private readonly ConsoleLogCollector _logCollector = new();
        public TestRunRecord Record { get; }

        public TestResultCapture(string testName, string mode, string sceneName = null)
        {
            Record = new TestRunRecord
            {
                TestName = testName,
                Mode = mode,
                SceneName = sceneName ?? string.Empty,
                Status = "Running",
                UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(),
                StartedAtUtc = DateTime.UtcNow.ToString("O")
            };
            _logCollector.Start();
        }

        public void AddStep(string step) => Record.Steps.Add(step);

        public void MarkPassed()
        {
            Record.Status = "Passed";
            FinalizeRecord();
        }

        public void MarkFailed(string failureStep, Exception exception = null)
        {
            Record.Status = "Failed";
            Record.FailureStep = failureStep;
            Record.Exception = exception?.ToString() ?? string.Empty;
            FinalizeRecord();
        }

        private void FinalizeRecord()
        {
            Record.FinishedAtUtc = DateTime.UtcNow.ToString("O");
            Record.ConsoleErrors.AddRange(_logCollector.Errors);
        }

        public void Dispose()
        {
            _logCollector.Dispose();
        }
    }
}
