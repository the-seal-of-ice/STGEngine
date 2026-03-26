using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a TimelineEvent (SpawnPatternEvent / SpawnWaveEvent).
    /// Used by MidStageLayer. Delegates StartTime/Duration directly to the underlying event.
    /// Pattern events have bullet trajectory thumbnails; wave events have spawn-delay bars.
    /// </summary>
    public class EventBlock : ITimelineBlock
    {
        private readonly TimelineEvent _event;

        // Cached trajectory data for pattern thumbnail
        private List<Vector2[]> _trajectoryLines;
        private Rect _trajectoryBounds;
        private bool _trajectoryComputed;

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
            set { }
        }

        public object DataSource => _event;

        // ── Thumbnail ──

        public bool HasThumbnail
        {
            get
            {
                if (_event is SpawnPatternEvent sp && sp.ResolvedPattern != null)
                {
                    EnsureTrajectoryComputed(sp.ResolvedPattern);
                    return _trajectoryLines != null && _trajectoryLines.Count > 0;
                }
                return false;
            }
        }

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_trajectoryLines == null || _trajectoryLines.Count == 0) return;

            float margin = 3f;
            float labelSpace = 12f; // Space for text label at top
            float drawW = blockWidth - margin * 2;
            float drawH = blockHeight - labelSpace - margin;
            if (drawW < 4f || drawH < 4f) return;

            // Map trajectory bounds to draw area
            float bw = _trajectoryBounds.width;
            float bh = _trajectoryBounds.height;
            if (bw < 0.01f) bw = 1f;
            if (bh < 0.01f) bh = 1f;

            float scale = Mathf.Min(drawW / bw, drawH / bh);
            float offsetX = margin + (drawW - bw * scale) * 0.5f;
            float offsetY = labelSpace + (drawH - bh * scale) * 0.5f;

            painter.strokeColor = new Color(0.9f, 0.9f, 0.9f, 0.5f);
            painter.lineWidth = 1f;

            foreach (var line in _trajectoryLines)
            {
                if (line.Length < 2) continue;
                painter.BeginPath();
                for (int i = 0; i < line.Length; i++)
                {
                    float x = offsetX + (line[i].x - _trajectoryBounds.xMin) * scale;
                    float y = offsetY + (line[i].y - _trajectoryBounds.yMin) * scale;
                    if (i == 0)
                        painter.MoveTo(new Vector2(x, y));
                    else
                        painter.LineTo(new Vector2(x, y));
                }
                painter.Stroke();
            }
        }

        private void EnsureTrajectoryComputed(BulletPattern pattern)
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;

            if (pattern?.Emitter == null) return;

            int bulletCount = pattern.Emitter.Count;
            int maxBullets = Mathf.Min(bulletCount, 12); // Sample up to 12 bullets
            int timeSteps = 8;
            float duration = _event.Duration > 0f ? _event.Duration : 5f;

            _trajectoryLines = new List<Vector2[]>(maxBullets);
            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;

            // Sample evenly spaced bullets
            int step = Mathf.Max(1, bulletCount / maxBullets);

            for (int bi = 0; bi < bulletCount && _trajectoryLines.Count < maxBullets; bi += step)
            {
                var points = new Vector2[timeSteps];
                for (int ti = 0; ti < timeSteps; ti++)
                {
                    float t = (ti / (float)(timeSteps - 1)) * duration;
                    var states = BulletEvaluator.EvaluateAll(pattern, t);
                    if (bi < states.Count)
                    {
                        // Project 3D → 2D: use X and Z (top-down view)
                        var pos = states[bi].Position;
                        float px = pos.x;
                        float py = -pos.z; // Flip Z for screen coords (Z+ = up in top-down)
                        points[ti] = new Vector2(px, py);

                        if (px < xMin) xMin = px;
                        if (px > xMax) xMax = px;
                        if (py < yMin) yMin = py;
                        if (py > yMax) yMax = py;
                    }
                    else
                    {
                        // Bullet index out of range at this time — use last known
                        points[ti] = ti > 0 ? points[ti - 1] : Vector2.zero;
                    }
                }
                _trajectoryLines.Add(points);
            }

            // Add small padding to bounds
            float pad = 0.5f;
            _trajectoryBounds = new Rect(xMin - pad, yMin - pad,
                (xMax - xMin) + pad * 2, (yMax - yMin) + pad * 2);
        }
    }
}
