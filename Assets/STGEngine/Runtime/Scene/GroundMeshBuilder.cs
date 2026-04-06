// Assets/STGEngine/Runtime/Scene/GroundMeshBuilder.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 为 Chunk 生成地面 mesh。地面沿 Z 轴延伸，宽度跟随 PathProfile 变化。
    /// Drift 偏移不嵌入 mesh（由 ScrollController 在运行时处理），
    /// UV 使用世界坐标确保纹理大小恒定。
    /// </summary>
    public static class GroundMeshBuilder
    {
        /// <summary>沿 Z 轴的细分段数。</summary>
        private const int SegmentsZ = 20;

        /// <summary>沿 X 轴的细分段数（横向）。</summary>
        private const int SegmentsX = 4;

        /// <summary>UV 缩放：每多少米重复一次纹理。</summary>
        private const float UvWorldScale = 5f;

        /// <summary>
        /// 为指定 Chunk 生成地面 mesh。
        /// 地面以通路中心为 X=0，宽度由 WidthCurve 决定。
        /// Drift 不嵌入顶点（由 ScrollController 运行时补偿）。
        /// UV 基于世界坐标，确保棋盘格大小恒定不随宽度变化。
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
                // 世界 Z 坐标用于 UV（Chunk 起始距离 + 局部 Z）
                float worldZ = chunk.StartDistance + tz * chunk.Length;

                for (int x = 0; x < vertsPerRow; x++)
                {
                    float tx = (float)x / SegmentsX;
                    int idx = z * vertsPerRow + x;

                    // 不含 Drift，只有宽度
                    float xPos = Mathf.Lerp(-halfWidth, halfWidth, tx);
                    vertices[idx] = new Vector3(xPos, 0f, zPos);

                    // 世界坐标 UV：棋盘格大小恒定
                    uvs[idx] = new Vector2(xPos / UvWorldScale, worldZ / UvWorldScale);
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
