using System;
using System.Collections.Generic;
using STGEngine.Core.DataModel;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Segment type determines time model.
    /// </summary>
    public enum SegmentType
    {
        /// <summary>Absolute timeline (scrolling stage).</summary>
        MidStage,
        /// <summary>Event-driven timeline (boss fight).</summary>
        BossFight
    }

    /// <summary>
    /// A timeline segment: stages are composed of sequential/conditional segments.
    /// Each segment has its own local timeline with events.
    /// </summary>
    public class TimelineSegment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Name { get; set; } = "";
        public SegmentType Type { get; set; } = SegmentType.MidStage;

        /// <summary>Local duration in seconds. For MidStage this is exact; for BossFight it's an estimate.</summary>
        public float Duration { get; set; } = 30f;

        /// <summary>
        /// Designer's estimated actual duration (seconds).
        /// -1 means unset; defaults to Duration × 0.8 at display time.
        /// Displayed as a green vertical line inside the block.
        /// </summary>
        public float DesignEstimate { get; set; } = -1f;

        /// <summary>Entry trigger from previous segment. Null for the first segment.</summary>
        public TriggerCondition EntryTrigger { get; set; }

        /// <summary>Events within this segment's local timeline (used by MidStage segments).</summary>
        public List<TimelineEvent> Events { get; set; } = new();

        /// <summary>
        /// Spell card IDs for BossFight segments. Each references an independent SpellCard YAML file.
        /// Spell cards execute sequentially; each ends when health is depleted or time runs out.
        /// Ignored for MidStage segments.
        /// </summary>
        public List<string> SpellCardIds { get; set; } = new();

        // ── Phase 5: segment-level modifiers ──

        /// <summary>Difficulty filter. Segment is skipped if current difficulty is not in this mask.</summary>
        public DifficultyFilter Difficulty { get; set; } = DifficultyFilter.All;

        /// <summary>Number of times this segment repeats. 1 = play once (no repeat).</summary>
        public int RepeatCount { get; set; } = 1;

        /// <summary>Force player power to this value at segment start. -1 = no change.</summary>
        public int PowerOverride { get; set; } = -1;

        /// <summary>Boss entrance path for BossFight segments. Null = no entrance animation.</summary>
        public List<PathKeyframe> BossEntrancePath { get; set; } = null;
    }
}
