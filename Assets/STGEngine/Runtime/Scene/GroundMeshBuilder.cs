// Assets/STGEngine/Runtime/Scene/GroundMeshBuilder.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 为 Chunk 生成地面 mesh。地面沿 Z 轴延伸，宽度和中心偏移
    /// 跟随 PathProfile 曲线变化，使用 Hermite 插值保证 C1 连续。
    /// </summary>
    public static class GroundMeshBuilder
    {
        /// <summary>沿 Z 轴的细分段数。越多越平滑，但顶点越多。</summary>
        private const int SegmentsZ = 20;

        /// <summary>沿 X 轴的细分段数（横向）。</summary>
        private const int SegmentsX = 1;

        /// <summary>
        /// 为指定 Chunk 生成地面 mesh。
        /// 地面 Y 坐标固定为 0（通路底部），顶点沿 Z 轴分布，
        /// 宽度和 X 偏移由 Chunk 的 StartSample/EndSample Hermite 插值决定。
        /// </summary>
        public static Mesh Build(Chunk chunk)
        {
            int vertsPerRow = SegmentsX + 1;
            int rowCount = SegmentsZ + 1;
            int vertCount = vertsPerRow * rowCount;
            int triCount = SegmentsX * SegmentsZ * 6;

            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[triCount];

            for (int z = 0; z < rowCount; z++)
            {
                float tz = (float)z / SegmentsZ;
                PathSample sample = chunk.LerpAt(tz);
                float halfWidth = sample.Width * 0.5f;
                float zPos = tz * chunk.Length;

                for (int x = 0; x < vertsPerRow; x++)
                {
                    float tx = (float)x / SegmentsX;
                    int idx = z * vertsPerRow + x;

                    float xPos = Mathf.Lerp(-halfWidth, halfWidth, tx) + sample.Drift;
                    vertices[idx] = new Vector3(xPos, 0f, zPos);
                    uvs[idx] = new Vector2(tx, tz);
                }
            }

            int tri = 0;
            for (int z = 0; z < SegmentsZ; z++)
            {
                for (int x = 0; x < SegmentsX; x++)
                {
                    int bl = z * vertsPerRow + x;
                    int br = bl + 1;
                    int tl = bl + vertsPerRow;
                    int tr = tl + 1;

                    triangles[tri++] = bl;
                    triangles[tri++] = tl;
                    triangles[tri++] = br;
                    triangles[tri++] = br;
                    triangles[tri++] = tl;
                    triangles[tri++] = tr;
                }
            }

            var mesh = new Mesh
            {
                name = $"GroundChunk_{chunk.Index}",
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
