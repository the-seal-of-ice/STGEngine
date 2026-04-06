using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 沿样条线为 Chunk 生成地面 mesh（世界坐标，一次性构建）。
    /// 地面宽度 = 通路宽度 + 两侧路侧带，覆盖障碍物区域。
    /// UV 用弧长做 V、法线距离做 U，确保纹理大小恒定。
    /// </summary>
    public static class GroundMeshBuilder
    {
        /// <summary>沿样条线方向的细分段数。</summary>
        private const int SegmentsAlong = 48;

        /// <summary>垂直于样条线方向的细分段数。</summary>
        private const int SegmentsAcross = 6;

        /// <summary>UV 缩放：每多少米重复一次纹理。</summary>
        private const float UvWorldScale = 5f;

        /// <summary>路侧带宽度（米），地面向通路两侧额外延伸的距离。</summary>
        private const float RoadsideExtension = 40f;

        /// <summary>
        /// 为指定 Chunk 生成地面 mesh（世界坐标）。
        /// 地面覆盖通路 + 两侧路侧带，总宽度 = Width + RoadsideExtension * 2。
        /// </summary>
        public static Mesh Build(Chunk chunk, PathProfile profile)
        {
            int vertsPerRow = SegmentsAcross + 1;
            int rowCount = SegmentsAlong + 1;
            int vertCount = vertsPerRow * rowCount;
            int triCount = SegmentsAcross * SegmentsAlong * 6;

            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var colors = new Color[vertCount];
            var triangles = new int[triCount];

            for (int z = 0; z < rowCount; z++)
            {
                float tz = (float)z / SegmentsAlong;
                float dist = Mathf.Lerp(chunk.StartDistance, chunk.EndDistance, tz);

                PathSample sample = profile.SampleAt(dist);
                float halfWidth = sample.Width * 0.5f;
                // 地面总半宽 = 通路半宽 + 路侧延伸
                float totalHalfWidth = halfWidth + RoadsideExtension;

                for (int x = 0; x < vertsPerRow; x++)
                {
                    float tx = (float)x / SegmentsAcross;
                    int idx = z * vertsPerRow + x;

                    float lateralOffset = Mathf.Lerp(-totalHalfWidth, totalHalfWidth, tx);
                    vertices[idx] = sample.Position + sample.Normal * lateralOffset;

                    uvs[idx] = new Vector2(lateralOffset / UvWorldScale, dist / UvWorldScale);

                    // 顶点色：通路内为亮色，路侧为暗色，用于区分道路和路侧
                    float insidePath = Mathf.Abs(lateralOffset) < halfWidth ? 1f : 0f;
                    // 平滑过渡：在路边 2m 范围内渐变
                    float edgeDist = Mathf.Abs(lateralOffset) - halfWidth;
                    float blend = Mathf.Clamp01(1f - edgeDist / 2f);
                    colors[idx] = Color.Lerp(new Color(0.25f, 0.3f, 0.2f), new Color(0.5f, 0.55f, 0.4f), blend);
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
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
