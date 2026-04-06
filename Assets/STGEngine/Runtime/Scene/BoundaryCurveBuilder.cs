using UnityEngine;
using System.Collections.Generic;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 边界曲线构建器。从路侧障碍物的实际位置拟合出左右两条平滑边界曲线。
    /// 采样每个 Chunk 中最靠近道路的障碍物，按弧长排序后做滑动平均平滑。
    /// BoundaryForce 基于这两条曲线做推力和硬限制。
    /// </summary>
    public class BoundaryCurveBuilder
    {
        /// <summary>边界采样点：弧长距离 → 横向偏移（正值）。</summary>
        public struct BoundarySample
        {
            public float ArcDistance;
            public float LateralOffset; // 到道路中心的距离（始终正值）
        }

        /// <summary>左侧边界采样点（按弧长排序）。</summary>
        public List<BoundarySample> LeftBoundary { get; } = new();

        /// <summary>右侧边界采样点（按弧长排序）。</summary>
        public List<BoundarySample> RightBoundary { get; } = new();

        // 平滑窗口大小
        private const int SmoothWindow = 3;

        // 弧长分桶大小（米），同一桶内取最靠近道路的障碍物
        private const float BucketSize = 5f;

        /// <summary>
        /// 从活跃 Chunk 的障碍物中重建边界曲线。
        /// </summary>
        public void Rebuild(IReadOnlyList<Chunk> activeChunks)
        {
            LeftBoundary.Clear();
            RightBoundary.Clear();

            // 收集所有路侧障碍物的（弧长, 横向偏移）
            var leftRaw = new List<BoundarySample>();
            var rightRaw = new List<BoundarySample>();

            foreach (var chunk in activeChunks)
            {
                if (!chunk.IsActive) continue;

                foreach (var obs in chunk.Obstacles)
                {
                    if (obs.Config == null) continue;
                    if (obs.Config.PlacementZone != PlacementZone.Roadside) continue;

                    float absOffset = Mathf.Abs(obs.LateralOffset);
                    var sample = new BoundarySample
                    {
                        ArcDistance = obs.ArcDistance,
                        LateralOffset = absOffset
                    };

                    if (obs.LateralOffset < 0f)
                        leftRaw.Add(sample);
                    else
                        rightRaw.Add(sample);
                }
            }

            // 分桶：每个桶内取最靠近道路的（最小 LateralOffset）
            BucketAndSmooth(leftRaw, LeftBoundary);
            BucketAndSmooth(rightRaw, RightBoundary);
        }

        /// <summary>
        /// 分桶取最近 + 滑动平均平滑。
        /// </summary>
        private void BucketAndSmooth(List<BoundarySample> raw, List<BoundarySample> output)
        {
            if (raw.Count == 0) return;

            // 按弧长排序
            raw.Sort((a, b) => a.ArcDistance.CompareTo(b.ArcDistance));

            // 分桶：每 BucketSize 米一个桶，取最小 LateralOffset
            var bucketed = new List<BoundarySample>();
            float bucketStart = raw[0].ArcDistance;
            float minOffset = raw[0].LateralOffset;
            float sumDist = raw[0].ArcDistance;
            int count = 1;

            for (int i = 1; i < raw.Count; i++)
            {
                if (raw[i].ArcDistance - bucketStart < BucketSize)
                {
                    if (raw[i].LateralOffset < minOffset)
                        minOffset = raw[i].LateralOffset;
                    sumDist += raw[i].ArcDistance;
                    count++;
                }
                else
                {
                    bucketed.Add(new BoundarySample
                    {
                        ArcDistance = sumDist / count,
                        LateralOffset = minOffset
                    });
                    bucketStart = raw[i].ArcDistance;
                    minOffset = raw[i].LateralOffset;
                    sumDist = raw[i].ArcDistance;
                    count = 1;
                }
            }
            bucketed.Add(new BoundarySample
            {
                ArcDistance = sumDist / count,
                LateralOffset = minOffset
            });

            // 滑动平均平滑
            for (int i = 0; i < bucketed.Count; i++)
            {
                float sum = 0f;
                int n = 0;
                for (int j = i - SmoothWindow; j <= i + SmoothWindow; j++)
                {
                    if (j < 0 || j >= bucketed.Count) continue;
                    sum += bucketed[j].LateralOffset;
                    n++;
                }
                output.Add(new BoundarySample
                {
                    ArcDistance = bucketed[i].ArcDistance,
                    LateralOffset = sum / n
                });
            }
        }

        /// <summary>
        /// 在指定弧长处查询边界的横向偏移（正值）。
        /// 在采样点之间线性插值。
        /// </summary>
        public float SampleAt(List<BoundarySample> boundary, float arcDistance, float fallback)
        {
            if (boundary.Count == 0) return fallback;
            if (arcDistance <= boundary[0].ArcDistance) return boundary[0].LateralOffset;
            if (arcDistance >= boundary[boundary.Count - 1].ArcDistance)
                return boundary[boundary.Count - 1].LateralOffset;

            for (int i = 0; i < boundary.Count - 1; i++)
            {
                if (arcDistance >= boundary[i].ArcDistance && arcDistance <= boundary[i + 1].ArcDistance)
                {
                    float t = (arcDistance - boundary[i].ArcDistance)
                            / (boundary[i + 1].ArcDistance - boundary[i].ArcDistance);
                    return Mathf.Lerp(boundary[i].LateralOffset, boundary[i + 1].LateralOffset, t);
                }
            }

            return fallback;
        }
    }
}
