using NUnit.Framework;
using STGEngine.Editor.UI.FileManager;

namespace STGEngine.Tests.EditMode.Editor
{
    public class STGCatalogTests
    {
        [Test]
        public void NameToId_ConvertsDisplayNameToSnakeCase()
        {
            var id = STGCatalog.NameToId("Ring Wave Demo");
            Assert.That(id, Is.EqualTo("ring_wave_demo"));
        }

        [Test]
        public void Load_CreatesCatalogObject()
        {
            var catalog = STGCatalog.Load();
            Assert.That(catalog, Is.Not.Null);
            Assert.That(STGCatalog.BasePath, Is.Not.Empty);
        }
    }
}
