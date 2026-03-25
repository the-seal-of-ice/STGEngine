using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// Enemy type template — a blueprint for enemy instances in waves.
    /// Defines base stats, carried bullet patterns, and visual appearance.
    /// Stored as independent YAML files for cross-wave reuse.
    /// </summary>
    public class EnemyType
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        // ── Base stats ──

        /// <summary>Hit points.</summary>
        public float Health { get; set; } = 10f;

        /// <summary>Movement speed along path (units/sec).</summary>
        public float Speed { get; set; } = 2f;

        /// <summary>Visual scale multiplier.</summary>
        public float Scale { get; set; } = 1f;

        // ── Bullet patterns ──

        /// <summary>Pattern IDs this enemy fires (cycled in order).</summary>
        public List<string> PatternIds { get; set; } = new();

        /// <summary>Delay after spawn before first shot (seconds).</summary>
        public float FireDelay { get; set; } = 0.5f;

        // ── Visuals ──

        /// <summary>Tint color.</summary>
        public Color Color { get; set; } = Color.white;

        /// <summary>Mesh shape (reuses bullet mesh types for now).</summary>
        public MeshType MeshType { get; set; } = MeshType.Diamond;
    }
}
