using UnityEngine;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Draws a wireframe box gizmo to visualize the sandbox boundary.
    /// Bullets outside this box are considered out of bounds.
    /// </summary>
    [AddComponentMenu("STGEngine/Sandbox Boundary")]
    public class SandboxBoundary : MonoBehaviour
    {
        [SerializeField] private Vector3 _halfExtents = new Vector3(40f, 40f, 40f);
        [SerializeField] private Color _color = new Color(0.3f, 0.6f, 1f, 0.3f);

        /// <summary>Half-size of the boundary box along each axis.</summary>
        public Vector3 HalfExtents
        {
            get => _halfExtents;
            set => _halfExtents = value;
        }

        /// <summary>Full size (width, height, depth).</summary>
        public Vector3 Size => _halfExtents * 2f;

        private void OnDrawGizmos()
        {
            DrawWireBox();
        }

        private void OnDrawGizmosSelected()
        {
            DrawWireBox();
        }

        private void DrawWireBox()
        {
            Gizmos.color = _color;
            Gizmos.DrawWireCube(transform.position, _halfExtents * 2f);
        }

        /// <summary>
        /// Runtime visualization using GL lines (visible in Game view).
        /// Draws a wireframe box with 12 edges.
        /// </summary>
        private void OnRenderObject()
        {
            GetGLMaterial().SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(transform.localToWorldMatrix);
            GL.Begin(GL.LINES);
            GL.Color(_color);

            var h = _halfExtents;

            // 8 corners of the box
            var c0 = new Vector3(-h.x, -h.y, -h.z);
            var c1 = new Vector3( h.x, -h.y, -h.z);
            var c2 = new Vector3( h.x,  h.y, -h.z);
            var c3 = new Vector3(-h.x,  h.y, -h.z);
            var c4 = new Vector3(-h.x, -h.y,  h.z);
            var c5 = new Vector3( h.x, -h.y,  h.z);
            var c6 = new Vector3( h.x,  h.y,  h.z);
            var c7 = new Vector3(-h.x,  h.y,  h.z);

            // Bottom face
            GLLine(c0, c1); GLLine(c1, c2); GLLine(c2, c3); GLLine(c3, c0);
            // Top face
            GLLine(c4, c5); GLLine(c5, c6); GLLine(c6, c7); GLLine(c7, c4);
            // Vertical edges
            GLLine(c0, c4); GLLine(c1, c5); GLLine(c2, c6); GLLine(c3, c7);

            GL.End();
            GL.PopMatrix();
        }

        private static void GLLine(Vector3 a, Vector3 b)
        {
            GL.Vertex3(a.x, a.y, a.z);
            GL.Vertex3(b.x, b.y, b.z);
        }

        private static Material _glMaterial;

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
