// Assets/STGEngine/Runtime/Scene/ScrollController.cs
using UnityEngine;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 控制场景的卷轴流动。每帧将所有活跃 Chunk 沿通路方向移动，
    /// 补偿 DriftCurve 的横向变化，使玩家视角始终"沿路前进"。
    /// 叠加缓慢的随机横向偏移增加生动感。
    /// </summary>
    public class ScrollController
    {
        /// <summary>当前累计滚动距离（米）。</summary>
        public float TotalScrolled { get; private set; }

        /// <summary>当前帧的流动速度（m/s）。</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>速度倍率覆盖。1.0 = 正常，0.0 = 停止。用于对话减速等。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>随机横向偏移的幅度（米）。0 = 无偏移。</summary>
        public float WanderAmplitude { get; set; } = 2f;

        /// <summary>随机横向偏移的变化频率。越大变化越快。</summary>
        public float WanderFrequency { get; set; } = 0.15f;

        /// <summary>当前的横向偏移量（Drift 补偿 + 随机漫游的合计）。</summary>
        public float CurrentLateralOffset { get; private set; }

        private readonly List<Chunk> _activeChunks;
        private Core.Scene.PathProfile _profile;
        private float _prevDrift;
        private float _accumulatedDriftCompensation;
        private float _wanderSeed;

        public ScrollController(List<Chunk> activeChunks)
        {
            _activeChunks = activeChunks;
            _wanderSeed = Random.Range(0f, 1000f);
        }

        /// <summary>设置当前使用的 PathProfile。</summary>
        public void SetProfile(Core.Scene.PathProfile profile)
        {
            _profile = profile;
            if (profile != null)
            {
                _prevDrift = profile.SampleAt(0f).Drift;
            }
        }

        /// <summary>
        /// 每帧调用。根据当前滚动距离从 PathProfile 采样速度，
        /// 沿通路方向移动所有活跃 Chunk（Z 方向 + Drift 补偿 X 方向）。
        /// </summary>
        public float Tick(float deltaTime)
        {
            if (_profile == null) return 0f;

            var sample = _profile.SampleAt(TotalScrolled);
            CurrentSpeed = sample.Speed * SpeedMultiplier;
            float scrollDelta = CurrentSpeed * deltaTime;

            // 计算 Drift 变化量：当前 Drift 与上一帧 Drift 的差值
            // 场景需要反向补偿这个差值，让玩家视角始终沿路前进
            float driftDelta = sample.Drift - _prevDrift;
            _accumulatedDriftCompensation += driftDelta;
            _prevDrift = sample.Drift;

            // 随机漫游偏移（Perlin noise 驱动，缓慢平滑变化）
            float wanderOffset = (Mathf.PerlinNoise(_wanderSeed, TotalScrolled * WanderFrequency) - 0.5f) * 2f * WanderAmplitude;

            // 总横向偏移 = Drift 补偿 + 随机漫游
            float totalLateralDelta = driftDelta + (wanderOffset - CurrentLateralOffset) * Mathf.Min(deltaTime * 2f, 1f);

            // 移动所有活跃 Chunk
            for (int i = 0; i < _activeChunks.Count; i++)
            {
                var chunk = _activeChunks[i];
                if (chunk.IsActive && chunk.Root != null)
                {
                    var pos = chunk.Root.transform.position;
                    pos.z -= scrollDelta;
                    pos.x -= totalLateralDelta;
                    chunk.Root.transform.position = pos;
                }
            }

            CurrentLateralOffset = wanderOffset;
            TotalScrolled += scrollDelta;
            return scrollDelta;
        }

        /// <summary>重置滚动状态。</summary>
        public void Reset()
        {
            TotalScrolled = 0f;
            CurrentSpeed = 0f;
            SpeedMultiplier = 1f;
            _prevDrift = 0f;
            _accumulatedDriftCompensation = 0f;
            CurrentLateralOffset = 0f;
        }
    }
}
