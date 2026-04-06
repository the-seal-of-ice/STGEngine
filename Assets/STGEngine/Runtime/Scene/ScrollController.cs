// Assets/STGEngine/Runtime/Scene/ScrollController.cs
using UnityEngine;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 控制场景的卷轴流动。每帧将所有活跃 Chunk 沿 -Z 方向移动，
    /// 速度由当前 PathProfile.ScrollSpeed 决定。
    /// 玩家始终在原点附近，场景向玩家流动。
    /// </summary>
    public class ScrollController
    {
        /// <summary>当前累计滚动距离（米）。</summary>
        public float TotalScrolled { get; private set; }

        /// <summary>当前帧的流动速度（m/s）。</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>速度倍率覆盖。1.0 = 正常，0.0 = 停止。用于对话减速等。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        private readonly List<Chunk> _activeChunks;
        private Core.Scene.PathProfile _profile;

        public ScrollController(List<Chunk> activeChunks)
        {
            _activeChunks = activeChunks;
        }

        /// <summary>设置当前使用的 PathProfile。</summary>
        public void SetProfile(Core.Scene.PathProfile profile)
        {
            _profile = profile;
        }

        /// <summary>
        /// 每帧调用。根据当前滚动距离从 PathProfile 采样速度，
        /// 移动所有活跃 Chunk。
        /// </summary>
        public float Tick(float deltaTime)
        {
            if (_profile == null) return 0f;

            var sample = _profile.SampleAt(TotalScrolled);
            CurrentSpeed = sample.Speed * SpeedMultiplier;
            float scrollDelta = CurrentSpeed * deltaTime;

            for (int i = 0; i < _activeChunks.Count; i++)
            {
                var chunk = _activeChunks[i];
                if (chunk.IsActive && chunk.Root != null)
                {
                    var pos = chunk.Root.transform.position;
                    pos.z -= scrollDelta;
                    chunk.Root.transform.position = pos;
                }
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
        }
    }
}
