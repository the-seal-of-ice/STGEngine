using UnityEngine;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Unified abstraction for a "block" on any timeline layer.
    /// Wraps the underlying data object (TimelineEvent, SpellCard, TimelineSegment, etc.)
    /// and exposes display/interaction properties for TrackAreaView.
    /// </summary>
    public interface ITimelineBlock
    {
        /// <summary>Unique identifier for this block.</summary>
        string Id { get; }

        /// <summary>Short label displayed inside the block.</summary>
        string DisplayLabel { get; }

        /// <summary>Start time relative to the layer's beginning (seconds).</summary>
        float StartTime { get; set; }

        /// <summary>Duration of this block (seconds). Represents HardLimit.</summary>
        float Duration { get; set; }

        /// <summary>Block fill color.</summary>
        Color BlockColor { get; }

        /// <summary>
        /// If false, the block is part of a sequential queue (Segment, SpellCard).
        /// Dragging reorders instead of moving freely.
        /// </summary>
        bool CanMove { get; }

        /// <summary>
        /// Designer's estimated actual duration (seconds).
        /// -1 means unset. Displayed as a green vertical line inside the block.
        /// </summary>
        float DesignEstimate { get; set; }

        /// <summary>The underlying data object (TimelineEvent, SpellCard, TimelineSegment, etc.).</summary>
        object DataSource { get; }
    }
}
