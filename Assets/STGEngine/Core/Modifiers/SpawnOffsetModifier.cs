using UnityEngine;
using STGEngine.Core.Emitters;
using STGEngine.Core.Random;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// 发射位置偏移修饰器。在 Emitter 输出之后一次性修改 BulletSpawnData。
    /// 
    /// 支持三种独立的随机扰动：
    /// - Position：发射点空间散布（XYZ 各轴独立范围）
    /// - Direction：发射方向旋转抖动（角度范围）
    /// - Speed：速度扰动（±范围）
    /// 
    /// 每种扰动可独立启用/禁用（Range 为 0 即禁用）。
    /// 所有随机行为通过 DeterministicRng 保证确定性重放。
    /// </summary>
    [TypeTag("spawn_offset")]
    public class SpawnOffsetModifier : ISpawnModifier
    {
        public enum DistributionMode
        {
            /// <summary>均匀分布：[-Range, +Range] 内等概率。</summary>
            Uniform,
            /// <summary>正态分布：Range 为标准差，约 68% 落在 ±Range 内。</summary>
            Normal
        }

        public string TypeName => "spawn_offset";
        public bool RequiresSimulation => false;

        // ── Position 偏移 ──

        /// <summary>分布模式。</summary>
        public DistributionMode Mode { get; set; } = DistributionMode.Uniform;

        /// <summary>各轴位置偏移范围。Uniform 模式下为 [-x,+x]，Normal 模式下为标准差。</summary>
        public Vector3 PositionRange { get; set; } = new(1f, 0f, 1f);

        // ── Direction 旋转 ──

        /// <summary>方向抖动最大角度（度）。0 = 不旋转方向。</summary>
        public float DirectionJitter { get; set; } = 0f;

        /// <summary>
        /// 是否将方向旋转与位置偏移联动。
        /// true = 方向从原点指向偏移后的位置（放射散布效果）。
        /// false = 方向独立随机旋转（纯抖动效果）。
        /// 仅当 DirectionJitter > 0 或 LinkDirectionToOffset = true 时生效。
        /// </summary>
        public bool LinkDirectionToOffset { get; set; } = false;

        // ── Speed 扰动 ──

        /// <summary>速度扰动范围。最终速度 = 原速度 + Random(-range, +range)。0 = 不扰动。</summary>
        public float SpeedJitter { get; set; } = 0f;

        public SpawnOffsetModifier() { }

        public void Apply(ref BulletSpawnData spawn, int bulletIndex, DeterministicRng rng)
        {
            // ── Position ──
            Vector3 offset = Vector3.zero;
            if (PositionRange.sqrMagnitude > 0.0001f)
            {
                offset = Mode == DistributionMode.Uniform
                    ? SampleUniform(rng, PositionRange)
                    : SampleNormal(rng, PositionRange);
                spawn.Position += offset;
            }

            // ── Direction ──
            if (LinkDirectionToOffset && offset.sqrMagnitude > 0.0001f)
            {
                // 放射散布：方向从原点指向偏移后的位置
                // 混合原方向和偏移方向，保持大致的发射趋势
                var offsetDir = offset.normalized;
                spawn.Direction = (spawn.Direction + offsetDir * 0.5f).normalized;
            }

            if (DirectionJitter > 0f)
            {
                // 独立方向抖动：在锥体内随机旋转
                float angle = Mode == DistributionMode.Uniform
                    ? rng.Range(-DirectionJitter, DirectionJitter)
                    : SampleNormalScalar(rng) * DirectionJitter;
                float azimuth = rng.Range(0f, 360f);

                var rotation = Quaternion.AngleAxis(angle, Vector3.up)
                             * Quaternion.AngleAxis(azimuth, spawn.Direction);
                spawn.Direction = (rotation * spawn.Direction).normalized;
            }

            // ── Speed ──
            if (SpeedJitter > 0.0001f)
            {
                float delta = Mode == DistributionMode.Uniform
                    ? rng.Range(-SpeedJitter, SpeedJitter)
                    : SampleNormalScalar(rng) * SpeedJitter;
                spawn.Speed = Mathf.Max(0f, spawn.Speed + delta);
            }
        }

        // ── 分布采样 ──

        private static Vector3 SampleUniform(DeterministicRng rng, Vector3 range)
        {
            return new Vector3(
                range.x > 0.0001f ? rng.Range(-range.x, range.x) : 0f,
                range.y > 0.0001f ? rng.Range(-range.y, range.y) : 0f,
                range.z > 0.0001f ? rng.Range(-range.z, range.z) : 0f);
        }

        private static Vector3 SampleNormal(DeterministicRng rng, Vector3 stddev)
        {
            return new Vector3(
                stddev.x > 0.0001f ? SampleNormalScalar(rng) * stddev.x : 0f,
                stddev.y > 0.0001f ? SampleNormalScalar(rng) * stddev.y : 0f,
                stddev.z > 0.0001f ? SampleNormalScalar(rng) * stddev.z : 0f);
        }

        /// <summary>Box-Muller 正态分布采样，返回标准正态值。</summary>
        private static float SampleNormalScalar(DeterministicRng rng)
        {
            float u1 = Mathf.Max(0.0001f, rng.NextFloat()); // 避免 log(0)
            float u2 = rng.NextFloat();
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
