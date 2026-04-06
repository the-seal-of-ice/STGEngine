// Assets/STGEngine/Runtime/Scene/ScrollController.cs
using UnityEngine;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 控制场景的卷轴流动。场景整体作为一个 Transform 的子物体，
    /// 通过移动和旋转该 Transform 实现"沿通路前进"的效果。
    /// Drift 补偿让视角始终朝向通路方向，Perlin noise 增加生动感。
    /// </summary>
    public class ScrollController
    {
        /// <summary>当前累计滚动距离（米）。</summary>
        public float TotalScrolled { get; private set; }

        /// <summary>当前帧的流动速度（m/s）。</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>速度倍率覆盖。1.0 = 正常，0.0 = 停止。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>随机横向偏移的幅度（米）。0 = 无偏移。</summary>
        public float WanderAmplitude { get; set; } = 1.5f;

        /// <summary>随机横向偏移的变化频率。</summary>
        public float WanderFrequency { get; set; } = 0.08f;

        /// <summary>当前通路朝向角度（Y 轴旋转，度）。</summary>
        public float CurrentHeading { get; private set; }

        private readonly List<Chunk> _activeChunks;
        private Core.Scene.PathProfile _profile;
        private float _prevDrift;
        private float _wanderSeed;
        private float _smoothedLateralOffset;

        /// <summary>场景根 Transform，所有 Chunk 是它的子物体。</summary>
        private Transform _sceneRoot;

        public ScrollController(List<Chunk> activeChunks)
        {
            _activeChunks = activeChunks;
            _wanderSeed = Random.Range(0f, 1000f);
        }

        /// <summary>设置场景根 Transform（ChunkGenerator 的 transform）。</summary>
        public void SetSceneRoot(Transform root)
        {
            _sceneRoot = root;
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
        /// 每帧调用。沿通路方向移动场景，补偿 Drift 变化，
        /// 旋转场景使视角朝向通路前进方向。
        /// </summary>
        public float Tick(float deltaTime)
        {
            if (_profile == null) return 0f;

            var sample = _profile.SampleAt(TotalScrolled);
            CurrentSpeed = sample.Speed * SpeedMultiplier;
            float scrollDelta = CurrentSpeed * deltaTime;

            // --- Drift 补偿 ---
            // Drift 变化率 = 通路弯曲方向
            float currentDrift = sample.Drift;
            float driftDelta = currentDrift - _prevDrift;
            _prevDrift = currentDrift;

            // --- 通路朝向角度 ---
            // 用 Drift 的变化率计算通路的朝向：atan2(driftDelta, scrollDelta)
            // 这给出通路在 XZ 平面上的前进方向
            if (scrollDelta > 0.001f)
            {
                float targetHeading = Mathf.Atan2(driftDelta, scrollDelta) * Mathf.Rad2Deg;
                // 平滑插值避免突变
                CurrentHeading = Mathf.LerpAngle(CurrentHeading, targetHeading, Mathf.Min(deltaTime * 3f, 1f));
            }

            // --- 随机漫游 ---
            float wanderTarget = (Mathf.PerlinNoise(_wanderSeed, TotalScrolled * WanderFrequency) - 0.5f)
                                 * 2f * WanderAmplitude;
            _smoothedLateralOffset = Mathf.Lerp(_smoothedLateralOffset, wanderTarget, Mathf.Min(deltaTime * 1.5f, 1f));

            // --- 移动所有 Chunk ---
            // 主轴移动（-Z）+ Drift 补偿（-X）+ 漫游偏移
            float lateralDelta = driftDelta;
            for (int i = 0; i < _activeChunks.Count; i++)
            {
                var chunk = _activeChunks[i];
                if (chunk.IsActive && chunk.Root != null)
                {
                    var pos = chunk.Root.transform.position;
                    pos.z -= scrollDelta;
                    pos.x -= lateralDelta;
                    chunk.Root.transform.position = pos;
                }
            }

            // --- 旋转场景根 ---
            // 旋转场景根而非每个 Chunk，让所有 Chunk 一起转
            // 加上漫游偏移的位移
            if (_sceneRoot != null)
            {
                // 旋转：让前方道路始终朝向摄像头前方
                _sceneRoot.rotation = Quaternion.Euler(0f, -CurrentHeading, 0f);

                // 漫游偏移：在场景根的局部 X 方向上偏移
                var rootPos = _sceneRoot.position;
                float wanderDelta = (_smoothedLateralOffset - rootPos.x);
                rootPos.x = -_smoothedLateralOffset;
                _sceneRoot.position = rootPos;
            }

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
            CurrentHeading = 0f;
            _smoothedLateralOffset = 0f;
            if (_sceneRoot != null)
            {
                _sceneRoot.position = Vector3.zero;
                _sceneRoot.rotation = Quaternion.identity;
            }
        }
    }
}
