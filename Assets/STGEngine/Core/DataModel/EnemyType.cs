using System;
using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// A pattern slot on an EnemyType — analogous to SpellCardPattern.
    /// Defines which pattern fires, when it starts, and how long it runs.
    /// </summary>
    public class EnemyPattern
    {
        /// <summary>Referenced BulletPattern file ID.</summary>
        public string PatternId { get; set; } = "";

        /// <summary>Delay before this pattern activates (seconds).</summary>
        public float Delay { get; set; }

        /// <summary>How long this pattern runs (seconds). 0 = use pattern's own duration.</summary>
        public float Duration { get; set; } = 5f;

        /// <summary>Spawn position offset relative to enemy position.</summary>
        public Vector3 Offset { get; set; }
    }

    /// <summary>
    /// Enemy type template — a blueprint for enemy instances in waves.
    /// Defines base stats, carried bullet patterns, and visual appearance.
    /// Stored as independent YAML files for cross-wave reuse.
    /// </summary>
    public class EnemyType
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Name { get; set; } = "";

        // ── Base stats ──

        /// <summary>Hit points.</summary>
        public float Health { get; set; } = 10f;

        /// <summary>Visual scale multiplier.</summary>
        public float Scale { get; set; } = 1f;

        // ── Bullet patterns ──

        /// <summary>Pattern slots this enemy fires (timeline-based).</summary>
        public List<EnemyPattern> Patterns { get; set; } = new();

        /// <summary>Total pattern timeline duration (seconds). Computed, not serialized.</summary>
        [YamlIgnore]
        public float PatternDuration
        {
            get
            {
                float max = 0f;
                foreach (var p in Patterns)
                    max = Mathf.Max(max, p.Delay + p.Duration);
                return max > 0f ? max : 10f;
            }
        }

        // ── Visuals ──

        /// <summary>Tint color.</summary>
        public Color Color { get; set; } = Color.white;

        /// <summary>Mesh shape (reuses bullet mesh types for now).</summary>
        public MeshType MeshType { get; set; } = MeshType.Diamond;
    }
}
