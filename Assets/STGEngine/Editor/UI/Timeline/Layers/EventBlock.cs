using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Timeline;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Optional interface for blocks that provide modifier-aware trajectory thumbnails.
    /// TrackAreaView renders these as separate inline icons after the emitter thumbnail.
    /// </summary>
    public interface IModifierThumbnailProvider
    {
        /// <summary>Whether modifier thumbnails are available.</summary>
        bool HasModifierThumbnails { get; }

        /// <summary>
        /// Number of per-modifier thumbnails (one per modifier).
        /// The first is shown inline; the rest appear on hover.
        /// </summary>
        int ModifierThumbnailCount { get; }

        /// <summary>Draw a single-bullet trajectory with only the i-th modifier applied.</summary>
        void DrawModifierThumbnail(Painter2D painter, float w, float h, int modifierIndex);

        /// <summary>Short label for the i-th modifier (e.g. "wave", "homing").</summary>
        string GetModifierLabel(int modifierIndex);

        /// <summary>Whether the all-bullets-all-modifiers thumbnail is available.</summary>
        bool HasAllBulletsThumbnail { get; }

        /// <summary>Draw all bullets with all modifiers applied.</summary>
        void DrawAllBulletsThumbnail(Painter2D painter, float w, float h);

        /// <summary>Reset cached trajectory data so it will be recomputed on next access.</summary>
        void InvalidateThumbnailCache();
    }

    /// <summary>
    /// ITimelineBlock wrapper for a TimelineEvent (SpawnPatternEvent / SpawnWaveEvent).
    /// Pattern events have oblique orthographic bullet trajectory thumbnails.
    /// Thumbnail 1: emitter only (no modifiers).
    /// Thumbnail 2: single bullet + first modifier (hover to see each modifier individually).
    /// Thumbnail 3: all bullets + all modifiers.
    /// </summary>
    public class EventBlock : ITimelineBlock, IModifierThumbnailProvider
    {
        private readonly TimelineEvent _event;
        private bool _trajectoryComputed;

        // Emitter-only trajectories (no modifiers)
        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _emitterTrajectories;
        // Per-modifier: single bullet with only that modifier
        private List<List<TrajectoryThumbnailRenderer.TrajPoint[]>> _perModifierTrajectories;
        // All bullets with all modifiers
        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _allModsTrajectories;
        // Modifier labels
        private List<string> _modifierLabels;

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

        public bool IsModified => false;

        // ── ITimelineBlock Thumbnail (emitter only) ──

        public bool HasThumbnail
        {
            get
            {
                if (_event is SpawnPatternEvent sp && sp.ResolvedPattern != null)
                {
                    EnsureTrajectoryComputed(sp.ResolvedPattern);
                    return _emitterTrajectories != null && _emitterTrajectories.Count > 0;
                }
                return false;
            }
        }

        public bool ThumbnailInline => true;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_emitterTrajectories == null || _emitterTrajectories.Count == 0) return;
            TrajectoryThumbnailRenderer.Draw(painter, blockWidth, blockHeight, _emitterTrajectories);
        }

        // ── IModifierThumbnailProvider ──

        public bool HasModifierThumbnails =>
            _perModifierTrajectories != null && _perModifierTrajectories.Count > 0;

        public int ModifierThumbnailCount =>
            _perModifierTrajectories?.Count ?? 0;

        public void DrawModifierThumbnail(Painter2D painter, float w, float h, int modifierIndex)
        {
            if (_perModifierTrajectories == null || modifierIndex < 0 ||
                modifierIndex >= _perModifierTrajectories.Count) return;
            var trajs = _perModifierTrajectories[modifierIndex];
            if (trajs != null && trajs.Count > 0)
                TrajectoryThumbnailRenderer.Draw(painter, w, h, trajs);
        }

        public string GetModifierLabel(int modifierIndex)
        {
            if (_modifierLabels == null || modifierIndex < 0 ||
                modifierIndex >= _modifierLabels.Count) return "?";
            return _modifierLabels[modifierIndex];
        }

        public bool HasAllBulletsThumbnail =>
            _allModsTrajectories != null && _allModsTrajectories.Count > 0;

        public void DrawAllBulletsThumbnail(Painter2D painter, float w, float h)
        {
            if (_allModsTrajectories == null || _allModsTrajectories.Count == 0) return;
            TrajectoryThumbnailRenderer.Draw(painter, w, h, _allModsTrajectories);
        }

        public void InvalidateThumbnailCache()
        {
            _trajectoryComputed = false;
            _emitterTrajectories = null;
            _perModifierTrajectories = null;
            _allModsTrajectories = null;
            _modifierLabels = null;
        }

        // ── Compute ──

        private void EnsureTrajectoryComputed(BulletPattern pattern)
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;

            float sampleDuration = Mathf.Max(10f, (_event.Duration > 0f ? _event.Duration : 5f) * 3f);
            float modSampleDuration = Mathf.Max(2f, _event.Duration > 0f ? _event.Duration : 3f);

            // 1) Emitter only (no modifiers)
            _emitterTrajectories = TrajectoryThumbnailRenderer.ComputeEmitterOnly(pattern, sampleDuration);

            // 2) Per-modifier single-bullet trajectories
            if (pattern.Modifiers != null && pattern.Modifiers.Count > 0)
            {
                _perModifierTrajectories = new List<List<TrajectoryThumbnailRenderer.TrajPoint[]>>();
                _modifierLabels = new List<string>();

                foreach (var mod in pattern.Modifiers)
                {
                    var trajs = TrajectoryThumbnailRenderer.ComputeSingleBulletWithModifier(pattern, mod, modSampleDuration);
                    _perModifierTrajectories.Add(trajs);
                    _modifierLabels.Add(mod.TypeName);
                }

                // 3) All bullets + all modifiers
                _allModsTrajectories = TrajectoryThumbnailRenderer.ComputeAllBulletsAllModifiers(pattern, modSampleDuration);
            }
        }
    }

    /// <summary>
    /// Shared trajectory thumbnail renderer with oblique orthographic projection.
    /// Virtual camera: 30° elevation, 45° azimuth, orthographic projection centered on emitter.
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
        private static readonly Vector3 CamDir;

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
        private const int MaxBulletsDetailed = 128;
        private const int TimeSteps = 16;
        private const float SimDt = 0.05f;

        // ─── Compute: emitter only (no modifiers at all) ───

        /// <summary>
        /// Compute trajectories using only the emitter (no modifiers).
        /// Creates a temporary pattern clone with empty modifier list.
        /// Uses higher sampling cap to reflect Count changes visually.
        /// </summary>
        public static List<TrajPoint[]> ComputeEmitterOnly(BulletPattern pattern, float duration)
        {
            if (pattern?.Emitter == null) return null;

            var stripped = new BulletPattern
            {
                Id = pattern.Id,
                Name = pattern.Name,
                Emitter = pattern.Emitter,
                Modifiers = new List<IModifier>(), // empty — no modifiers
                BulletScale = pattern.BulletScale,
                BulletColor = pattern.BulletColor,
                Duration = pattern.Duration,
                Seed = pattern.Seed,
                MeshType = pattern.MeshType,
                ColorCurve = pattern.ColorCurve,
                Collision = pattern.Collision
            };

            return ComputeViaEvaluator(stripped, duration, MaxBulletsDetailed);
        }

        // ─── Compute: single bullet with one specific modifier ───

        /// <summary>
        /// Compute single-bullet trajectory with only the specified modifier applied.
        /// For formula modifiers: uses BulletEvaluator with a 1-bullet, 1-modifier pattern.
        /// For simulation modifiers: uses SimulationEvaluator with a 1-bullet, 1-modifier pattern.
        /// </summary>
        public static List<TrajPoint[]> ComputeSingleBulletWithModifier(
            BulletPattern pattern, IModifier modifier, float duration)
        {
            if (pattern?.Emitter == null) return null;

            var singleModPattern = new BulletPattern
            {
                Id = pattern.Id,
                Name = pattern.Name,
                Emitter = new CountOverrideEmitter(pattern.Emitter, 1),
                Modifiers = new List<IModifier> { modifier },
                BulletScale = pattern.BulletScale,
                BulletColor = pattern.BulletColor,
                Duration = pattern.Duration,
                Seed = pattern.Seed,
                MeshType = pattern.MeshType,
                ColorCurve = pattern.ColorCurve,
                Collision = pattern.Collision
            };

            if (modifier.RequiresSimulation)
                return RunSimulation(singleModPattern, duration);
            else
                return ComputeViaEvaluator(singleModPattern, duration);
        }

        // ─── Compute: all bullets with all modifiers ───

        /// <summary>
        /// Compute all-bullet trajectories with all modifiers applied.
        /// Automatically chooses BulletEvaluator or SimulationEvaluator based on modifier types.
        /// </summary>
        public static List<TrajPoint[]> ComputeAllBulletsAllModifiers(BulletPattern pattern, float duration)
        {
            if (pattern?.Emitter == null) return null;

            if (SimulationEvaluator.RequiresSimulation(pattern))
                return RunSimulation(pattern, duration);
            else
                return ComputeViaEvaluator(pattern, duration);
        }

        // ─── Legacy: compute via BulletEvaluator (formula path) ───

        /// <summary>
        /// Sample bullet trajectories using BulletEvaluator (handles formula modifiers).
        /// </summary>
        public static List<TrajPoint[]> Compute(BulletPattern pattern, float duration)
        {
            return ComputeViaEvaluator(pattern, duration);
        }

        private static List<TrajPoint[]> ComputeViaEvaluator(BulletPattern pattern, float duration,
            int maxBulletsCap = -1)
        {
            if (pattern?.Emitter == null) return null;
            if (maxBulletsCap < 0) maxBulletsCap = MaxBullets;

            int bulletCount = pattern.Emitter.Count;
            int maxBullets = Mathf.Min(bulletCount, maxBulletsCap);
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

        // ─── Simulation path ───

        private static List<TrajPoint[]> RunSimulation(BulletPattern pattern, float duration)
        {
            var sim = new SimulationEvaluator(pattern);
            int totalSteps = Mathf.Max(1, Mathf.CeilToInt(duration / SimDt));
            float sampleInterval = duration / (TimeSteps - 1);

            var snapshots = new List<List<BulletState>>(TimeSteps);
            float elapsed = 0f;
            int nextSample = 0;

            sim.Step(0f);
            snapshots.Add(sim.GetStates());
            nextSample = 1;

            for (int s = 0; s < totalSteps && nextSample < TimeSteps; s++)
            {
                float stepDt = Mathf.Min(SimDt, duration - elapsed);
                if (stepDt <= 0f) break;
                sim.Step(stepDt);
                elapsed += stepDt;

                float nextSampleTime = nextSample * sampleInterval;
                if (elapsed >= nextSampleTime - SimDt * 0.5f)
                {
                    snapshots.Add(sim.GetStates());
                    nextSample++;
                }
            }

            while (snapshots.Count < TimeSteps)
                snapshots.Add(sim.GetStates());

            int maxBulletCount = 0;
            foreach (var snap in snapshots)
                if (snap.Count > maxBulletCount) maxBulletCount = snap.Count;

            if (maxBulletCount == 0) return null;

            int trajCount = Mathf.Min(maxBulletCount, MaxBullets);
            int trajStep = Mathf.Max(1, maxBulletCount / trajCount);

            var result = new List<TrajPoint[]>(trajCount);
            for (int bi = 0; bi < maxBulletCount && result.Count < trajCount; bi += trajStep)
            {
                var points = new TrajPoint[TimeSteps];
                for (int ti = 0; ti < TimeSteps; ti++)
                {
                    float tNorm = ti / (float)(TimeSteps - 1);
                    var snap = snapshots[ti];
                    if (bi < snap.Count)
                    {
                        points[ti] = new TrajPoint
                        {
                            Position3D = snap[bi].Position,
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

        // ─── Projection ───

        private static Vector3 Project(Vector3 worldPos, Vector3 camPos)
        {
            Vector3 rel = worldPos - camPos;
            float depth = Vector3.Dot(rel, CamForward);
            float screenX = Vector3.Dot(rel, CamRight);
            float screenY = Vector3.Dot(rel, CamUp);
            return new Vector3(screenX, -screenY, depth);
        }

        // ─── Color ───

        private static Color GetSegmentColor(float timeNormalized, float depthNormalized)
        {
            Color c = Color.Lerp(
                new Color(0.3f, 0.5f, 1f),
                new Color(1f, 0.3f, 0.2f),
                timeNormalized);

            float brightness = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(depthNormalized));
            c.r *= brightness;
            c.g *= brightness;
            c.b *= brightness;
            c.a = Mathf.Lerp(0.9f, 0.4f, Mathf.Clamp01(depthNormalized));
            return c;
        }

        // ─── Drawing ───

        /// <summary>
        /// Draw trajectories into a Painter2D area.
        /// </summary>
        public static void Draw(Painter2D painter, float blockWidth, float blockHeight,
            List<TrajPoint[]> trajectories)
        {
            if (trajectories == null || trajectories.Count == 0) return;

            float margin = 2f;
            float drawW = blockWidth - margin * 2;
            float drawH = blockHeight - margin * 2;
            if (drawW < 4f || drawH < 4f) return;

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

    /// <summary>
    /// Wrapper emitter that overrides Count while delegating Evaluate to the inner emitter.
    /// </summary>
    internal class CountOverrideEmitter : STGEngine.Core.Emitters.IEmitter
    {
        private readonly STGEngine.Core.Emitters.IEmitter _inner;

        public string TypeName => _inner.TypeName;
        public int Count { get; set; }

        public CountOverrideEmitter(STGEngine.Core.Emitters.IEmitter inner, int count)
        {
            _inner = inner;
            Count = count;
        }

        public STGEngine.Core.Emitters.BulletSpawnData Evaluate(int index, float time)
        {
            return _inner.Evaluate(index, time);
        }
    }
}
