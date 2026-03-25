using System.Collections.Generic;

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
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public SegmentType Type { get; set; } = SegmentType.MidStage;

        /// <summary>Local duration in seconds. For MidStage this is exact; for BossFight it's an estimate.</summary>
        public float Duration { get; set; } = 30f;

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
    }
}
