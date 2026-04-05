using System;
using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Rendering;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Visual placeholder for the Boss during spell card editing.
    /// Shows a diamond mesh at the interpolated BossPath position,
    /// and draws the path as GL lines in the 3D viewport.
    /// Tracks HP — fires OnHealthDepleted when health reaches 0.
    /// </summary>
    [AddComponentMenu("STGEngine/Boss Placeholder")]
    public class BossPlaceholder : MonoBehaviour
    {
        private GameObject _visual;
        private MeshRenderer _meshRenderer;
        private List<PathKeyframe> _path;
        private bool _visible;

        private float _health = float.MaxValue;
        private float _maxHealth = float.MaxValue;

        // Transition tween state
        private bool _transitioning;
        private Vector3 _tweenFrom;
        private Vector3 _tweenTo;
        private float _tweenDuration;
        private float _tweenElapsed;

        /// <summary>Fired when boss HP reaches 0. Arg = this placeholder.</summary>
        public event Action<BossPlaceholder> OnHealthDepleted;

        private static Material _glMaterial;
        private static readonly Color PathLineColor = new Color(0.9f, 0.3f, 0.9f, 0.6f);
        private static readonly Color KeyframeMarkerColor = new Color(1f, 0.5f, 1f, 0.8f);
        private const float MarkerSize = 0.15f;

        // ── Public state ──

        public bool IsVisible => _visible;
        public float Health => _health;
        public float MaxHealth => _maxHealth;
        public float CollisionRadius => _collisionRadius;

        private const float VisualScale = 1.6f;
        private const float _collisionRadius = 2.5f; // WorldScale.BossVisualScale * 0.5

        private void Awake()
        {
            // Create visual child
            _visual = new GameObject("BossVisual");
            _visual.transform.SetParent(transform, false);

            var meshFilter = _visual.AddComponent<MeshFilter>();
            meshFilter.mesh = BulletMeshFactory.Create(MeshType.Diamond);

            _meshRenderer = _visual.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.8f, 0.2f, 0.8f, 0.7f);
            // Enable transparency
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            _meshRenderer.material = mat;

            _visual.transform.localScale = Vector3.one * VisualScale;

            Hide();
        }

        /// <summary>Set the boss movement path keyframes.</summary>
        public void SetPath(List<PathKeyframe> path)
        {
            _path = path;
        }

        /// <summary>Set boss health for the current spell card.</summary>
        public void SetHealth(float health)
        {
            _health = health;
            _maxHealth = health;
        }

        /// <summary>Apply damage. Fires OnHealthDepleted when HP reaches 0.</summary>
        public void ApplyDamage(float damage)
        {
            if (_health <= 0f) return;
            _health -= damage;
            if (_health <= 0f)
            {
                _health = 0f;
                OnHealthDepleted?.Invoke(this);
            }
        }

        // ── Transition tween ──

        public bool IsTransitioning => _transitioning;

        /// <summary>
        /// Start a position tween from current position to target.
        /// During transition, SetTime is ignored — position is driven by the tween.
        /// Call TickTransition each frame to advance.
        /// </summary>
        public void StartTransition(Vector3 target, float duration)
        {
            _tweenFrom = transform.position;
            _tweenTo = target;
            _tweenDuration = Mathf.Max(0.1f, duration);
            _tweenElapsed = 0f;
            _transitioning = true;
        }

        /// <summary>
        /// Advance the transition tween. Returns true while still transitioning.
        /// </summary>
        public bool TickTransition(float dt)
        {
            if (!_transitioning) return false;

            _tweenElapsed += dt;
            float t = Mathf.Clamp01(_tweenElapsed / _tweenDuration);
            // Smooth ease-in-out
            float smooth = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(_tweenFrom, _tweenTo, smooth);

            if (t >= 1f)
            {
                _transitioning = false;
                return false;
            }
            return true;
        }

        /// <summary>Cancel any active transition.</summary>
        public void CancelTransition()
        {
            _transitioning = false;
        }

        // ── Time ──

        /// <summary>Evaluate position at the given time and move the placeholder.</summary>
        public void SetTime(float t)
        {
            if (_transitioning) return; // Tween overrides path
            if (_path == null || _path.Count == 0) return;

            transform.position = EvaluatePathAt(t);
        }

        public void Show()
        {
            _visible = true;
            _visual.SetActive(true);
        }

        public void Hide()
        {
            _visible = false;
            _visual.SetActive(false);
        }

        /// <summary>Query the path position at a given time without moving the placeholder.</summary>
        public Vector3 EvaluatePathAt(float t)
        {
            if (_path == null || _path.Count == 0) return transform.position;
            if (_path.Count == 1) return _path[0].Position;

            // Before first keyframe
            if (t <= _path[0].Time) return _path[0].Position;

            // After last keyframe
            if (t >= _path[_path.Count - 1].Time) return _path[_path.Count - 1].Position;

            // Find segment
            for (int i = 0; i < _path.Count - 1; i++)
            {
                var a = _path[i];
                var b = _path[i + 1];
                if (t >= a.Time && t <= b.Time)
                {
                    float segLen = b.Time - a.Time;
                    float frac = segLen > 0f ? (t - a.Time) / segLen : 0f;
                    return Vector3.Lerp(a.Position, b.Position, frac);
                }
            }

            return _path[_path.Count - 1].Position;
        }

        /// <summary>Draw path lines and keyframe markers in the Game view.</summary>
        private void OnRenderObject()
        {
            if (!_visible || _path == null || _path.Count == 0) return;

            GetGLMaterial().SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            // Path lines between keyframes
            GL.Color(PathLineColor);
            for (int i = 0; i < _path.Count - 1; i++)
            {
                var a = _path[i].Position;
                var b = _path[i + 1].Position;
                GL.Vertex3(a.x, a.y, a.z);
                GL.Vertex3(b.x, b.y, b.z);
            }

            // Keyframe cross markers
            GL.Color(KeyframeMarkerColor);
            foreach (var kf in _path)
            {
                var p = kf.Position;
                // Horizontal cross
                GL.Vertex3(p.x - MarkerSize, p.y, p.z);
                GL.Vertex3(p.x + MarkerSize, p.y, p.z);
                // Vertical cross
                GL.Vertex3(p.x, p.y - MarkerSize, p.z);
                GL.Vertex3(p.x, p.y + MarkerSize, p.z);
                // Depth cross
                GL.Vertex3(p.x, p.y, p.z - MarkerSize);
                GL.Vertex3(p.x, p.y, p.z + MarkerSize);
            }

            GL.End();
            GL.PopMatrix();
        }

        private static Material GetGLMaterial()
        {
            if (_glMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                _glMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _glMaterial.SetInt("_ZWrite", 0);
            }
            return _glMaterial;
        }
    }
}
