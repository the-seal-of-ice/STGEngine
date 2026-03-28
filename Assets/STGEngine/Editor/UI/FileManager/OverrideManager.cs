using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace STGEngine.Editor.UI.FileManager
{
    /// <summary>
    /// Manages the Modified/Override layer for timeline resources.
    /// When a referenced resource (SpellCard, Pattern, Wave) is edited in a child layer,
    /// the original YAML stays untouched — a full copy is saved under Modified/{contextId}/.
    ///
    /// Resolution order: Override path (if exists) → Original path (via STGCatalog).
    /// </summary>
    public static class OverrideManager
    {
        /// <summary>Absolute path to Assets/Resources/STGData/Modified/.</summary>
        public static string ModifiedDir =>
            Path.Combine(STGCatalog.BasePath, "Modified");

        // ── Path resolution ──

        /// <summary>
        /// Get the override file path for a resource within a given context.
        /// Does NOT check whether the file exists.
        /// </summary>
        public static string GetOverridePath(string contextId, string resourceId)
        {
            return Path.Combine(ModifiedDir, contextId, $"{resourceId}.yaml");
        }

        /// <summary>
        /// Check whether an override exists for the given resource in the given context.
        /// </summary>
        public static bool HasOverride(string contextId, string resourceId)
        {
            if (string.IsNullOrEmpty(contextId) || string.IsNullOrEmpty(resourceId))
                return false;
            return File.Exists(GetOverridePath(contextId, resourceId));
        }

        /// <summary>
        /// Resolve a SpellCard path: override first, then catalog fallback.
        /// </summary>
        public static string ResolveSpellCardPath(STGCatalog catalog, string contextId, string scId)
        {
            if (!string.IsNullOrEmpty(contextId))
            {
                var overridePath = GetOverridePath(contextId, scId);
                if (File.Exists(overridePath))
                    return overridePath;
            }
            return catalog.GetSpellCardPath(scId);
        }

        /// <summary>
        /// Resolve a Pattern YAML path: override first, then catalog fallback.
        /// </summary>
        public static string ResolvePatternPath(STGCatalog catalog, string contextId, string patternId)
        {
            if (!string.IsNullOrEmpty(contextId))
            {
                var overridePath = GetOverridePath(contextId, patternId);
                if (File.Exists(overridePath))
                    return overridePath;
            }
            return catalog.GetPatternPath(patternId);
        }

        /// <summary>
        /// Resolve a Wave YAML path: override first, then catalog fallback.
        /// </summary>
        public static string ResolveWavePath(STGCatalog catalog, string contextId, string waveId)
        {
            if (!string.IsNullOrEmpty(contextId))
            {
                var overridePath = GetOverridePath(contextId, waveId);
                if (File.Exists(overridePath))
                    return overridePath;
            }
            return catalog.GetWavePath(waveId);
        }

        // ── Write / Delete ──

        /// <summary>
        /// Save an override file. Creates the directory structure if needed.
        /// </summary>
        public static void SaveOverride(string contextId, string resourceId, string yamlContent)
        {
            var path = GetOverridePath(contextId, resourceId);
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, yamlContent);
            Debug.Log($"[OverrideManager] Saved override: {contextId}/{resourceId}");
        }

        /// <summary>
        /// Delete an override file (revert to original).
        /// Returns true if a file was actually deleted.
        /// </summary>
        public static bool DeleteOverride(string contextId, string resourceId)
        {
            var path = GetOverridePath(contextId, resourceId);
            if (!File.Exists(path)) return false;

            File.Delete(path);
            Debug.Log($"[OverrideManager] Deleted override: {contextId}/{resourceId}");

            // Clean up empty context directory
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0)
                Directory.Delete(dir);

            return true;
        }

        /// <summary>
        /// List all overridden resource IDs for a given context.
        /// </summary>
        public static List<string> GetOverridesForContext(string contextId)
        {
            var result = new List<string>();
            var dir = Path.Combine(ModifiedDir, contextId);
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.yaml"))
            {
                result.Add(Path.GetFileNameWithoutExtension(file));
            }
            return result;
        }

        /// <summary>
        /// Copy an override to the original resource directory as a new template.
        /// Registers the new resource in the catalog.
        /// Returns the new resource ID.
        /// </summary>
        public static string SaveAsNewTemplate(
            STGCatalog catalog, string contextId, string resourceId,
            string newId, string resourceType)
        {
            var overridePath = GetOverridePath(contextId, resourceId);
            if (!File.Exists(overridePath))
            {
                Debug.LogWarning($"[OverrideManager] No override to save as template: {contextId}/{resourceId}");
                return null;
            }

            string targetDir;
            switch (resourceType.ToLower())
            {
                case "spellcard": targetDir = STGCatalog.SpellCardsDir; break;
                case "pattern":   targetDir = STGCatalog.PatternsDir;   break;
                case "wave":      targetDir = STGCatalog.WavesDir;      break;
                default:
                    Debug.LogError($"[OverrideManager] Unknown resource type: {resourceType}");
                    return null;
            }

            var targetPath = Path.Combine(targetDir, $"{newId}.yaml");
            if (File.Exists(targetPath))
            {
                Debug.LogWarning($"[OverrideManager] Target already exists: {targetPath}");
                return null;
            }

            File.Copy(overridePath, targetPath);

            // Register in catalog
            switch (resourceType.ToLower())
            {
                case "spellcard": catalog.AddOrUpdateSpellCard(newId, newId); break;
                case "pattern":   catalog.AddOrUpdatePattern(newId, newId);   break;
                case "wave":      catalog.AddOrUpdateWave(newId, newId);      break;
            }
            STGCatalog.Save(catalog);

            Debug.Log($"[OverrideManager] Saved as new template: {newId} ({resourceType})");
            return newId;
        }

        // ── Context ID helpers ──

        /// <summary>
        /// Build a context ID for a BossFight segment.
        /// Format: "{segmentId}" — SpellCards within this segment can be overridden.
        /// </summary>
        public static string SegmentContext(string segmentId) => segmentId;

        /// <summary>
        /// Build a per-instance context ID for a SpellCard within a BossFight.
        /// Format: "{segmentId}/sc_{index}" — isolates overrides when the same scId appears multiple times.
        /// </summary>
        public static string SpellCardInstanceContext(string segmentId, int index) =>
            $"{segmentId}/sc_{index}";

        /// <summary>
        /// Build a context ID for patterns within a SpellCard instance.
        /// Format: "{segmentId}/sc_{index}/{spellCardId}" — Patterns within this SC instance can be overridden.
        /// </summary>
        public static string SpellCardContext(string segmentId, int index, string spellCardId) =>
            $"{segmentId}/sc_{index}/{spellCardId}";
    }
}
