using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;

namespace STGEngine.Runtime
{
    /// <summary>
    /// Loads and caches BulletPattern files from Resources/DefaultPatterns/.
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
        /// Resolve a pattern by ID. Returns null if not found.
        /// </summary>
        public BulletPattern Resolve(string patternId)
        {
            EnsureScanned();
            return _cache.TryGetValue(patternId, out var pattern) ? pattern : null;
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

            var assets = Resources.LoadAll<TextAsset>("DefaultPatterns");
            foreach (var asset in assets)
            {
                try
                {
                    var pattern = YamlSerializer.Deserialize(asset.text);
                    if (!string.IsNullOrEmpty(pattern.Id))
                        _cache[pattern.Id] = pattern;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[PatternLibrary] Failed to load pattern '{asset.name}': {e.Message}");
                }
            }

            Debug.Log($"[PatternLibrary] Loaded {_cache.Count} patterns.");
        }
    }
}
