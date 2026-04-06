using UnityEngine;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 控制场景的卷轴流动。管理弧长推进和速度。
    /// 样条线方案下不需要移动/旋转场景——Chunk 几何体本身就沿曲线生成。
    /// 每帧推进弧长，ChunkGenerator 根据弧长决定回收和生成。
    /// </summary>
    public class ScrollController
    {
        /// <summary>当前累计滚动弧长距离（米）。即玩家在样条线上的位置。</summary>
        public float TotalScrolled { get; private set; }

        /// <summary>当前帧的流动速度（m/s）。</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>速度倍率覆盖。1.0 = 正常，0.0 = 停止。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        private Core.Scene.PathProfile _profile;

        /// <summary>设置当前使用的 PathProfile。</summary>
        public void SetProfile(Core.Scene.PathProfile profile)
        {
            _profile = profile;
        }

        /// <summary>
        /// 每帧调用。根据当前弧长从 PathProfile 采样速度，推进弧长。
        /// 返回本帧推进的弧长距离。
        /// </summary>
        public float Tick(float deltaTime)
        {
            if (_profile == null) return 0f;

            var sample = _profile.SampleAt(TotalScrolled);
            CurrentSpeed = sample.Speed * SpeedMultiplier;
            float scrollDelta = CurrentSpeed * deltaTime;

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
