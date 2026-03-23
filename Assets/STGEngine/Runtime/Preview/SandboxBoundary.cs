using UnityEngine;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Draws a wireframe sphere gizmo to visualize the sandbox boundary.
    /// Bullets outside this radius are considered out of bounds.
    /// </summary>
    [AddComponentMenu("STGEngine/Sandbox Boundary")]
    public class SandboxBoundary : MonoBehaviour
    {
        [SerializeField] private float _radius = 20f;
        [SerializeField] private Color _color = new Color(0.3f, 0.6f, 1f, 0.3f);
        [SerializeField] private int _segments = 64;

        public float Radius => _radius;

        private void OnDrawGizmos()
        {
            DrawWireSphere();
        }

        private void OnDrawGizmosSelected()
        {
            DrawWireSphere();
        }

        private void DrawWireSphere()
        {
            Gizmos.color = _color;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }

        /// <summary>
        /// Runtime visualization using GL lines (visible in Game view).
        /// </summary>
        private void OnRenderObject()
        {
            DrawGLCircle(Vector3.up, Vector3.forward);    // XZ plane
            DrawGLCircle(Vector3.right, Vector3.up);       // YZ plane
            DrawGLCircle(Vector3.forward, Vector3.up);     // XY plane
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

        private void DrawGLCircle(Vector3 normal, Vector3 up)
        {
            GetGLMaterial().SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(transform.localToWorldMatrix);
            GL.Begin(GL.LINES);
            GL.Color(_color);

            var right = Vector3.Cross(normal, up).normalized;
            var fwd = Vector3.Cross(right, normal).normalized;

            for (int i = 0; i < _segments; i++)
            {
                float a0 = (2f * Mathf.PI * i) / _segments;
                float a1 = (2f * Mathf.PI * (i + 1)) / _segments;

                var p0 = (right * Mathf.Cos(a0) + fwd * Mathf.Sin(a0)) * _radius;
                var p1 = (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * _radius;

                GL.Vertex3(p0.x, p0.y, p0.z);
                GL.Vertex3(p1.x, p1.y, p1.z);
            }

            GL.End();
            GL.PopMatrix();
        }
    }
}
