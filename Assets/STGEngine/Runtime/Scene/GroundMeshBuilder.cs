using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 沿样条线为 Chunk 生成地面 mesh（世界坐标，一次性构建）。
    /// 每行顶点的中心在样条线上，左右沿法线方向按宽度展开。
    /// UV 用弧长做 V、法线距离做 U，确保纹理大小恒定。
    /// </summary>
    public static class GroundMeshBuilder
    {
        /// <summary>沿样条线方向的细分段数。</summary>
        private const int SegmentsAlong = 24;

        /// <summary>垂直于样条线方向的细分段数。</summary>
        private const int SegmentsAcross = 4;

        /// <summary>UV 缩放：每多少米重复一次纹理。</summary>
        private const float UvWorldScale = 5f;

        /// <summary>
        /// 为指定 Chunk 生成地面 mesh（世界坐标）。
        /// 顶点沿样条线分布，每行的中心点在样条线上，
        /// 左右沿法线方向按 WidthCurve 展开。
        /// </summary>
        public static Mesh Build(Chunk chunk, PathProfile profile)
        {
            int vertsPerRow = SegmentsAcross + 1;
            int rowCount = SegmentsAlong + 1;
            int vertCount = vertsPerRow * rowCount;
            int triCount = SegmentsAcross * SegmentsAlong * 6;

            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[triCount];

            for (int z = 0; z < rowCount; z++)
            {
                float tz = (float)z / SegmentsAlong;
                float dist = Mathf.Lerp(chunk.StartDistance, chunk.EndDistance, tz);

                PathSample sample = profile.SampleAt(dist);
                float halfWidth = sample.Width * 0.5f;

                for (int x = 0; x < vertsPerRow; x++)
                {
                    float tx = (float)x / SegmentsAcross;
                    int idx = z * vertsPerRow + x;

                    // 从样条线中心沿法线方向展开（世界坐标）
                    float lateralOffset = Mathf.Lerp(-halfWidth, halfWidth, tx);
                    vertices[idx] = sample.Position + sample.Normal * lateralOffset;

                    // UV：弧长做 V，横向距离做 U
                    uvs[idx] = new Vector2(lateralOffset / UvWorldScale, dist / UvWorldScale);
                }
            }

            int tri = 0;
            for (int z = 0; z < SegmentsAlong; z++)
            {
                for (int x = 0; x < SegmentsAcross; x++)
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
