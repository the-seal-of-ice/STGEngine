using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;

namespace STGEngine.Editor.UI.FileManager
{
    /// <summary>
    /// A single entry in the catalog index.
    /// </summary>
    [Serializable]
    public class CatalogEntry
    {
        public string Id;
        public string Name;
        /// <summary>Relative path under STGData/, e.g. "Patterns/demo_ring_wave.yaml".</summary>
        public string File;

        public string DisplayLabel => string.IsNullOrEmpty(Name) ? Id : $"{Name}  ({Id})";
    }

    /// <summary>
    /// Central catalog that indexes all Pattern and Stage YAML files under
    /// Assets/Resources/STGData/. Provides load, save, add, remove, and
    /// first-run migration from legacy directories.
    /// </summary>
    public class STGCatalog
    {
        public List<CatalogEntry> Patterns = new();
        public List<CatalogEntry> Stages = new();

        // ─── Paths ───

        /// <summary>Absolute path to Assets/Resources/STGData/.</summary>
        public static string BasePath =>
            Path.Combine(Application.dataPath, "Resources", "STGData");

        public static string CatalogFilePath =>
            Path.Combine(BasePath, "catalog.yaml");

        public static string PatternsDir =>
            Path.Combine(BasePath, "Patterns");

        public static string StagesDir =>
            Path.Combine(BasePath, "Stages");

        // ─── Load / Save ───

        /// <summary>
        /// Load the catalog from disk. If it doesn't exist, run migration
        /// from legacy directories and create it.
        /// </summary>
        public static STGCatalog Load()
        {
            EnsureDirectories();

            if (System.IO.File.Exists(CatalogFilePath))
            {
                try
                {
                    var yaml = System.IO.File.ReadAllText(CatalogFilePath);
                    return ParseCatalog(yaml);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[STGCatalog] Failed to parse catalog: {e.Message}");
                }
            }

            // First run — migrate legacy files
            var catalog = new STGCatalog();
            catalog.MigrateFromLegacy();
            Save(catalog);
            return catalog;
        }

        public static void Save(STGCatalog catalog)
        {
            EnsureDirectories();
            var yaml = SerializeCatalog(catalog);
            System.IO.File.WriteAllText(CatalogFilePath, yaml);
        }

        // ─── Entry Management ───

        public CatalogEntry FindPattern(string id) =>
            Patterns.FirstOrDefault(e => e.Id == id);

        public CatalogEntry FindStage(string id) =>
            Stages.FirstOrDefault(e => e.Id == id);

        public void AddOrUpdatePattern(string id, string name)
        {
            var entry = FindPattern(id);
            if (entry != null)
            {
                entry.Name = name;
            }
            else
            {
                Patterns.Add(new CatalogEntry
                {
                    Id = id,
                    Name = name,
                    File = $"Patterns/{id}.yaml"
                });
            }
        }

        public void AddOrUpdateStage(string id, string name)
        {
            var entry = FindStage(id);
            if (entry != null)
            {
                entry.Name = name;
            }
            else
            {
                Stages.Add(new CatalogEntry
                {
                    Id = id,
                    Name = name,
                    File = $"Stages/{id}.yaml"
                });
            }
        }

        public bool RemovePattern(string id)
        {
            var entry = FindPattern(id);
            if (entry == null) return false;

            var absPath = Path.Combine(BasePath, entry.File);
            if (System.IO.File.Exists(absPath))
                System.IO.File.Delete(absPath);

            Patterns.Remove(entry);
            return true;
        }

        public bool RemoveStage(string id)
        {
            var entry = FindStage(id);
            if (entry == null) return false;

            var absPath = Path.Combine(BasePath, entry.File);
            if (System.IO.File.Exists(absPath))
                System.IO.File.Delete(absPath);

            Stages.Remove(entry);
            return true;
        }

        /// <summary>Absolute disk path for a pattern entry.</summary>
        public string GetPatternPath(string id)
        {
            var entry = FindPattern(id);
            return entry != null
                ? Path.Combine(BasePath, entry.File)
                : Path.Combine(PatternsDir, $"{id}.yaml");
        }

        /// <summary>Absolute disk path for a stage entry.</summary>
        public string GetStagePath(string id)
        {
            var entry = FindStage(id);
            return entry != null
                ? Path.Combine(BasePath, entry.File)
                : Path.Combine(StagesDir, $"{id}.yaml");
        }

        // ─── ID Generation ───

        /// <summary>
        /// Convert a display name to a valid snake_case ID.
        /// "Ring Wave Demo" → "ring_wave_demo"
        /// </summary>
        public static string NameToId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";
            var s = name.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"[^a-z0-9]+", "_");
            s = s.Trim('_');
            return string.IsNullOrEmpty(s) ? "unnamed" : s;
        }

        /// <summary>
        /// Ensure the ID is unique within a list by appending _2, _3, etc.
        /// </summary>
        public string EnsureUniquePatternId(string baseId)
        {
            if (FindPattern(baseId) == null) return baseId;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseId}_{i}";
                if (FindPattern(candidate) == null) return candidate;
            }
            return $"{baseId}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        public string EnsureUniqueStageId(string baseId)
        {
            if (FindStage(baseId) == null) return baseId;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseId}_{i}";
                if (FindStage(candidate) == null) return candidate;
            }
            return $"{baseId}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        // ─── Migration ───

        private void MigrateFromLegacy()
        {
            // Migrate patterns from Resources/DefaultPatterns/
            var legacyPatterns = Path.Combine(Application.dataPath, "Resources", "DefaultPatterns");
            if (Directory.Exists(legacyPatterns))
            {
                foreach (var file in Directory.GetFiles(legacyPatterns, "*.yaml"))
                {
                    try
                    {
                        var yaml = System.IO.File.ReadAllText(file);
                        var pattern = YamlSerializer.Deserialize(yaml);
                        var id = !string.IsNullOrEmpty(pattern.Id) ? pattern.Id : Path.GetFileNameWithoutExtension(file);
                        var name = !string.IsNullOrEmpty(pattern.Name) ? pattern.Name : id;

                        var destPath = Path.Combine(PatternsDir, $"{id}.yaml");
                        if (!System.IO.File.Exists(destPath))
                            System.IO.File.Copy(file, destPath);

                        Patterns.Add(new CatalogEntry { Id = id, Name = name, File = $"Patterns/{id}.yaml" });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[STGCatalog] Migration skip {file}: {e.Message}");
                    }
                }
            }

            // Migrate stages from Resources/Stages/
            var legacyStages = Path.Combine(Application.dataPath, "Resources", "Stages");
            if (Directory.Exists(legacyStages))
            {
                foreach (var file in Directory.GetFiles(legacyStages, "*.yaml"))
                {
                    try
                    {
                        var yaml = System.IO.File.ReadAllText(file);
                        var stage = YamlSerializer.DeserializeStage(yaml);
                        var id = !string.IsNullOrEmpty(stage.Id) ? stage.Id : Path.GetFileNameWithoutExtension(file);
                        var name = !string.IsNullOrEmpty(stage.Name) ? stage.Name : id;

                        var destPath = Path.Combine(StagesDir, $"{id}.yaml");
                        if (!System.IO.File.Exists(destPath))
                            System.IO.File.Copy(file, destPath);

                        Stages.Add(new CatalogEntry { Id = id, Name = name, File = $"Stages/{id}.yaml" });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[STGCatalog] Migration skip {file}: {e.Message}");
                    }
                }
            }

            // Also scan persistentDataPath/Patterns/ (old pattern editor saves)
            var oldPatterns = Path.Combine(Application.persistentDataPath, "Patterns");
            if (Directory.Exists(oldPatterns))
            {
                foreach (var file in Directory.GetFiles(oldPatterns, "*.yaml"))
                {
                    try
                    {
                        var yaml = System.IO.File.ReadAllText(file);
                        var pattern = YamlSerializer.Deserialize(yaml);
                        var id = !string.IsNullOrEmpty(pattern.Id) ? pattern.Id : Path.GetFileNameWithoutExtension(file);
                        var name = !string.IsNullOrEmpty(pattern.Name) ? pattern.Name : id;

                        // Skip duplicates
                        if (FindPattern(id) != null) continue;

                        var destPath = Path.Combine(PatternsDir, $"{id}.yaml");
                        if (!System.IO.File.Exists(destPath))
                            System.IO.File.Copy(file, destPath);

                        Patterns.Add(new CatalogEntry { Id = id, Name = name, File = $"Patterns/{id}.yaml" });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[STGCatalog] Migration skip {file}: {e.Message}");
                    }
                }
            }

            Debug.Log($"[STGCatalog] Migrated {Patterns.Count} patterns, {Stages.Count} stages.");
        }

        // ─── Simple YAML Serialization (no dependency on YamlDotNet for catalog) ───

        private static string SerializeCatalog(STGCatalog catalog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# STGEngine Asset Catalog — auto-generated, do not edit manually");
            sb.AppendLine("patterns:");
            foreach (var e in catalog.Patterns)
            {
                sb.AppendLine($"  - id: {e.Id}");
                sb.AppendLine($"    name: \"{EscapeYaml(e.Name)}\"");
                sb.AppendLine($"    file: {e.File}");
            }
            sb.AppendLine("stages:");
            foreach (var e in catalog.Stages)
            {
                sb.AppendLine($"  - id: {e.Id}");
                sb.AppendLine($"    name: \"{EscapeYaml(e.Name)}\"");
                sb.AppendLine($"    file: {e.File}");
            }
            return sb.ToString();
        }

        private static STGCatalog ParseCatalog(string yaml)
        {
            var catalog = new STGCatalog();
            List<CatalogEntry> currentList = null;
            CatalogEntry currentEntry = null;

            foreach (var rawLine in yaml.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed == "patterns:")
                {
                    currentList = catalog.Patterns;
                    currentEntry = null;
                    continue;
                }
                if (trimmed == "stages:")
                {
                    currentList = catalog.Stages;
                    currentEntry = null;
                    continue;
                }

                if (currentList == null) continue;

                if (trimmed.StartsWith("- id:"))
                {
                    currentEntry = new CatalogEntry { Id = ExtractValue(trimmed, "- id:") };
                    currentList.Add(currentEntry);
                    continue;
                }

                if (currentEntry == null) continue;

                if (trimmed.StartsWith("name:"))
                    currentEntry.Name = UnescapeYaml(ExtractValue(trimmed, "name:"));
                else if (trimmed.StartsWith("file:"))
                    currentEntry.File = ExtractValue(trimmed, "file:");
            }

            return catalog;
        }

        private static string ExtractValue(string line, string prefix)
        {
            var val = line.Substring(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length).Trim();
            // Strip surrounding quotes
            if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                val = val.Substring(1, val.Length - 2);
            return val;
        }

        private static string EscapeYaml(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        private static string UnescapeYaml(string s) =>
            s?.Replace("\\\"", "\"").Replace("\\\\", "\\") ?? "";

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(PatternsDir);
            Directory.CreateDirectory(StagesDir);
        }
    }
}
