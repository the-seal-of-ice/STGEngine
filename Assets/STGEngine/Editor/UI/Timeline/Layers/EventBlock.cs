using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a TimelineEvent (SpawnPatternEvent / SpawnWaveEvent).
    /// Used by MidStageLayer. Delegates StartTime/Duration directly to the underlying event.
    /// </summary>
    public class EventBlock : ITimelineBlock
    {
        private readonly TimelineEvent _event;

        public EventBlock(TimelineEvent evt)
        {
            _event = evt;
        }

        public string Id => _event.Id;

        public string DisplayLabel => _event switch
        {
            SpawnPatternEvent sp => sp.PatternId,
            SpawnWaveEvent sw => $"\u2693 {sw.WaveId}",
            _ => _event.Id
        };

        public float StartTime
        {
            get => _event.StartTime;
            set => _event.StartTime = value;
        }

        public float Duration
        {
            get => _event.Duration;
            set => _event.Duration = value;
        }

        public Color BlockColor
        {
            get
            {
                if (_event is SpawnWaveEvent sw)
                {
                    int hash = sw.WaveId?.GetHashCode() ?? 0;
                    float hue = 0.3f + Mathf.Abs(hash % 60) / 360f;
                    return Color.HSVToRGB(hue, 0.5f, 0.55f);
                }

                if (_event is SpawnPatternEvent sp)
                {
                    int hash = sp.PatternId?.GetHashCode() ?? 0;
                    float hue = Mathf.Abs(hash % 360) / 360f;
                    return Color.HSVToRGB(hue, 0.5f, 0.6f);
                }

                return new Color(0.4f, 0.4f, 0.4f);
            }
        }

        public bool CanMove => true;

        public float DesignEstimate
        {
            get
            {
                if (_event is SpawnPatternEvent sp)
                    return sp.ComputedEffectiveDuration;
                return -1f;
            }
            set
            {
                // ComputedEffectiveDuration is engine-computed, not user-editable
            }
        }

        public object DataSource => _event;
    }
}
