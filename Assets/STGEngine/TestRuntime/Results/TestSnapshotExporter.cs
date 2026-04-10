using System.IO;
using System.Text;
using UnityEngine;

namespace STGEngine.TestRuntime.Results
{
    public static class TestSnapshotExporter
    {
        public static void WriteJson<T>(string path, T payload)
        {
            var json = JsonUtility.ToJson(payload, true);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
    }
}
