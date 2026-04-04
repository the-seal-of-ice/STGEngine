using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Rendering;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Describes a region where bullets have been cleared.
    /// Updated each frame by ActionEventPreviewController as the wave expands.
    /// </summary>
    public struct ClearZone
    {
        /// <summary>0=FullScreen, 1=Circle, 2=Rectangle</summary>
        public int ShapeType;
        /// <summary>Center in previewer-local space.</summary>
        public Vector3 Origin;
        /// <summary>Current radius (grows over time for expanding wave).</summary>
        public float Radius;
        /// <summary>Current half-extents (grows over time for expanding wave).</summary>
        public Vector3 Extents;

        public bool Contains(Vector3 localPos)
        {
            return ShapeType switch
            {
                0 => true,
                1 => Vector3.Distance(localPos, Origin) <= Radius,
                2 => Mathf.Abs(localPos.x - Origin.x) <= Extents.x
                  && Mathf.Abs(localPos.y - Origin.y) <= Extents.y
                  && Mathf.Abs(localPos.z - Origin.z) <= Extents.z,
                _ => false
            };
        }
    }

    /// <summary>
    /// Sandbox previewer MonoBehaviour. Owns no time logic — delegates entirely
    /// to PlaybackController (validation #10). Evaluates bullet states via
    /// BulletEvaluator (formula path) or SimulationEvaluator (simulation path)
    /// and renders via BulletRenderer with alpha interpolation.
    /// </summary>
    [AddComponentMenu("STGEngine/Pattern Previewer")]
    public class PatternPreviewer : MonoBehaviour
    {
        [Header("Bullet Visuals")]
        [SerializeField] private Mesh _bulletMesh;
        [SerializeField] private Material _bulletMaterial;

        /// <summary>Playback controller — exposed for Editor UI binding.</summary>
        public PlaybackController Playback { get; private set; } = new();

        /// <summary>Current pattern being previewed.</summary>
        public BulletPattern Pattern
        {
            get => _pattern;
            set
            {
                _pattern = value;
                if (_pattern != null)
                {
                    Playback.Duration = _pattern.Duration;
                    // Determine evaluation path
                    _useSimulation = SimulationEvaluator.RequiresSimulation(_pattern);
                    if (_useSimulation)
                    {
                        _simEvaluator = new SimulationEvaluator(_pattern);
                        _simEvaluator.HomingTargetProvider = HomingTargetProvider;
                    }
                    else
                        _simEvaluator = null;
                }
                else
                {
                    _useSimulation = false;
                    _simEvaluator = null;
                }
            }
        }

        private BulletPattern _pattern;
        private BulletRenderer _renderer;
        private bool _useSimulation;
        private SimulationEvaluator _simEvaluator;

        // Two-frame state for render interpolation
        private List<BulletState> _prevStates;
        private List<BulletState> _currStates;

        /// <summary>Current bullet states for external consumers (e.g. CollisionVisualizer).</summary>
        public List<BulletState> CurrentStates => _currStates;

        /// <summary>Expose the simulation evaluator for BulletClear operations.</summary>
        public SimulationEvaluator SimEvaluator => _simEvaluator;

        /// <summary>
        /// When true, all bullets are suppressed (cleared). OnTimeChanged produces
        /// empty states until the next ForceRefresh/Seek resets this flag.
        /// Used by FullScreen BulletClear to also clear formula-path bullets.
        /// </summary>
        public bool Cleared { get; set; }

        /// <summary>
        /// Immediately clear all visible bullets this frame.
        /// Sets Cleared flag and wipes current render states.
        /// </summary>
        public void ClearAllBullets()
        {
            Cleared = true;
            _clearZones.Clear();
            _currStates = new List<BulletState>(0);
            _prevStates = _currStates;
        }

        /// <summary>
        /// Replace the active clear zones. Called each frame by the controller
        /// as expanding waves grow. Also immediately filters current states
        /// and marks simulation bullets inactive.
        /// </summary>
        public void SetClearZones(List<ClearZone> zones)
        {
            _clearZones.Clear();
            if (zones != null)
                _clearZones.AddRange(zones);

            // Immediately mark simulation bullets inactive inside new zones
            if (_simEvaluator != null)
            {
                foreach (var z in _clearZones)
                    _simEvaluator.ClearBullets(z.ShapeType, z.Origin, z.Radius, z.Extents);
            }

            // Immediately filter current render states
            ApplyClearZones();
        }

        private readonly List<ClearZone> _clearZones = new();

        /// <summary>
        /// Dynamic target provider for PlayerHomingModifier. When set, player-homing
        /// bullets track this position each tick. Typically set to IPlayerProvider.Position.
        /// </summary>
        public System.Func<Vector3> HomingTargetProvider { get; set; }

        private void Awake()
        {
            _renderer = new BulletRenderer();
            Playback.OnTimeChanged += OnTimeChanged;
        }

        private void OnDestroy()
        {
            Playback.OnTimeChanged -= OnTimeChanged;
            _renderer?.Dispose();
        }

        private void Update()
        {
            Playback.Tick(Time.deltaTime);
            Render();
        }

        /// <summary>
        /// Configure bullet visuals at runtime (called by scene setup).
        /// </summary>
        public void SetBulletVisuals(Mesh mesh, Material material)
        {
            _bulletMesh = mesh;
            _bulletMaterial = material;
        }

        /// <summary>
        /// Set a default pattern for quick testing (called from scene setup).
        /// </summary>
        public void SetDefaultPattern(BulletPattern pattern)
        {
            Pattern = pattern;
            Playback.Seek(0f);
            ForceRefresh();
            Playback.Play();
        }

        /// <summary>
        /// Force-refresh states at current time. Call after Seek or pattern change
        /// to snap visuals without interpolation artifacts.
        /// </summary>
        public void ForceRefresh()
        {
            if (_pattern == null) return;

            // Reset cleared state on refresh (e.g. after Seek)
            Cleared = false;
            _clearZones.Clear();

            // Re-evaluate whether simulation is needed (modifier list may have changed)
            bool needsSim = SimulationEvaluator.RequiresSimulation(_pattern);
            if (needsSim != _useSimulation || (needsSim && _simEvaluator == null))
            {
                _useSimulation = needsSim;
                if (needsSim)
                {
                    _simEvaluator = new SimulationEvaluator(_pattern);
                    _simEvaluator.HomingTargetProvider = HomingTargetProvider;
                }
                else
                {
                    _simEvaluator = null;
                }
            }
            else if (_useSimulation)
            {
                // Rebuild evaluator to pick up newly added/removed modifiers
                _simEvaluator = new SimulationEvaluator(_pattern);
                _simEvaluator.HomingTargetProvider = HomingTargetProvider;
            }

            if (_useSimulation)
            {
                // For simulation path, reset and re-simulate to current time
                _simEvaluator.Reset();
                float dt = Playback.FixedDt;
                float target = Playback.CurrentTime;

                // Even at t=0, we need at least one Step to initialize bullet states
                if (target <= 0f)
                {
                    _simEvaluator.Step(0f); // Initialize without advancing time
                }
                else
                {
                    while (target > dt)
                    {
                        _simEvaluator.Step(dt);
                        target -= dt;
                    }
                    if (target > 0f)
                        _simEvaluator.Step(target);
                }

                _currStates = _simEvaluator.GetStates();
            }
            else
            {
                _currStates = BulletEvaluator.EvaluateAll(_pattern, Playback.CurrentTime);
            }
            _prevStates = _currStates; // No interpolation gap
        }

        /// <summary>Called by PlaybackController on every time change.</summary>
        private void OnTimeChanged(float t)
        {
            if (_pattern == null) return;

            _prevStates = _currStates;

            if (Cleared)
            {
                _currStates = new List<BulletState>(0);
                return;
            }

            if (_useSimulation)
            {
                // SimulationEvaluator is stepped by the fixed-timestep loop,
                // so each OnTimeChanged corresponds to one logic tick.
                float dt = Playback.FixedDt;
                _simEvaluator.Step(dt);
                _currStates = _simEvaluator.GetStates();
            }
            else
            {
                _currStates = BulletEvaluator.EvaluateAll(_pattern, t);
            }

            // Filter out bullets inside any active clear zones
            if (_clearZones.Count > 0)
                ApplyClearZones();
        }

        /// <summary>Remove bullets that fall inside any registered clear zone.</summary>
        private void ApplyClearZones()
        {
            if (_currStates == null || _clearZones.Count == 0) return;
            _currStates.RemoveAll(b =>
            {
                foreach (var zone in _clearZones)
                {
                    if (zone.Contains(b.Position)) return true;
                }
                return false;
            });
        }

        /// <summary>Render with alpha interpolation between prev and curr states.</summary>
        private void Render()
        {
            if (_currStates == null || _bulletMesh == null || _bulletMaterial == null)
            {
                if (_pattern != null && Time.frameCount % 60 == 0)
                    Debug.Log($"[Previewer:{name}] Render SKIP: states={_currStates?.Count} mesh={_bulletMesh != null} mat={_bulletMaterial != null} pattern={_pattern?.Name}");
                return;
            }

            float alpha = Playback.Alpha;
            bool canInterpolate = _prevStates != null
                && _prevStates.Count == _currStates.Count;

            for (int i = 0; i < _currStates.Count; i++)
            {
                var curr = _currStates[i];
                Vector3 pos;
                float scale;
                Color color;

                if (canInterpolate)
                {
                    var prev = _prevStates[i];
                    pos = Vector3.Lerp(prev.Position, curr.Position, alpha);
                    scale = Mathf.Lerp(prev.Scale, curr.Scale, alpha);
                    color = Color.Lerp(prev.Color, curr.Color, alpha);
                }
                else
                {
                    pos = curr.Position;
                    scale = curr.Scale;
                    color = curr.Color;
                }

                _renderer.Submit(_bulletMesh, _bulletMaterial, transform.position + pos, scale, color);
            }

            _renderer.Flush();
        }
    }
}
