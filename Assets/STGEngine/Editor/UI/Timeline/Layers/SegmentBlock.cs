using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a TimelineSegment within a Stage.
    /// Sequential layout: StartTime is computed from sum of preceding segment Durations.
    /// Duration = segment.Duration (HardLimit). DesignEstimate from segment data.
    /// CanMove = false (drag reorders in sequential queue).
    /// </summary>
    public class SegmentBlock : ITimelineBlock
    {
        private readonly TimelineSegment _segment;
        private float _startTime;

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
                var typeTag = _segment.Type == SegmentType.BossFight ? "\u2694" : "\u25B6"; // ⚔ or ▶
                return $"{typeTag} {_segment.Name}";
            }
        }

        public float StartTime
        {
            get => _startTime;
            set => _startTime = value; // Recalculated by StageLayer on reorder
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
                    return new Color(0.55f, 0.25f, 0.55f); // Purple for BossFight
                return new Color(0.25f, 0.4f, 0.55f); // Blue for MidStage
            }
        }

        public bool CanMove => false; // Sequential queue — drag reorders

        public float DesignEstimate
        {
            get => _segment.DesignEstimate;
            set => _segment.DesignEstimate = value;
        }

        public object DataSource => _segment;
    }
}
