using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Visualizes collision shapes as wireframe overlays on bullet positions.
    /// Reads CollisionShape from the current BulletPattern and draws GL lines
    /// at each bullet position. Only active in preview mode.
    /// </summary>
    [AddComponentMenu("STGEngine/Collision Visualizer")]
    public class CollisionVisualizer : MonoBehaviour
    {
        [SerializeField] private Color _wireColor = new Color(0f, 1f, 0.5f, 0.4f);
        [SerializeField] private int _circleSegments = 16;

        private PatternPreviewer _previewer;
        private bool _enabled = true;

        /// <summary>Toggle collision shape visualization.</summary>
        public bool ShowCollision
        {
            get => _enabled;
            set => _enabled = value;
        }

        private void Awake()
        {
            _previewer = GetComponent<PatternPreviewer>();
        }

        private void OnRenderObject()
        {
            if (!_enabled || _previewer == null || _previewer.Pattern == null)
                return;

            var collision = _previewer.Pattern.Collision;
            if (collision == null) return;

            var states = _previewer.CurrentStates;
            if (states == null || states.Count == 0) return;

            GetGLMaterial().SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            GL.Color(_wireColor);

            foreach (var state in states)
            {
                switch (collision.ShapeType)
                {
                    case CollisionShapeType.Sphere:
                        DrawWireCircle(state.Position, Vector3.up, collision.Radius);
                        DrawWireCircle(state.Position, Vector3.right, collision.Radius);
                        break;
                    case CollisionShapeType.Capsule:
                        DrawWireCircle(state.Position, Vector3.up, collision.Radius);
                        DrawWireCircle(state.Position + Vector3.up * collision.Height * 0.5f, Vector3.up, collision.Radius);
                        DrawWireCircle(state.Position - Vector3.up * collision.Height * 0.5f, Vector3.up, collision.Radius);
                        break;
                    case CollisionShapeType.Box:
                        DrawWireBox(state.Position, collision.HalfExtents);
                        break;
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private void DrawWireCircle(Vector3 center, Vector3 normal, float radius)
        {
            var right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.Cross(normal, Vector3.right);
            right.Normalize();
            var fwd = Vector3.Cross(right, normal).normalized;

            for (int i = 0; i < _circleSegments; i++)
            {
                float a0 = 2f * Mathf.PI * i / _circleSegments;
                float a1 = 2f * Mathf.PI * (i + 1) / _circleSegments;

                var p0 = center + (right * Mathf.Cos(a0) + fwd * Mathf.Sin(a0)) * radius;
                var p1 = center + (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * radius;

                GL.Vertex3(p0.x, p0.y, p0.z);
                GL.Vertex3(p1.x, p1.y, p1.z);
            }
        }

        private void DrawWireBox(Vector3 center, Vector3 half)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                corners[i] = center + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z
                );
            }

            // 12 edges of a box
            int[] edges = { 0,1, 2,3, 4,5, 6,7, 0,2, 1,3, 4,6, 5,7, 0,4, 1,5, 2,6, 3,7 };
            for (int i = 0; i < edges.Length; i += 2)
            {
                var a = corners[edges[i]];
                var b = corners[edges[i + 1]];
                GL.Vertex3(a.x, a.y, a.z);
                GL.Vertex3(b.x, b.y, b.z);
            }
        }

        private static Material _glMaterial;

        private static Material GetGLMaterial()
        {
            if (_glMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                _glMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _glMaterial.SetInt("_ZWrite", 0);
            }
            return _glMaterial;
        }
    }
}
