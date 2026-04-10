using NUnit.Framework;
using STGEngine.Editor.TestTools;

namespace STGEngine.Tests.EditMode.Editor
{
    public class EditorTestFacadeTests
    {
        [Test]
        public void CreateSummary_ContainsCatalogAndOverrideRoots()
        {
            var summary = EditorTestFacade.CreateEnvironmentSummary();

            Assert.That(summary.CatalogRoot, Is.Not.Empty);
            Assert.That(summary.OverrideRoot, Is.Not.Empty);
        }
    }
}
