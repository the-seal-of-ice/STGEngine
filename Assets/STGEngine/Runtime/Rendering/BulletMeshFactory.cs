using UnityEngine;
using STGEngine.Core.DataModel;

namespace STGEngine.Runtime.Rendering
{
    /// <summary>
    /// Creates bullet meshes for each MeshType.
    /// Sphere uses Unity primitive; others are procedurally generated.
    /// </summary>
    public static class BulletMeshFactory
    {
        /// <summary>
        /// Create a mesh for the given MeshType.
        /// Caller is responsible for lifetime management.
        /// </summary>
        public static Mesh Create(MeshType meshType)
        {
            switch (meshType)
            {
                case MeshType.Diamond: return CreateDiamond();
                case MeshType.Arrow: return CreateArrow();
                case MeshType.Rice: return CreateRice();
                case MeshType.Sphere:
                default: return CreateSphere();
            }
        }

        private static Mesh CreateSphere()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            return mesh;
        }

        private static Mesh CreateDiamond()
        {
            var mesh = new Mesh { name = "BulletDiamond" };

            // Octahedron (diamond shape)
            var verts = new Vector3[]
            {
                new(0, 1, 0),   // top
                new(1, 0, 0),   // +x
                new(0, 0, 1),   // +z
                new(-1, 0, 0),  // -x
                new(0, 0, -1),  // -z
                new(0, -1, 0)   // bottom
            };

            var tris = new int[]
            {
                0,1,2, 0,2,3, 0,3,4, 0,4,1, // top
                5,2,1, 5,3,2, 5,4,3, 5,1,4  // bottom
            };

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateArrow()
        {
            var mesh = new Mesh { name = "BulletArrow" };

            // Simple arrow pointing forward (+Y)
            var verts = new Vector3[]
            {
                new(0, 1.5f, 0),     // tip
                new(-0.4f, 0.3f, 0), // left wing
                new(0.4f, 0.3f, 0),  // right wing
                new(-0.15f, 0.3f, 0),// left body top
                new(0.15f, 0.3f, 0), // right body top
                new(-0.15f, -1f, 0), // left body bottom
                new(0.15f, -1f, 0),  // right body bottom
                // Back faces (z offset for thickness)
                new(0, 1.5f, 0.15f),
                new(-0.4f, 0.3f, 0.15f),
                new(0.4f, 0.3f, 0.15f),
                new(-0.15f, 0.3f, 0.15f),
                new(0.15f, 0.3f, 0.15f),
                new(-0.15f, -1f, 0.15f),
                new(0.15f, -1f, 0.15f),
            };

            var tris = new int[]
            {
                // Front
                0,2,1, 3,4,6, 3,6,5,
                // Back
                7,8,9, 10,13,11, 10,12,13,
                // Sides (simplified)
                0,1,8, 0,8,7, 0,7,9, 0,9,2,
                5,6,13, 5,13,12,
            };

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRice()
        {
            var mesh = new Mesh { name = "BulletRice" };

            // Elongated capsule-like shape (simplified as stretched sphere segments)
            int segments = 8;
            int rings = 6;
            var verts = new Vector3[(segments + 1) * (rings + 1)];
            var tris = new int[segments * rings * 6];

            float scaleY = 2f; // Elongation factor

            int vi = 0;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float y = Mathf.Cos(phi) * scaleY;
                float ringRadius = Mathf.Sin(phi);

                for (int s = 0; s <= segments; s++)
                {
                    float theta = 2f * Mathf.PI * s / segments;
                    verts[vi++] = new Vector3(
                        ringRadius * Mathf.Cos(theta),
                        y,
                        ringRadius * Mathf.Sin(theta)
                    );
                }
            }

            int ti = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s;
                    int b = a + segments + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
