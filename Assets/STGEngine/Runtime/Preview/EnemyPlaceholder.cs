using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Rendering;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Visual placeholder for a single enemy instance during wave editing.
    /// Shows a colored mesh at the interpolated path position,
    /// and draws the movement path as GL lines in the 3D viewport.
    /// </summary>
    public class EnemyPlaceholder : MonoBehaviour
    {
        private GameObject _visual;
        private MeshRenderer _meshRenderer;
        private List<PathKeyframe> _path;
        private float _spawnDelay;
        private Vector3 _spawnOffset;
        private bool _visible;
        private Color _color = Color.white;
        private Color _pathColor;

        private static Material _glMaterial;
        private const float MarkerSize = 0.12f;

        /// <summary>
        /// Initialize the visual mesh and color.
        /// Call once after creation.
        /// </summary>
        public void Setup(MeshType meshType, Color color, float scale)
        {
            _color = color;
            _pathColor = new Color(color.r, color.g, color.b, 0.5f);

            if (_visual != null) Destroy(_visual);

            _visual = new GameObject("EnemyVisual");
            _visual.transform.SetParent(transform, false);

            var meshFilter = _visual.AddComponent<MeshFilter>();
            meshFilter.mesh = BulletMeshFactory.Create(meshType);

            _meshRenderer = _visual.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(color.r, color.g, color.b, 0.7f);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            _meshRenderer.material = mat;

            _visual.transform.localScale = Vector3.one * scale;
            _visual.SetActive(_visible);
        }

        /// <summary>Set the movement path, spawn delay, and world offset for this enemy.</summary>
        public void SetPath(List<PathKeyframe> path, float spawnDelay, Vector3 spawnOffset = default)
        {
            _path = path;
            _spawnDelay = spawnDelay;
            _spawnOffset = spawnOffset;
        }

        /// <summary>
        /// Evaluate position at the given wave-global time.
        /// Accounts for spawnDelay: enemy appears at (t - spawnDelay) along its path.
        /// </summary>
        public void SetTime(float waveTime)
        {
            if (_path == null || _path.Count == 0) return;

            float localTime = waveTime - _spawnDelay;

            // Before spawn: hide
            if (localTime < 0f)
            {
                if (_visual != null) _visual.SetActive(false);
                return;
            }

            // After path ends: stay at last position
            if (_visual != null && !_visual.activeSelf && _visible)
                _visual.SetActive(true);

            transform.position = EvaluatePath(localTime) + _spawnOffset;
        }

        public void Show()
        {
            _visible = true;
            if (_visual != null) _visual.SetActive(true);
        }

        public void Hide()
        {
            _visible = false;
            if (_visual != null) _visual.SetActive(false);
        }

        public bool IsVisible => _visible;

        private Vector3 EvaluatePath(float t)
        {
            if (_path.Count == 1) return _path[0].Position;
            if (t <= _path[0].Time) return _path[0].Position;
            if (t >= _path[_path.Count - 1].Time) return _path[_path.Count - 1].Position;

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

            // Path lines
            GL.Color(_pathColor);
            for (int i = 0; i < _path.Count - 1; i++)
            {
                var a = _path[i].Position + _spawnOffset;
                var b = _path[i + 1].Position + _spawnOffset;
                GL.Vertex3(a.x, a.y, a.z);
                GL.Vertex3(b.x, b.y, b.z);
            }

            // Keyframe cross markers
            var markerColor = new Color(_color.r, _color.g, _color.b, 0.7f);
            GL.Color(markerColor);
            foreach (var kf in _path)
            {
                var p = kf.Position + _spawnOffset;
                GL.Vertex3(p.x - MarkerSize, p.y, p.z);
                GL.Vertex3(p.x + MarkerSize, p.y, p.z);
                GL.Vertex3(p.x, p.y - MarkerSize, p.z);
                GL.Vertex3(p.x, p.y + MarkerSize, p.z);
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
