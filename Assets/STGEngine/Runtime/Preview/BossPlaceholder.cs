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
    /// </summary>
    [AddComponentMenu("STGEngine/Boss Placeholder")]
    public class BossPlaceholder : MonoBehaviour
    {
        private GameObject _visual;
        private MeshRenderer _meshRenderer;
        private List<PathKeyframe> _path;
        private bool _visible;

        private static Material _glMaterial;
        private static readonly Color PathLineColor = new Color(0.9f, 0.3f, 0.9f, 0.6f);
        private static readonly Color KeyframeMarkerColor = new Color(1f, 0.5f, 1f, 0.8f);
        private const float MarkerSize = 0.15f;

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

            _visual.transform.localScale = Vector3.one * 1.6f;

            Hide();
        }

        /// <summary>Set the boss movement path keyframes.</summary>
        public void SetPath(List<PathKeyframe> path)
        {
            _path = path;
        }

        /// <summary>Evaluate position at the given time and move the placeholder.</summary>
        public void SetTime(float t)
        {
            if (_path == null || _path.Count == 0) return;

            transform.position = EvaluatePath(t);
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

        public bool IsVisible => _visible;

        /// <summary>Linear interpolation along the path keyframes.</summary>
        private Vector3 EvaluatePath(float t)
        {
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
