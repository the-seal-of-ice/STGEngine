using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Rendering;

namespace STGEngine.Runtime.Preview
{
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

        /// <summary>
        /// Dynamic target provider for HomingModifier. When set, homing bullets
        /// track this position instead of their static TargetPosition.
        /// Typically set to IPlayerProvider.Position.
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
        }

        /// <summary>Render with alpha interpolation between prev and curr states.</summary>
        private void Render()
        {
            if (_currStates == null || _bulletMesh == null || _bulletMaterial == null)
                return;

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
