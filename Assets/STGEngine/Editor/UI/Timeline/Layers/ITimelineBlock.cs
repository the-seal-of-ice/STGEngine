using UnityEngine;
using UnityEngine.UIElements;

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

        // ── Thumbnail ──

        /// <summary>Whether this block has a thumbnail to draw inside it.</summary>
        bool HasThumbnail { get; }

        /// <summary>
        /// If true, thumbnail is drawn as a small inline icon after the label,
        /// with a hover-to-enlarge popup. If false, drawn as block background.
        /// </summary>
        bool ThumbnailInline { get; }

        /// <summary>
        /// Draw a thumbnail using Painter2D.
        /// Called via generateVisualContent. blockWidth/blockHeight are in pixels.
        /// </summary>
        void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight);
    }
}
