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

        public string DisplayLabel => string.IsNullOrEmpty(Name) ? Id.Substring(0, Math.Min(8, Id.Length)) : Name;
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
        public List<CatalogEntry> EnemyTypes = new();
        public List<CatalogEntry> Waves = new();
        public List<CatalogEntry> SpellCards = new();

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

        public static string EnemyTypesDir =>
            Path.Combine(BasePath, "EnemyTypes");

        public static string WavesDir =>
            Path.Combine(BasePath, "Waves");

        public static string SpellCardsDir =>
            Path.Combine(BasePath, "SpellCards");

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

        public CatalogEntry FindEnemyType(string id) =>
            EnemyTypes.FirstOrDefault(e => e.Id == id);

        public CatalogEntry FindWave(string id) =>
            Waves.FirstOrDefault(e => e.Id == id);

        public CatalogEntry FindSpellCard(string id) =>
            SpellCards.FirstOrDefault(e => e.Id == id);

        public void AddOrUpdatePattern(string id, string name)
        {
            var entry = FindPattern(id);
            if (entry != null)
            {
                entry.Name = name;
                return;
            }
            var slug = NameToSlug(name);
            slug = EnsureUniquePatternFile(slug);
            Patterns.Add(new CatalogEntry { Id = id, Name = name, File = $"Patterns/{slug}.yaml" });
        }

        public void AddOrUpdateStage(string id, string name)
        {
            var entry = FindStage(id);
            if (entry != null)
            {
                entry.Name = name;
                return;
            }
            var slug = NameToSlug(name);
            slug = EnsureUniqueStageFile(slug);
            Stages.Add(new CatalogEntry { Id = id, Name = name, File = $"Stages/{slug}.yaml" });
        }

        public void AddOrUpdateEnemyType(string id, string name)
        {
            var entry = FindEnemyType(id);
            if (entry != null)
            {
                entry.Name = name;
                return;
            }
            var slug = NameToSlug(name);
            slug = EnsureUniqueEnemyTypeFile(slug);
            EnemyTypes.Add(new CatalogEntry { Id = id, Name = name, File = $"EnemyTypes/{slug}.yaml" });
        }

        public void AddOrUpdateWave(string id, string name)
        {
            var entry = FindWave(id);
            if (entry != null)
            {
                entry.Name = name;
                return;
            }
            var slug = NameToSlug(name);
            slug = EnsureUniqueWaveFile(slug);
            Waves.Add(new CatalogEntry { Id = id, Name = name, File = $"Waves/{slug}.yaml" });
        }

        public void AddOrUpdateSpellCard(string id, string name)
        {
            var entry = FindSpellCard(id);
            if (entry != null)
            {
                entry.Name = name;
                return;
            }
            var slug = NameToSlug(name);
            slug = EnsureUniqueSpellCardFile(slug);
            SpellCards.Add(new CatalogEntry { Id = id, Name = name, File = $"SpellCards/{slug}.yaml" });
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

        public bool RemoveEnemyType(string id)
        {
            var entry = FindEnemyType(id);
            if (entry == null) return false;

            var absPath = Path.Combine(BasePath, entry.File);
            if (System.IO.File.Exists(absPath))
                System.IO.File.Delete(absPath);

            EnemyTypes.Remove(entry);
            return true;
        }

        public bool RemoveWave(string id)
        {
            var entry = FindWave(id);
            if (entry == null) return false;

            var absPath = Path.Combine(BasePath, entry.File);
            if (System.IO.File.Exists(absPath))
                System.IO.File.Delete(absPath);

            Waves.Remove(entry);
            return true;
        }

        public bool RemoveSpellCard(string id)
        {
            var entry = FindSpellCard(id);
            if (entry == null) return false;

            var absPath = Path.Combine(BasePath, entry.File);
            if (System.IO.File.Exists(absPath))
                System.IO.File.Delete(absPath);

            SpellCards.Remove(entry);
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

        /// <summary>Absolute disk path for an enemy type entry.</summary>
        public string GetEnemyTypePath(string id)
        {
            var entry = FindEnemyType(id);
            return entry != null
                ? Path.Combine(BasePath, entry.File)
                : Path.Combine(EnemyTypesDir, $"{id}.yaml");
        }

        /// <summary>Absolute disk path for a wave entry.</summary>
        public string GetWavePath(string id)
        {
            var entry = FindWave(id);
            return entry != null
                ? Path.Combine(BasePath, entry.File)
                : Path.Combine(WavesDir, $"{id}.yaml");
        }

        /// <summary>Absolute disk path for a spell card entry.</summary>
        public string GetSpellCardPath(string id)
        {
            var entry = FindSpellCard(id);
            return entry != null
                ? Path.Combine(BasePath, entry.File)
                : Path.Combine(SpellCardsDir, $"{id}.yaml");
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

        /// <summary>Convert a display name to a filesystem-safe slug for file naming.</summary>
        public static string NameToSlug(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return $"unnamed_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            var slug = name.Trim().ToLowerInvariant();
            slug = Regex.Replace(slug, @"[^a-z0-9\u4e00-\u9fff]+", "_");
            slug = slug.Trim('_');
            if (string.IsNullOrEmpty(slug)) slug = $"unnamed_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            return slug;
        }

        /// <summary>
        /// Ensure the ID is unique within a list by appending _2, _3, etc.
        /// </summary>
        public string EnsureUniquePatternId(string _) => Guid.NewGuid().ToString("N").Substring(0, 12);

        public string EnsureUniqueStageId(string _) => Guid.NewGuid().ToString("N").Substring(0, 12);

        public string EnsureUniqueEnemyTypeId(string _) => Guid.NewGuid().ToString("N").Substring(0, 12);

        public string EnsureUniqueWaveId(string _) => Guid.NewGuid().ToString("N").Substring(0, 12);

        public string EnsureUniqueSpellCardId(string _) => Guid.NewGuid().ToString("N").Substring(0, 12);

        // ─── File Slug Uniqueness ───

        public string EnsureUniquePatternFile(string slug)
        {
            var path = Path.Combine(PatternsDir, $"{slug}.yaml");
            if (!System.IO.File.Exists(path) && Patterns.All(e => e.File != $"Patterns/{slug}.yaml")) return slug;
            for (int i = 2; i < 100; i++)
            {
                var candidate = $"{slug}_{i}";
                var candidatePath = Path.Combine(PatternsDir, $"{candidate}.yaml");
                if (!System.IO.File.Exists(candidatePath) && Patterns.All(e => e.File != $"Patterns/{candidate}.yaml"))
                    return candidate;
            }
            return $"{slug}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        public string EnsureUniqueStageFile(string slug)
        {
            var path = Path.Combine(StagesDir, $"{slug}.yaml");
            if (!System.IO.File.Exists(path) && Stages.All(e => e.File != $"Stages/{slug}.yaml")) return slug;
            for (int i = 2; i < 100; i++)
            {
                var candidate = $"{slug}_{i}";
                var candidatePath = Path.Combine(StagesDir, $"{candidate}.yaml");
                if (!System.IO.File.Exists(candidatePath) && Stages.All(e => e.File != $"Stages/{candidate}.yaml"))
                    return candidate;
            }
            return $"{slug}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        public string EnsureUniqueEnemyTypeFile(string slug)
        {
            var path = Path.Combine(EnemyTypesDir, $"{slug}.yaml");
            if (!System.IO.File.Exists(path) && EnemyTypes.All(e => e.File != $"EnemyTypes/{slug}.yaml")) return slug;
            for (int i = 2; i < 100; i++)
            {
                var candidate = $"{slug}_{i}";
                var candidatePath = Path.Combine(EnemyTypesDir, $"{candidate}.yaml");
                if (!System.IO.File.Exists(candidatePath) && EnemyTypes.All(e => e.File != $"EnemyTypes/{candidate}.yaml"))
                    return candidate;
            }
            return $"{slug}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        public string EnsureUniqueWaveFile(string slug)
        {
            var path = Path.Combine(WavesDir, $"{slug}.yaml");
            if (!System.IO.File.Exists(path) && Waves.All(e => e.File != $"Waves/{slug}.yaml")) return slug;
            for (int i = 2; i < 100; i++)
            {
                var candidate = $"{slug}_{i}";
                var candidatePath = Path.Combine(WavesDir, $"{candidate}.yaml");
                if (!System.IO.File.Exists(candidatePath) && Waves.All(e => e.File != $"Waves/{candidate}.yaml"))
                    return candidate;
            }
            return $"{slug}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        public string EnsureUniqueSpellCardFile(string slug)
        {
            var path = Path.Combine(SpellCardsDir, $"{slug}.yaml");
            if (!System.IO.File.Exists(path) && SpellCards.All(e => e.File != $"SpellCards/{slug}.yaml")) return slug;
            for (int i = 2; i < 100; i++)
            {
                var candidate = $"{slug}_{i}";
                var candidatePath = Path.Combine(SpellCardsDir, $"{candidate}.yaml");
                if (!System.IO.File.Exists(candidatePath) && SpellCards.All(e => e.File != $"SpellCards/{candidate}.yaml"))
                    return candidate;
            }
            return $"{slug}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
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
            AppendCatalogSection(sb, "patterns", catalog.Patterns);
            AppendCatalogSection(sb, "stages", catalog.Stages);
            AppendCatalogSection(sb, "enemy_types", catalog.EnemyTypes);
            AppendCatalogSection(sb, "waves", catalog.Waves);
            AppendCatalogSection(sb, "spell_cards", catalog.SpellCards);
            return sb.ToString();
        }

        private static void AppendCatalogSection(StringBuilder sb, string sectionName, List<CatalogEntry> entries)
        {
            sb.AppendLine($"{sectionName}:");
            foreach (var e in entries)
            {
                sb.AppendLine($"  - id: {e.Id}");
                sb.AppendLine($"    name: \"{EscapeYaml(e.Name)}\"");
                sb.AppendLine($"    file: {e.File}");
            }
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
                if (trimmed == "enemy_types:")
                {
                    currentList = catalog.EnemyTypes;
                    currentEntry = null;
                    continue;
                }
                if (trimmed == "waves:")
                {
                    currentList = catalog.Waves;
                    currentEntry = null;
                    continue;
                }
                if (trimmed == "spell_cards:")
                {
                    currentList = catalog.SpellCards;
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
            Directory.CreateDirectory(EnemyTypesDir);
            Directory.CreateDirectory(WavesDir);
            Directory.CreateDirectory(SpellCardsDir);
        }
    }
}
