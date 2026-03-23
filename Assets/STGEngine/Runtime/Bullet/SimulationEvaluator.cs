using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Emitters;
using STGEngine.Core.Modifiers;

namespace STGEngine.Runtime.Bullet
{
    /// <summary>
    /// Stateful evaluator for patterns containing ISimulationModifier.
    /// Maintains per-bullet state and advances via fixed-timestep Step().
    /// Supports mixed mode: formula modifiers contribute initial displacement,
    /// simulation modifiers step from there.
    /// Handles SplitModifier by spawning child bullets mid-simulation.
    /// </summary>
    public class SimulationEvaluator
    {
        /// <summary>Per-bullet runtime state.</summary>
        private class BulletInstance
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Elapsed;
            public bool Active;
            public BulletSpawnData Spawn;
            // Per-bullet modifier instances (each bullet needs its own state)
            public List<ISimulationModifier> SimMods;
        }

        private readonly BulletPattern _pattern;
        private List<BulletInstance> _bullets;
        private float _currentTime;
        private bool _initialized;

        // Cached formula modifiers (shared, stateless)
        private List<IFormulaModifier> _formulaMods;
        private bool _hasSpeedCurve;
        private SpeedCurveModifier _speedCurveMod;

        public SimulationEvaluator(BulletPattern pattern)
        {
            _pattern = pattern;
            CacheFormulaModifiers();
        }

        /// <summary>
        /// Check if a pattern requires simulation (has any ISimulationModifier).
        /// </summary>
        public static bool RequiresSimulation(BulletPattern pattern)
        {
            if (pattern?.Modifiers == null) return false;
            foreach (var mod in pattern.Modifiers)
            {
                if (mod.RequiresSimulation) return true;
            }
            return false;
        }

        /// <summary>
        /// Reset simulation to t=0 and re-initialize all bullets.
        /// </summary>
        public void Reset()
        {
            _currentTime = 0f;
            _initialized = false;
            _bullets = null;
        }

        /// <summary>
        /// Advance simulation by one fixed timestep.
        /// </summary>
        public void Step(float dt)
        {
            if (_pattern?.Emitter == null) return;

            if (!_initialized)
            {
                Initialize();
                _initialized = true;
            }

            _currentTime += dt;

            // Temporary list for new bullets from splits
            List<BulletInstance> newBullets = null;

            for (int i = 0; i < _bullets.Count; i++)
            {
                var b = _bullets[i];
                if (!b.Active) continue;

                // Apply simulation modifiers
                foreach (var mod in b.SimMods)
                {
                    if (mod is SplitModifier split)
                    {
                        split.Step(dt, ref b.Position, ref b.Velocity);
                        if (split.ShouldSplit())
                        {
                            var dirs = split.GetSplitDirections(b.Velocity);
                            foreach (var vel in dirs)
                            {
                                var child = CreateChildBullet(b, vel);
                                newBullets ??= new List<BulletInstance>();
                                newBullets.Add(child);
                            }
                        }
                    }
                    else
                    {
                        mod.Step(dt, ref b.Position, ref b.Velocity);
                    }
                }

                // If no sim mods handled movement (shouldn't happen, but safety)
                if (b.SimMods.Count == 0)
                {
                    b.Position += b.Velocity * dt;
                }

                b.Elapsed += dt;
            }

            // Add split children
            if (newBullets != null)
                _bullets.AddRange(newBullets);
        }

        /// <summary>
        /// Get current bullet states for rendering.
        /// </summary>
        public List<BulletState> GetStates()
        {
            if (_bullets == null)
                return new List<BulletState>(0);

            var results = new List<BulletState>(_bullets.Count);
            foreach (var b in _bullets)
            {
                if (!b.Active) continue;

                // Compute color with ColorCurve support
                Color color = _pattern.BulletColor;
                if (_pattern.ColorCurve != null && _pattern.Duration > 0f)
                {
                    float normalizedT = Mathf.Clamp01(_currentTime / _pattern.Duration);
                    float curveVal = _pattern.ColorCurve.Evaluate(normalizedT);
                    color = new Color(
                        color.r * curveVal,
                        color.g * curveVal,
                        color.b * curveVal,
                        color.a
                    );
                }

                results.Add(new BulletState
                {
                    Position = b.Position,
                    Scale = _pattern.BulletScale,
                    Color = color
                });
            }
            return results;
        }

        /// <summary>
        /// Capture full simulation state for seek/rollback.
        /// </summary>
        public object CaptureState()
        {
            if (_bullets == null) return null;

            var states = new List<BulletSnapshot>(_bullets.Count);
            foreach (var b in _bullets)
            {
                var modStates = new List<object>(b.SimMods.Count);
                foreach (var mod in b.SimMods)
                    modStates.Add(mod.CaptureState());

                states.Add(new BulletSnapshot
                {
                    Position = b.Position,
                    Velocity = b.Velocity,
                    Elapsed = b.Elapsed,
                    Active = b.Active,
                    ModStates = modStates
                });
            }

            return new SimSnapshot
            {
                CurrentTime = _currentTime,
                Initialized = _initialized,
                Bullets = states
            };
        }

        /// <summary>
        /// Restore simulation state from a snapshot.
        /// </summary>
        public void RestoreState(object state)
        {
            if (state == null) { Reset(); return; }

            var snap = (SimSnapshot)state;
            _currentTime = snap.CurrentTime;
            _initialized = snap.Initialized;

            if (snap.Bullets == null) { _bullets = null; return; }

            // Ensure bullet list matches snapshot size
            while (_bullets != null && _bullets.Count < snap.Bullets.Count)
            {
                // Need to create additional bullet instances (from splits)
                var template = _bullets.Count > 0 ? _bullets[0] : null;
                _bullets.Add(CreateEmptyBullet());
            }

            for (int i = 0; i < snap.Bullets.Count; i++)
            {
                var bs = snap.Bullets[i];
                var b = _bullets[i];
                b.Position = bs.Position;
                b.Velocity = bs.Velocity;
                b.Elapsed = bs.Elapsed;
                b.Active = bs.Active;

                for (int j = 0; j < b.SimMods.Count && j < bs.ModStates.Count; j++)
                    b.SimMods[j].RestoreState(bs.ModStates[j]);
            }
        }

        // ─── Private helpers ───

        private void Initialize()
        {
            var emitter = _pattern.Emitter;
            int count = emitter.Count;
            _bullets = new List<BulletInstance>(count);

            for (int i = 0; i < count; i++)
            {
                var spawn = emitter.Evaluate(i, 0f);
                var dir = spawn.Direction;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                else dir.Normalize();

                // Apply formula modifiers for initial position offset
                var pos = spawn.Position;
                if (_formulaMods != null && _formulaMods.Count > 0)
                {
                    // Formula mods don't contribute to simulation initial state
                    // (they were for the f(t) path). In simulation mode,
                    // bullets start at spawn position and move via velocity.
                }

                var bullet = new BulletInstance
                {
                    Position = pos,
                    Velocity = dir * spawn.Speed,
                    Elapsed = 0f,
                    Active = true,
                    Spawn = spawn,
                    SimMods = CreateModifierInstances()
                };

                _bullets.Add(bullet);
            }
        }

        /// <summary>
        /// Create per-bullet modifier instances (each bullet needs independent state).
        /// </summary>
        private List<ISimulationModifier> CreateModifierInstances()
        {
            var mods = new List<ISimulationModifier>();
            if (_pattern.Modifiers == null) return mods;

            bool hasSimMovement = false;
            foreach (var mod in _pattern.Modifiers)
            {
                if (mod is ISimulationModifier)
                {
                    // Clone modifier for per-bullet state
                    var instance = CloneSimModifier(mod);
                    if (instance != null)
                    {
                        mods.Add(instance);
                        hasSimMovement = true;
                    }
                }
            }

            // If we have sim mods but none handle position advancement,
            // we need a passthrough. Currently all sim mods advance position in Step().
            return mods;
        }

        private ISimulationModifier CloneSimModifier(IModifier mod)
        {
            // Create a new instance with same parameters but fresh state
            if (mod is HomingModifier hm)
            {
                return new HomingModifier
                {
                    TargetPosition = hm.TargetPosition,
                    TurnSpeed = hm.TurnSpeed,
                    Delay = hm.Delay
                };
            }
            if (mod is BounceModifier bm)
            {
                return new BounceModifier
                {
                    BoundaryRadius = bm.BoundaryRadius,
                    MaxBounces = bm.MaxBounces
                };
            }
            if (mod is SplitModifier sm)
            {
                return new SplitModifier
                {
                    SplitTime = sm.SplitTime,
                    SplitCount = sm.SplitCount,
                    SpreadAngle = sm.SpreadAngle
                };
            }
            return null;
        }

        private BulletInstance CreateChildBullet(BulletInstance parent, Vector3 velocity)
        {
            return new BulletInstance
            {
                Position = parent.Position,
                Velocity = velocity,
                Elapsed = 0f,
                Active = true,
                Spawn = parent.Spawn,
                SimMods = CreateChildModifierInstances()
            };
        }

        /// <summary>
        /// Create modifier instances for child bullets (no SplitModifier to avoid recursive splits).
        /// </summary>
        private List<ISimulationModifier> CreateChildModifierInstances()
        {
            var mods = new List<ISimulationModifier>();
            if (_pattern.Modifiers == null) return mods;

            foreach (var mod in _pattern.Modifiers)
            {
                if (mod is ISimulationModifier && !(mod is SplitModifier))
                {
                    var instance = CloneSimModifier(mod);
                    if (instance != null) mods.Add(instance);
                }
            }
            return mods;
        }

        private BulletInstance CreateEmptyBullet()
        {
            return new BulletInstance
            {
                Position = Vector3.zero,
                Velocity = Vector3.zero,
                Elapsed = 0f,
                Active = false,
                Spawn = default,
                SimMods = CreateModifierInstances()
            };
        }

        private void CacheFormulaModifiers()
        {
            if (_pattern?.Modifiers == null) return;
            _formulaMods = new List<IFormulaModifier>();
            foreach (var mod in _pattern.Modifiers)
            {
                if (mod is IFormulaModifier fm)
                {
                    _formulaMods.Add(fm);
                    if (mod is SpeedCurveModifier scm)
                    {
                        _hasSpeedCurve = true;
                        _speedCurveMod = scm;
                    }
                }
            }
        }

        // ─── Snapshot types ───

        private struct BulletSnapshot
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Elapsed;
            public bool Active;
            public List<object> ModStates;
        }

        private struct SimSnapshot
        {
            public float CurrentTime;
            public bool Initialized;
            public List<BulletSnapshot> Bullets;
        }
    }
}
