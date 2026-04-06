using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// Catmull-Rom 样条线，定义通路中心线的 3D 轨迹。
    /// 支持弧长参数化（按距离均匀采样）和 Frenet 标架查询（位置、切线、法线）。
    /// 所有后续系统（Chunk 生成、障碍物散布、镜头方向）都基于此样条线。
    /// </summary>
    public class PathSpline
    {
        /// <summary>样条线控制点（世界坐标）。至少需要 2 个点。</summary>
        public List<SplinePoint> Points { get; set; } = new();

        /// <summary>弧长查找表的采样精度（每两个控制点之间的细分数）。</summary>
        public int ArcLengthSubdivisions { get; set; } = 64;

        // 弧长查找表：_arcLengths[i] = 从起点到参数 t=i/N 处的累计弧长
        private float[] _arcLengths;
        private float _totalArcLength;
        private bool _dirty = true;

        /// <summary>样条线总弧长（米）。</summary>
        public float TotalLength
        {
            get
            {
                if (_dirty) RebuildArcLengthTable();
                return _totalArcLength;
            }
        }

        public PathSpline() { }

        public PathSpline(params Vector3[] positions)
        {
            foreach (var p in positions)
                Points.Add(new SplinePoint { Position = p });
            _dirty = true;
        }

        /// <summary>
        /// 在指定弧长距离处采样样条线。
        /// 返回世界坐标位置、切线方向（归一化）、法线方向（归一化，指向右侧）。
        /// </summary>
        public SplineSample SampleAtDistance(float distance)
        {
            if (_dirty) RebuildArcLengthTable();
            if (Points.Count < 2)
            {
                var p = Points.Count > 0 ? Points[0].Position : Vector3.zero;
                return new SplineSample { Position = p, Tangent = Vector3.forward, Normal = Vector3.right };
            }

            float t = DistanceToParam(Mathf.Clamp(distance, 0f, _totalArcLength));
            return SampleAtParam(t);
        }

        /// <summary>
        /// 在归一化参数 t (0~1) 处采样样条线（非弧长参数化）。
        /// </summary>
        public SplineSample SampleAtParam(float t)
        {
            if (Points.Count < 2)
            {
                var p = Points.Count > 0 ? Points[0].Position : Vector3.zero;
                return new SplineSample { Position = p, Tangent = Vector3.forward, Normal = Vector3.right };
            }

            int segCount = Points.Count - 1;
            float scaled = t * segCount;
            int seg = Mathf.Clamp((int)scaled, 0, segCount - 1);
            float localT = scaled - seg;

            Vector3 pos = CatmullRomPosition(seg, localT);
            Vector3 tangent = CatmullRomTangent(seg, localT).normalized;

            // 法线：用 up 向量叉乘切线得到右方向（简化 Frenet 标架）
            // 对于大部分水平通路这足够了
            Vector3 up = Vector3.up;
            Vector3 normal = Vector3.Cross(up, tangent).normalized;
            if (normal.sqrMagnitude < 0.001f)
            {
                // 切线几乎垂直时用 forward 作为备选
                normal = Vector3.Cross(Vector3.forward, tangent).normalized;
            }

            return new SplineSample
            {
                Position = pos,
                Tangent = tangent,
                Normal = normal
            };
        }

        /// <summary>
        /// Catmull-Rom 插值：计算段 seg 在局部参数 t 处的位置。
        /// 使用 4 个控制点：P[seg-1], P[seg], P[seg+1], P[seg+2]，
        /// 边界处通过镜像扩展虚拟控制点。
        /// </summary>
        private Vector3 CatmullRomPosition(int seg, float t)
        {
            Vector3 p0 = GetPointClamped(seg - 1);
            Vector3 p1 = GetPointClamped(seg);
            Vector3 p2 = GetPointClamped(seg + 1);
            Vector3 p3 = GetPointClamped(seg + 2);

            float t2 = t * t;
            float t3 = t2 * t;

            // Catmull-Rom 矩阵形式
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        /// <summary>
        /// Catmull-Rom 切线：计算段 seg 在局部参数 t 处的切线（未归一化）。
        /// </summary>
        private Vector3 CatmullRomTangent(int seg, float t)
        {
            Vector3 p0 = GetPointClamped(seg - 1);
            Vector3 p1 = GetPointClamped(seg);
            Vector3 p2 = GetPointClamped(seg + 1);
            Vector3 p3 = GetPointClamped(seg + 2);

            float t2 = t * t;

            return 0.5f * (
                (-p0 + p2) +
                (4f * p0 - 10f * p1 + 8f * p2 - 2f * p3) * t +
                (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t2
            );
        }

        /// <summary>获取控制点位置，越界时 clamp 到首尾。</summary>
        private Vector3 GetPointClamped(int index)
        {
            if (index < 0) return Points[0].Position;
            if (index >= Points.Count) return Points[Points.Count - 1].Position;
            return Points[index].Position;
        }

        /// <summary>
        /// 重建弧长查找表。将样条线细分为 N 段，累计每段的弧长。
        /// </summary>
        private void RebuildArcLengthTable()
        {
            if (Points.Count < 2)
            {
                _arcLengths = new float[] { 0f };
                _totalArcLength = 0f;
                _dirty = false;
                return;
            }

            int segCount = Points.Count - 1;
            int totalSamples = segCount * ArcLengthSubdivisions + 1;
            _arcLengths = new float[totalSamples];
            _arcLengths[0] = 0f;

            Vector3 prev = CatmullRomPosition(0, 0f);
            for (int i = 1; i < totalSamples; i++)
            {
                float t = (float)i / (totalSamples - 1);
                float scaled = t * segCount;
                int seg = Mathf.Min((int)scaled, segCount - 1);
                float localT = scaled - seg;

                Vector3 curr = CatmullRomPosition(seg, localT);
                _arcLengths[i] = _arcLengths[i - 1] + Vector3.Distance(prev, curr);
                prev = curr;
            }

            _totalArcLength = _arcLengths[totalSamples - 1];
            _dirty = false;
        }

        /// <summary>
        /// 将弧长距离转换为样条线参数 t (0~1)。
        /// 使用二分查找在弧长查找表中定位。
        /// </summary>
        private float DistanceToParam(float distance)
        {
            if (_arcLengths == null || _arcLengths.Length < 2) return 0f;
            if (distance <= 0f) return 0f;
            if (distance >= _totalArcLength) return 1f;

            // 二分查找
            int lo = 0, hi = _arcLengths.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_arcLengths[mid] < distance) lo = mid;
                else hi = mid;
            }

            float segLength = _arcLengths[hi] - _arcLengths[lo];
            float frac = segLength > 0.0001f ? (distance - _arcLengths[lo]) / segLength : 0f;
            float param = (lo + frac) / (_arcLengths.Length - 1);
            return param;
        }

        /// <summary>标记样条线数据已变化，需要重建弧长表。</summary>
        public void SetDirty()
        {
            _dirty = true;
        }
    }

    /// <summary>样条线控制点。</summary>
    public class SplinePoint
    {
        /// <summary>控制点世界坐标位置。</summary>
        public Vector3 Position { get; set; }
    }

    /// <summary>样条线采样结果。</summary>
    public struct SplineSample
    {
        /// <summary>世界坐标位置。</summary>
        public Vector3 Position;
        /// <summary>切线方向（归一化，指向前进方向）。</summary>
        public Vector3 Tangent;
        /// <summary>法线方向（归一化，指向通路右侧）。</summary>
        public Vector3 Normal;
    }
}
