using System;
using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.TestRuntime.Results
{
    public sealed class ConsoleLogCollector : IDisposable
    {
        private readonly List<string> _errors = new();

        public IReadOnlyList<string> Errors => _errors;

        public void Start()
        {
            Application.logMessageReceived += HandleLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _errors.Add($"[{type}] {condition}\n{stackTrace}");
        }
    }
}
