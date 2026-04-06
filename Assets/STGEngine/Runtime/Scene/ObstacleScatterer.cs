using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 沿样条线为 Chunk 散布障碍物。
    /// 在样条线局部坐标系（弧长 × 法线偏移）中做泊松采样，
    /// 然后映射到世界坐标放置预制体。
    /// </summary>
    public class ObstacleScatterer
    {
        private readonly ObstaclePool _pool;
        private readonly PathProfile _profile;

        public ObstacleScatterer(ObstaclePool pool, PathProfile profile)
        {
            _pool = pool;
            _profile = profile;
        }

        /// <summary>
        /// 为指定 Chunk 散布障碍物。返回放置的障碍物信息列表。
        /// </summary>
        public List<ObstacleInstance> Scatter(Chunk chunk, List<ObstacleConfig> configs, float hazardFrequency)
        {
            var instances = new List<ObstacleInstance>();

            foreach (var config in configs)
            {
                if (config.PrefabVariants.Count == 0) continue;

                if (config.PlacementZone == PlacementZone.Roadside)
                {
                    ScatterRoadside(chunk, config, instances);
                }
                else if (config.PlacementZone == PlacementZone.Interior)
                {
                    ScatterInterior(chunk, config, hazardFrequency, instances);
                }
            }

            return instances;
        }

        /// <summary>在通路两侧散布障碍物。左右两侧独立采样，避免宽通路时大量点被过滤。</summary>
        private void ScatterRoadside(Chunk chunk, ObstacleConfig config, List<ObstacleInstance> instances)
        {
            // 左右两侧各自独立采样
            ScatterOneSide(chunk, config, instances, -1); // 左侧
            ScatterOneSide(chunk, config, instances, +1); // 右侧
        }

        /// <summary>在通路一侧散布障碍物。</summary>
        /// <param name="side">-1 = 左侧，+1 = 右侧。</param>
        private void ScatterOneSide(Chunk chunk, ObstacleConfig config, List<ObstacleInstance> instances, int side)
        {
            float chunkLen = chunk.Length;
            float bandWidth = 30f; // 路侧带宽度（从路边向外延伸多远）

            // 泊松采样在 2D 矩形中：X = 路侧带宽度，Y = 沿样条线长度
            int seed = chunk.Index * 31337 + (side > 0 ? 17 : 0);
            var points = PoissonDiskSampler.Sample(bandWidth, chunkLen, config.MinSpacing, seed);

            var rng = new System.Random(seed + 7);

            foreach (var pt in points)
            {
                // 沿样条线的弧长位置
                float dist = chunk.StartDistance + pt.y;
                var sample = _profile.SampleAt(dist);
                float localHalfWidth = sample.Width * 0.5f;

                // 横向偏移：从路边开始向外
                // pt.x 在 [0, bandWidth] 范围内，映射到 [halfWidth, halfWidth + bandWidth]
                float lateralOffset = (localHalfWidth + pt.x) * side;

                // 世界坐标
                Vector3 worldPos = sample.Position + sample.Normal * lateralOffset;

                // 随机选取变体
                string prefabPath = config.PrefabVariants[rng.Next(config.PrefabVariants.Count)];
                var obj = _pool.Get(prefabPath);
                if (obj == null) continue;

                float scale = Mathf.Lerp(config.ScaleRange.x, config.ScaleRange.y, (float)rng.NextDouble());
                float rotY = Mathf.Lerp(config.RotationRange.x, config.RotationRange.y, (float)rng.NextDouble());

                // Y 抬高让物体站在地面上
                var renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    worldPos.y += renderer.bounds.extents.y;
                }

                obj.transform.position = worldPos;
                obj.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                obj.transform.localScale = obj.transform.localScale * scale;

                instances.Add(new ObstacleInstance
                {
                    GameObject = obj,
                    PrefabPath = prefabPath,
                    Config = config,
                    ArcDistance = dist,
                    LateralOffset = lateralOffset
                });
            }
        }

        /// <summary>在通路内部散布危险障碍物。</summary>
        private void ScatterInterior(Chunk chunk, ObstacleConfig config, float hazardFrequency, List<ObstacleInstance> instances)
        {
            if (hazardFrequency <= 0f) return;

            float chunkLen = chunk.Length;
            // Expected count based on frequency (per 100m)
            float expectedCount = hazardFrequency * chunkLen / 100f;

            int seed = chunk.Index * 51749;
            var rng = new System.Random(seed);

            int count = Mathf.FloorToInt(expectedCount);
            if ((float)rng.NextDouble() < (expectedCount - count)) count++;

            for (int i = 0; i < count; i++)
            {
                float alongT = (float)rng.NextDouble();
                float dist = chunk.StartDistance + alongT * chunkLen;
                var sample = _profile.SampleAt(dist);
                float localHalfWidth = sample.Width * 0.5f;

                // Random position inside the path
                float lateralOffset = ((float)rng.NextDouble() * 2f - 1f) * localHalfWidth * 0.7f;

                Vector3 worldPos = sample.Position + sample.Normal * lateralOffset;

                string prefabPath = config.PrefabVariants[rng.Next(config.PrefabVariants.Count)];
                var obj = _pool.Get(prefabPath);
                if (obj == null) continue;

                float scale = Mathf.Lerp(config.ScaleRange.x, config.ScaleRange.y, (float)rng.NextDouble());
                float rotY = Mathf.Lerp(config.RotationRange.x, config.RotationRange.y, (float)rng.NextDouble());

                // Adjust Y so object sits on ground
                var renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    float halfHeight = renderer.bounds.extents.y;
                    worldPos.y += halfHeight;
                }

                obj.transform.position = worldPos;
                obj.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                obj.transform.localScale = obj.transform.localScale * scale;

                instances.Add(new ObstacleInstance
                {
                    GameObject = obj,
                    PrefabPath = prefabPath,
                    Config = config,
                    ArcDistance = dist,
                    LateralOffset = lateralOffset
                });
            }
        }

        /// <summary>归还一个 Chunk 的所有障碍物到池中。</summary>
        public void ReturnAll(List<ObstacleInstance> instances)
        {
            foreach (var inst in instances)
            {
                _pool.Return(inst.PrefabPath, inst.GameObject);
            }
            instances.Clear();
        }
    }

    /// <summary>障碍物运行时实例信息。</summary>
    public class ObstacleInstance
    {
        /// <summary>障碍物 GameObject。</summary>
        public GameObject GameObject { get; set; }
        /// <summary>预制体资源路径（用于归还到正确的池）。</summary>
        public string PrefabPath { get; set; }
        /// <summary>所属的 ObstacleConfig。</summary>
        public ObstacleConfig Config { get; set; }
        /// <summary>在样条线上的弧长位置。</summary>
        public float ArcDistance { get; set; }
        /// <summary>相对于样条线中心的横向偏移。</summary>
        public float LateralOffset { get; set; }
    }
}
