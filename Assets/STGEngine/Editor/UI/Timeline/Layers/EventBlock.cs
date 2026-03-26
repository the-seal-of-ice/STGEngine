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
    /// Pattern events have pseudo-3D bullet trajectory thumbnails with depth + time coloring.
    /// </summary>
    public class EventBlock : ITimelineBlock
    {
        private readonly TimelineEvent _event;
        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _trajectories;
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
            get => (_event is SpawnPatternEvent sp) ? sp.ComputedEffectiveDuration : -1f;
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
                    return _trajectories != null && _trajectories.Count > 0;
                }
                return false;
            }
        }

        public bool ThumbnailInline => true;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_trajectories == null || _trajectories.Count == 0) return;
            TrajectoryThumbnailRenderer.Draw(painter, blockWidth, blockHeight, _trajectories);
        }

        private void EnsureTrajectoryComputed(BulletPattern pattern)
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;
            float sampleDuration = Mathf.Max(10f, (_event.Duration > 0f ? _event.Duration : 5f) * 3f);
            _trajectories = TrajectoryThumbnailRenderer.Compute(pattern, sampleDuration);
        }
    }

    /// <summary>
    /// Shared pseudo-3D trajectory thumbnail renderer.
    /// Virtual camera: 30° elevation, 45° azimuth, perspective projection centered on emitter.
    /// Color: time → blue(early) to red(late), depth → bright(near) to dim(far).
    /// </summary>
    public static class TrajectoryThumbnailRenderer
    {
        public struct TrajPoint
        {
            public Vector3 Position3D;
            public float TimeNormalized; // 0..1
        }

        // Virtual camera parameters
        private static readonly float CamElevation = 30f * Mathf.Deg2Rad;
        private static readonly float CamAzimuth = 45f * Mathf.Deg2Rad;
        private static readonly float CamDistance = 15f;

        private static readonly Vector3 CamForward;
        private static readonly Vector3 CamRight;
        private static readonly Vector3 CamUp;
        private static readonly Vector3 CamDir; // Unit vector from origin toward camera

        static TrajectoryThumbnailRenderer()
        {
            float cosE = Mathf.Cos(CamElevation);
            float sinE = Mathf.Sin(CamElevation);
            float cosA = Mathf.Cos(CamAzimuth);
            float sinA = Mathf.Sin(CamAzimuth);

            CamDir = new Vector3(cosE * sinA, sinE, cosE * cosA);
            CamForward = -CamDir;
            CamRight = Vector3.Cross(Vector3.up, CamForward).normalized;
            if (CamRight.sqrMagnitude < 0.001f)
                CamRight = Vector3.right;
            CamUp = Vector3.Cross(CamForward, CamRight).normalized;
        }

        private const int MaxBullets = 12;
        private const int TimeSteps = 16;

        /// <summary>
        /// Sample bullet trajectories from a pattern.
        /// Camera is centered on the emitter's average spawn position.
        /// </summary>
        public static List<TrajPoint[]> Compute(BulletPattern pattern, float duration)
        {
            if (pattern?.Emitter == null) return null;

            int bulletCount = pattern.Emitter.Count;
            int maxBullets = Mathf.Min(bulletCount, MaxBullets);
            int step = Mathf.Max(1, bulletCount / maxBullets);

            var result = new List<TrajPoint[]>(maxBullets);

            for (int bi = 0; bi < bulletCount && result.Count < maxBullets; bi += step)
            {
                var points = new TrajPoint[TimeSteps];
                for (int ti = 0; ti < TimeSteps; ti++)
                {
                    float tNorm = ti / (float)(TimeSteps - 1);
                    float t = tNorm * duration;
                    var states = BulletEvaluator.EvaluateAll(pattern, t);
                    if (bi < states.Count)
                    {
                        points[ti] = new TrajPoint
                        {
                            Position3D = states[bi].Position,
                            TimeNormalized = tNorm
                        };
                    }
                    else
                    {
                        points[ti] = ti > 0 ? points[ti - 1] : new TrajPoint();
                    }
                }
                result.Add(points);
            }

            return result;
        }

        /// <summary>
        /// Project a 3D point to 2D using the virtual camera.
        /// Returns (screenX, screenY, depth).
        /// Camera position is offset from focusPoint along CamDir.
        /// </summary>
        private static Vector3 Project(Vector3 worldPos, Vector3 camPos)
        {
            Vector3 rel = worldPos - camPos;
            float depth = Vector3.Dot(rel, CamForward);
            float screenX = Vector3.Dot(rel, CamRight);
            float screenY = Vector3.Dot(rel, CamUp);

            // Perspective division
            if (depth > 0.1f)
            {
                float perspScale = CamDistance / depth;
                screenX *= perspScale;
                screenY *= perspScale;
            }

            return new Vector3(screenX, -screenY, depth);
        }

        /// <summary>
        /// Color for a trajectory segment.
        /// Time: blue(0) → red(1). Depth: bright(near) → dim(far).
        /// </summary>
        private static Color GetSegmentColor(float timeNormalized, float depthNormalized)
        {
            Color c = Color.Lerp(
                new Color(0.3f, 0.5f, 1f),   // Early: blue
                new Color(1f, 0.3f, 0.2f),   // Late: red
                timeNormalized);

            float brightness = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(depthNormalized));
            c.r *= brightness;
            c.g *= brightness;
            c.b *= brightness;
            c.a = Mathf.Lerp(0.9f, 0.4f, Mathf.Clamp01(depthNormalized));

            return c;
        }

        /// <summary>
        /// Draw pseudo-3D trajectories into a Painter2D area.
        /// </summary>
        public static void Draw(Painter2D painter, float blockWidth, float blockHeight,
            List<TrajPoint[]> trajectories)
        {
            if (trajectories == null || trajectories.Count == 0) return;

            float margin = 2f;
            float drawW = blockWidth - margin * 2;
            float drawH = blockHeight - margin * 2;
            if (drawW < 4f || drawH < 4f) return;

            // Find focus point (average of all t=0 positions = emitter center)
            Vector3 focus = Vector3.zero;
            int count = 0;
            foreach (var traj in trajectories)
            {
                if (traj.Length > 0)
                {
                    focus += traj[0].Position3D;
                    count++;
                }
            }
            if (count > 0) focus /= count;

            Vector3 camPos = focus + CamDir * CamDistance;

            // Project all points, find 2D bounds + depth range
            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;
            float dMin = float.MaxValue, dMax = float.MinValue;

            var projected = new List<Vector3[]>(trajectories.Count);
            foreach (var traj in trajectories)
            {
                var pts = new Vector3[traj.Length];
                for (int i = 0; i < traj.Length; i++)
                {
                    pts[i] = Project(traj[i].Position3D, camPos);
                    if (pts[i].x < xMin) xMin = pts[i].x;
                    if (pts[i].x > xMax) xMax = pts[i].x;
                    if (pts[i].y < yMin) yMin = pts[i].y;
                    if (pts[i].y > yMax) yMax = pts[i].y;
                    if (pts[i].z < dMin) dMin = pts[i].z;
                    if (pts[i].z > dMax) dMax = pts[i].z;
                }
                projected.Add(pts);
            }

            float bw = xMax - xMin;
            float bh = yMax - yMin;
            if (bw < 0.01f) bw = 1f;
            if (bh < 0.01f) bh = 1f;
            float dRange = dMax - dMin;
            if (dRange < 0.01f) dRange = 1f;

            float scale = Mathf.Min(drawW / bw, drawH / bh);
            float offsetX = margin + (drawW - bw * scale) * 0.5f;
            float offsetY = margin + (drawH - bh * scale) * 0.5f;

            painter.lineWidth = 1.2f;

            // Draw segment-by-segment with per-segment color
            for (int ti = 0; ti < trajectories.Count; ti++)
            {
                var traj = trajectories[ti];
                var pts = projected[ti];
                if (pts.Length < 2) continue;

                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float x0 = offsetX + (pts[i].x - xMin) * scale;
                    float y0 = offsetY + (pts[i].y - yMin) * scale;
                    float x1 = offsetX + (pts[i + 1].x - xMin) * scale;
                    float y1 = offsetY + (pts[i + 1].y - yMin) * scale;

                    float timeMid = (traj[i].TimeNormalized + traj[i + 1].TimeNormalized) * 0.5f;
                    float depthMid = ((pts[i].z - dMin) + (pts[i + 1].z - dMin)) * 0.5f / dRange;

                    painter.strokeColor = GetSegmentColor(timeMid, depthMid);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x0, y0));
                    painter.LineTo(new Vector2(x1, y1));
                    painter.Stroke();
                }
            }
        }
    }
}
