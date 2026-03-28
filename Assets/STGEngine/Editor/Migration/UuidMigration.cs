using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using STGEngine.Editor.UI.FileManager;

namespace STGEngine.Editor.Migration
{
    /// <summary>
    /// One-time migration: converts human-readable IDs to 12-char hex UUIDs.
    /// Run via menu: STGEngine > Migrate to UUID.
    /// </summary>
    public static class UuidMigration
    {
        private static readonly Regex UuidPattern = new(@"^[0-9a-f]{12}$");

        [UnityEditor.MenuItem("STGEngine/Migrate to UUID")]
        public static void Run()
        {
            var catalog = STGCatalog.Load();
            if (catalog == null)
            {
                Debug.LogWarning("[UuidMigration] No catalog found. Nothing to migrate.");
                return;
            }

            // Build old→new ID mapping for all resource types
            var idMap = new Dictionary<string, string>();

            MapIds(catalog.Patterns, idMap, "Pattern");
            MapIds(catalog.Waves, idMap, "Wave");
            MapIds(catalog.EnemyTypes, idMap, "EnemyType");
            MapIds(catalog.SpellCards, idMap, "SpellCard");
            MapIds(catalog.Stages, idMap, "Stage");

            if (idMap.Count == 0)
            {
                Debug.Log("[UuidMigration] All IDs are already UUIDs. Nothing to migrate.");
                return;
            }

            Debug.Log($"[UuidMigration] Migrating {idMap.Count} resources to UUID...");

            // 1. Update catalog entries
            foreach (var entry in catalog.Patterns.Concat(catalog.Waves)
                .Concat(catalog.EnemyTypes).Concat(catalog.SpellCards).Concat(catalog.Stages))
            {
                if (idMap.TryGetValue(entry.Id, out var newId))
                {
                    // Preserve old Id as Name if Name is empty
                    if (string.IsNullOrEmpty(entry.Name))
                        entry.Name = entry.Id;
                    entry.Id = newId;
                }
            }

            // 2. Update all YAML files — replace old IDs with new UUIDs in content
            UpdateYamlFiles(catalog, idMap);

            // 3. Update Override files
            UpdateOverrideFiles(idMap);

            // 4. Save catalog
            STGCatalog.Save(catalog);

            Debug.Log($"[UuidMigration] Migration complete. {idMap.Count} resources migrated.");
        }

        private static void MapIds(List<CatalogEntry> entries, Dictionary<string, string> idMap, string type)
        {
            foreach (var entry in entries)
            {
                if (!UuidPattern.IsMatch(entry.Id))
                {
                    var newId = Guid.NewGuid().ToString("N").Substring(0, 12);
                    idMap[entry.Id] = newId;
                    Debug.Log($"[UuidMigration] {type}: {entry.Id} → {newId}");
                }
            }
        }

        private static void UpdateYamlFiles(STGCatalog catalog, Dictionary<string, string> idMap)
        {
            var basePath = STGCatalog.BasePath;

            // Update Pattern files
            foreach (var entry in catalog.Patterns)
            {
                var path = Path.Combine(basePath, entry.File);
                if (File.Exists(path))
                    ReplaceIdsInFile(path, idMap);
            }

            // Update Wave files
            foreach (var entry in catalog.Waves)
            {
                var path = Path.Combine(basePath, entry.File);
                if (File.Exists(path))
                    ReplaceIdsInFile(path, idMap);
            }

            // Update EnemyType files
            foreach (var entry in catalog.EnemyTypes)
            {
                var path = Path.Combine(basePath, entry.File);
                if (File.Exists(path))
                    ReplaceIdsInFile(path, idMap);
            }

            // Update SpellCard files
            foreach (var entry in catalog.SpellCards)
            {
                var path = Path.Combine(basePath, entry.File);
                if (File.Exists(path))
                    ReplaceIdsInFile(path, idMap);
            }

            // Update Stage files
            foreach (var entry in catalog.Stages)
            {
                var path = Path.Combine(basePath, entry.File);
                if (File.Exists(path))
                    ReplaceIdsInFile(path, idMap);
            }
        }

        private static void ReplaceIdsInFile(string path, Dictionary<string, string> idMap)
        {
            try
            {
                var content = File.ReadAllText(path);
                var original = content;

                // Replace IDs in YAML values — match "key: oldId" patterns
                // Sort by length descending to avoid partial replacements
                foreach (var kv in idMap.OrderByDescending(k => k.Key.Length))
                {
                    // Replace in YAML scalar values: after ": " or in lists "- "
                    // Pattern: word boundary match to avoid partial replacements
                    content = Regex.Replace(content,
                        @"(?<=:\s)" + Regex.Escape(kv.Key) + @"(?=\s*$|(?=\r?\n))",
                        kv.Value, RegexOptions.Multiline);

                    // Replace in YAML list items: "- oldId" 
                    content = Regex.Replace(content,
                        @"(?<=-\s)" + Regex.Escape(kv.Key) + @"(?=\s*$|(?=\r?\n))",
                        kv.Value, RegexOptions.Multiline);

                    // Replace in inline lists: [oldId, oldId2]
                    content = Regex.Replace(content,
                        @"(?<=[\[,]\s*)" + Regex.Escape(kv.Key) + @"(?=\s*[,\]])",
                        kv.Value);
                }

                if (content != original)
                {
                    File.WriteAllText(path, content);
                    Debug.Log($"[UuidMigration] Updated: {path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UuidMigration] Failed to update {path}: {e.Message}");
            }
        }

        private static void UpdateOverrideFiles(Dictionary<string, string> idMap)
        {
            var modifiedDir = OverrideManager.ModifiedDir;
            if (!Directory.Exists(modifiedDir)) return;

            // Walk all override directories and files
            foreach (var contextDir in Directory.GetDirectories(modifiedDir, "*", SearchOption.AllDirectories))
            {
                foreach (var file in Directory.GetFiles(contextDir, "*.yaml"))
                {
                    // 1. Replace IDs inside the file content
                    ReplaceIdsInFile(file, idMap);

                    // 2. Rename the file if its name matches an old ID
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (idMap.TryGetValue(fileName, out var newFileName))
                    {
                        var newPath = Path.Combine(Path.GetDirectoryName(file), $"{newFileName}.yaml");
                        if (!File.Exists(newPath))
                        {
                            File.Move(file, newPath);
                            // Also move .meta file if it exists
                            var metaPath = file + ".meta";
                            var newMetaPath = newPath + ".meta";
                            if (File.Exists(metaPath) && !File.Exists(newMetaPath))
                                File.Move(metaPath, newMetaPath);
                            Debug.Log($"[UuidMigration] Renamed override: {fileName} → {newFileName}");
                        }
                    }
                }
            }

            // 3. Rename context directories if they match old IDs
            // Context dirs can be segment IDs or "segmentId/spellCardId"
            foreach (var dir in Directory.GetDirectories(modifiedDir))
            {
                var dirName = Path.GetFileName(dir);
                if (idMap.TryGetValue(dirName, out var newDirName))
                {
                    var newDir = Path.Combine(modifiedDir, newDirName);
                    if (!Directory.Exists(newDir))
                    {
                        Directory.Move(dir, newDir);
                        Debug.Log($"[UuidMigration] Renamed override dir: {dirName} → {newDirName}");
                    }
                }

                // Check subdirectories (e.g. segmentId/spellCardId)
                var checkDir = Directory.Exists(newDirName != null ? Path.Combine(modifiedDir, newDirName) : dir)
                    ? (newDirName != null ? Path.Combine(modifiedDir, newDirName) : dir)
                    : dir;
                if (Directory.Exists(checkDir))
                {
                    foreach (var subDir in Directory.GetDirectories(checkDir))
                    {
                        var subDirName = Path.GetFileName(subDir);
                        if (idMap.TryGetValue(subDirName, out var newSubDirName))
                        {
                            var newSubDir = Path.Combine(checkDir, newSubDirName);
                            if (!Directory.Exists(newSubDir))
                            {
                                Directory.Move(subDir, newSubDir);
                                Debug.Log($"[UuidMigration] Renamed override subdir: {subDirName} → {newSubDirName}");
                            }
                        }
                    }
                }
            }
        }
    }
}
