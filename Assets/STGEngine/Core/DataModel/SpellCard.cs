using System;
using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// A single attack phase within a BossFight segment.
    /// Contains bullet patterns, boss movement path, health, and time limit.
    /// The spell card ends when health is depleted or time runs out.
    /// </summary>
    public class SpellCard
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Name { get; set; } = "";

        // ── Bullet configuration ──

        /// <summary>Bullet patterns used in this spell card, arranged on a local timeline.</summary>
        public List<SpellCardPattern> Patterns { get; set; } = new();

        // ── Boss behavior ──

        /// <summary>Boss movement path keyframes (time → position). Linear interpolation.</summary>
        public List<PathKeyframe> BossPath { get; set; } = new();

        // ── Phase parameters ──

        /// <summary>Time limit in seconds. Spell card ends when exceeded.</summary>
        public float TimeLimit { get; set; } = 30f;

        /// <summary>Health pool. Spell card is "captured" when depleted.</summary>
        public float Health { get; set; } = 1000f;

        // ── Duration semantics ──

        /// <summary>
        /// Designer's estimated actual duration (seconds).
        /// -1 means unset; defaults to TimeLimit × 0.7 at display time.
        /// Displayed as a green vertical line inside the block.
        /// </summary>
        public float DesignEstimate { get; set; } = -1f;

        /// <summary>
        /// Transition duration after this spell card ends (seconds).
        /// Covers bullet-clear, boss reposition tween, etc.
        /// Displayed as a special narrow block between spell cards.
        /// </summary>
        public float TransitionDuration { get; set; } = 1.5f;
    }

    /// <summary>
    /// A bullet pattern entry within a spell card's local timeline.
    /// References a Pattern file by ID.
    /// </summary>
    public class SpellCardPattern
    {
        /// <summary>Referenced BulletPattern file ID.</summary>
        public string PatternId { get; set; } = "";

        /// <summary>Delay from spell card start before this pattern activates (seconds).</summary>
        public float Delay { get; set; }

        /// <summary>How long this pattern runs (seconds).</summary>
        public float Duration { get; set; } = 5f;

        /// <summary>Spawn position offset relative to boss position.</summary>
        public Vector3 Offset { get; set; }
    }
}
