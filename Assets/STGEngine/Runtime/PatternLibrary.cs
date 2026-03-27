using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;

namespace STGEngine.Runtime
{
    /// <summary>
    /// Loads and caches BulletPattern files from Resources/STGData/Patterns/
    /// and legacy Resources/DefaultPatterns/.
    /// Used by the timeline system to resolve pattern references by ID.
    /// </summary>
    public class PatternLibrary
    {
        private readonly Dictionary<string, BulletPattern> _cache = new();
        private bool _scanned;

        /// <summary>All loaded pattern IDs.</summary>
        public IEnumerable<string> PatternIds
        {
            get
            {
                EnsureScanned();
                return _cache.Keys;
            }
        }

        /// <summary>Number of loaded patterns.</summary>
        public int Count
        {
            get
            {
                EnsureScanned();
                return _cache.Count;
            }
        }

        /// <summary>
        /// Resolve a pattern by ID. Returns the cached (shared) instance. Null if not found.
        /// WARNING: Do NOT mutate the returned object — it is shared across all references.
        /// Use ResolveClone() when you need an editable copy.
        /// </summary>
        public BulletPattern Resolve(string patternId)
        {
            EnsureScanned();
            return _cache.TryGetValue(patternId, out var pattern) ? pattern : null;
        }

        /// <summary>
        /// Resolve a pattern by ID and return a deep clone (serialize → deserialize).
        /// Safe to mutate without affecting other references or the cache.
        /// Returns null if not found or clone fails.
        /// </summary>
        public BulletPattern ResolveClone(string patternId)
        {
            var original = Resolve(patternId);
            if (original == null) return null;
            try
            {
                var yaml = YamlSerializer.Serialize(original);
                return YamlSerializer.Deserialize(yaml);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PatternLibrary] Failed to clone pattern '{patternId}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all loaded patterns.
        /// </summary>
        public IEnumerable<BulletPattern> GetAll()
        {
            EnsureScanned();
            return _cache.Values;
        }

        /// <summary>
        /// Force rescan of pattern files.
        /// </summary>
        public void Refresh()
        {
            _scanned = false;
            _cache.Clear();
            EnsureScanned();
        }

        /// <summary>
        /// Manually register a pattern (e.g. newly created in editor).
        /// </summary>
        public void Register(BulletPattern pattern)
        {
            if (!string.IsNullOrEmpty(pattern.Id))
                _cache[pattern.Id] = pattern;
        }

        private void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;

            // Scan new STGData/Patterns/ directory (primary)
            ScanResourceFolder("STGData/Patterns");

            // Scan legacy DefaultPatterns/ directory (backward compat)
            ScanResourceFolder("DefaultPatterns");

            Debug.Log($"[PatternLibrary] Loaded {_cache.Count} patterns.");
        }

        private void ScanResourceFolder(string resourcePath)
        {
            var assets = Resources.LoadAll<TextAsset>(resourcePath);
            foreach (var asset in assets)
            {
                try
                {
                    var pattern = YamlSerializer.Deserialize(asset.text);
                    if (!string.IsNullOrEmpty(pattern.Id) && !_cache.ContainsKey(pattern.Id))
                        _cache[pattern.Id] = pattern;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[PatternLibrary] Failed to load pattern '{asset.name}': {e.Message}");
                }
            }
        }
    }
}
