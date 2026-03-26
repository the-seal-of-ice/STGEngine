using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Timeline;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Lightweight data for a sub-block thumbnail bar inside a parent block.
    /// </summary>
    public struct ThumbnailBar
    {
        /// <summary>Normalized start position (0..1) within the parent block.</summary>
        public float NormalizedStart;
        /// <summary>Normalized width (0..1) within the parent block.</summary>
        public float NormalizedWidth;
        /// <summary>Color of this bar.</summary>
        public Color Color;
        /// <summary>Row index for vertical stacking (0-based).</summary>
        public int Row;
    }

    /// <summary>
    /// ITimelineBlock wrapper for a TimelineSegment within a Stage.
    /// Sequential layout: StartTime is computed from sum of preceding segment Durations.
    /// Duration = segment.Duration (HardLimit). DesignEstimate from segment data.
    /// CanMove = false (drag reorders in sequential queue).
    /// Thumbnail: draws sub-event/spell-card color bars.
    /// </summary>
    public class SegmentBlock : ITimelineBlock
    {
        private readonly TimelineSegment _segment;
        private float _startTime;
        private List<ThumbnailBar> _thumbnailBars;

        public SegmentBlock(TimelineSegment segment, float startTime)
        {
            _segment = segment;
            _startTime = startTime;
        }

        public string Id => _segment.Id;

        public string DisplayLabel
        {
            get
            {
                var typeTag = _segment.Type == SegmentType.BossFight ? "\u2694" : "\u25B6";
                return $"{typeTag} {_segment.Name}";
            }
        }

        public float StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        public float Duration
        {
            get => _segment.Duration;
            set => _segment.Duration = Mathf.Max(1f, value);
        }

        public Color BlockColor
        {
            get
            {
                if (_segment.Type == SegmentType.BossFight)
                    return new Color(0.55f, 0.25f, 0.55f);
                return new Color(0.25f, 0.4f, 0.55f);
            }
        }

        public bool CanMove => false;

        public float DesignEstimate
        {
            get => _segment.DesignEstimate;
            set => _segment.DesignEstimate = value;
        }

        public object DataSource => _segment;

        // ── Thumbnail ──

        /// <summary>
        /// Set pre-computed thumbnail bars for this segment's sub-blocks.
        /// Called by StageLayer during RebuildBlockList.
        /// </summary>
        public void SetThumbnailBars(List<ThumbnailBar> bars)
        {
            _thumbnailBars = bars;
        }

        public bool HasThumbnail => _thumbnailBars != null && _thumbnailBars.Count > 0;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_thumbnailBars == null || _thumbnailBars.Count == 0) return;

            // Find max row for vertical distribution
            int maxRow = 0;
            foreach (var bar in _thumbnailBars)
                if (bar.Row > maxRow) maxRow = bar.Row;

            float barAreaTop = 14f; // Leave space for label
            float barAreaHeight = blockHeight - barAreaTop - 2f;
            if (barAreaHeight < 2f) return;

            float rowHeight = barAreaHeight / (maxRow + 1);
            rowHeight = Mathf.Min(rowHeight, 8f); // Cap row height

            foreach (var bar in _thumbnailBars)
            {
                float x = bar.NormalizedStart * blockWidth;
                float w = Mathf.Max(bar.NormalizedWidth * blockWidth, 2f);
                float y = barAreaTop + bar.Row * rowHeight;
                float h = Mathf.Max(rowHeight - 1f, 2f);

                var color = bar.Color;
                color.a = 0.6f; // Reduced opacity for thumbnail
                painter.fillColor = color;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + w, y));
                painter.LineTo(new Vector2(x + w, y + h));
                painter.LineTo(new Vector2(x, y + h));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
