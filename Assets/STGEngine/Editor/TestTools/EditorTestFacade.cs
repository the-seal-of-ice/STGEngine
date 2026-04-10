using System;
using STGEngine.Editor.UI.FileManager;

namespace STGEngine.Editor.TestTools
{
    [Serializable]
    public class EditorEnvironmentSummary
    {
        public string CatalogRoot;
        public string OverrideRoot;
    }

    public static class EditorTestFacade
    {
        public static EditorEnvironmentSummary CreateEnvironmentSummary()
        {
            return new EditorEnvironmentSummary
            {
                CatalogRoot = STGCatalog.BasePath,
                OverrideRoot = OverrideManager.ModifiedDir
            };
        }
    }
}
