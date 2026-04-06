using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 2D 泊松圆盘采样（Bridson 算法）。
    /// 在矩形区域内生成均匀分布的随机点，保证任意两点间距 ≥ minDistance。
    /// </summary>
    public static class PoissonDiskSampler
    {
        private const int MaxAttempts = 30;

        /// <summary>
        /// 在指定矩形区域内生成泊松圆盘采样点。
        /// </summary>
        /// <param name="width">区域宽度。</param>
        /// <param name="height">区域高度。</param>
        /// <param name="minDistance">任意两点间的最小距离。</param>
        /// <param name="seed">随机种子（确定性）。</param>
        /// <returns>采样点列表（2D 坐标，原点在区域左下角）。</returns>
        public static List<Vector2> Sample(float width, float height, float minDistance, int seed)
        {
            var rng = new System.Random(seed);
            float cellSize = minDistance / Mathf.Sqrt(2f);
            int gridW = Mathf.CeilToInt(width / cellSize);
            int gridH = Mathf.CeilToInt(height / cellSize);

            var grid = new int[gridW * gridH];
            for (int i = 0; i < grid.Length; i++) grid[i] = -1;

            var points = new List<Vector2>();
            var active = new List<int>();

            // First point
            var first = new Vector2((float)(rng.NextDouble() * width), (float)(rng.NextDouble() * height));
            points.Add(first);
            active.Add(0);
            int gx = Mathf.Clamp((int)(first.x / cellSize), 0, gridW - 1);
            int gy = Mathf.Clamp((int)(first.y / cellSize), 0, gridH - 1);
            grid[gy * gridW + gx] = 0;

            while (active.Count > 0)
            {
                int idx = (int)(rng.NextDouble() * active.Count);
                var point = points[active[idx]];
                bool found = false;

                for (int attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    float dist = minDistance + (float)(rng.NextDouble() * minDistance);
                    var candidate = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                    if (candidate.x < 0 || candidate.x >= width || candidate.y < 0 || candidate.y >= height)
                        continue;

                    int cx = Mathf.Clamp((int)(candidate.x / cellSize), 0, gridW - 1);
                    int cy = Mathf.Clamp((int)(candidate.y / cellSize), 0, gridH - 1);

                    bool tooClose = false;
                    for (int dy = -2; dy <= 2 && !tooClose; dy++)
                    {
                        for (int dx = -2; dx <= 2 && !tooClose; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH) continue;
                            int ni = grid[ny * gridW + nx];
                            if (ni >= 0 && Vector2.Distance(candidate, points[ni]) < minDistance)
                                tooClose = true;
                        }
                    }

                    if (!tooClose)
                    {
                        int newIdx = points.Count;
                        points.Add(candidate);
                        active.Add(newIdx);
                        grid[cy * gridW + cx] = newIdx;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    active.RemoveAt(idx);
            }

            return points;
        }
    }
}
