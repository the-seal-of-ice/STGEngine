using NUnit.Framework;
using System.IO;
using STGEngine.Editor.UI.FileManager;

namespace STGEngine.Tests.EditMode.Editor
{
    public class OverrideManagerTests
    {
        [Test]
        public void SaveOverride_CreatesYamlFileAndCanDeleteIt()
        {
            const string contextId = "segment_test";
            const string resourceId = "pattern_test";
            var path = OverrideManager.GetOverridePath(contextId, resourceId);
            if (File.Exists(path)) File.Delete(path);

            OverrideManager.SaveOverride(contextId, resourceId, "id: pattern_test");
            Assert.That(File.Exists(path), Is.True);
            Assert.That(OverrideManager.HasOverride(contextId, resourceId), Is.True);

            var deleted = OverrideManager.DeleteOverride(contextId, resourceId);
            Assert.That(deleted, Is.True);
            Assert.That(File.Exists(path), Is.False);
        }
    }
}
