using UnityEngine;
using STGEngine.Core.Random;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// AI 随机游走决策引擎。纯 C# 类，无 MonoBehaviour 依赖。
    /// 
    /// 输入：当前位置、场景边界。
    /// 输出：归一化方向向量（与 PlayerController._inputDirection 同语义）。
    /// 
    /// 所有行为由 DeterministicRng 驱动，种子+参数即可完美重放。
    /// </summary>
    public class RandomWalkBrain
    {
        // ── 可序列化参数（种子+这些参数 = 完整重放） ──

        /// <summary>确定性种子。相同种子+参数 = 相同轨迹。</summary>
        public int Seed { get; set; }

        /// <summary>随机改变方向的平均间隔（秒）。越小越频繁变向。</summary>
        public float WanderInterval { get; set; } = 1.5f;

        /// <summary>减速趋向 [0,1]。0=始终全速，1=频繁减速/停顿。</summary>
        public float SlowdownTendency { get; set; } = 0.3f;

        /// <summary>远离边界的趋势强度 [0,1]。0=无回避，1=强力回避。</summary>
        public float BoundaryAvoidance { get; set; } = 0.6f;

        /// <summary>移动速度倍率。1=正常速度。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        // ── 运行时状态 ──

        private DeterministicRng _rng;
        private Vector3 _currentDirection;
        private float _directionTimer;
        private float _speedFactor = 1f; // 当前速度因子 [0,1]
        private float _slowdownTimer;
        private bool _initialized;

        /// <summary>当前 AI 决策的移动方向（归一化）。</summary>
        public Vector3 CurrentDirection => _currentDirection * _speedFactor;

        /// <summary>初始化或重置 Brain。用相同种子调用可重放。</summary>
        public void Initialize()
        {
            _rng = new DeterministicRng(Seed);
            _currentDirection = RandomDirection();
            _directionTimer = NextWanderInterval();
            _speedFactor = 1f;
            _slowdownTimer = 0f;
            _initialized = true;
        }

        /// <summary>
        /// 每逻辑 tick 调用。返回归一化方向向量（可能为零向量表示停顿）。
        /// </summary>
        /// <param name="currentPos">当前位置</param>
        /// <param name="boundaryMin">场景边界最小角</param>
        /// <param name="boundaryMax">场景边界最大角</param>
        /// <param name="dt">时间步长</param>
        public Vector3 Tick(Vector3 currentPos, Vector3 boundaryMin, Vector3 boundaryMax, float dt)
        {
            if (!_initialized) Initialize();

            // ── 方向切换 ──
            _directionTimer -= dt;
            if (_directionTimer <= 0f)
            {
                _currentDirection = RandomDirection();
                _directionTimer = NextWanderInterval();

                // 随机决定是否减速
                if (_rng.NextFloat() < SlowdownTendency)
                {
                    _speedFactor = _rng.Range(0.1f, 0.5f);
                    _slowdownTimer = _rng.Range(0.3f, 1.0f);
                }
            }

            // ── 减速恢复 ──
            if (_slowdownTimer > 0f)
            {
                _slowdownTimer -= dt;
                if (_slowdownTimer <= 0f)
                    _speedFactor = 1f;
            }

            // ── 边界回避 ──
            var avoidance = ComputeBoundaryAvoidance(currentPos, boundaryMin, boundaryMax);
            var finalDir = (_currentDirection * _speedFactor + avoidance).normalized;

            // 如果在减速中，保持低速
            float magnitude = _speedFactor * SpeedMultiplier;
            return finalDir * magnitude;
        }

        // ── 内部方法 ──

        private Vector3 RandomDirection()
        {
            // 随机 3D 方向（偏向水平面，Y 分量较小）
            float x = _rng.Range(-1f, 1f);
            float y = _rng.Range(-0.3f, 0.3f); // 垂直方向幅度较小
            float z = _rng.Range(-1f, 1f);
            var dir = new Vector3(x, y, z);
            return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
        }

        private float NextWanderInterval()
        {
            // 在 [0.5x, 1.5x] 范围内随机
            return WanderInterval * _rng.Range(0.5f, 1.5f);
        }

        private Vector3 ComputeBoundaryAvoidance(Vector3 pos, Vector3 min, Vector3 max)
        {
            if (BoundaryAvoidance <= 0.001f) return Vector3.zero;

            var center = (min + max) * 0.5f;
            var halfSize = (max - min) * 0.5f;
            var avoidance = Vector3.zero;

            // 每个轴：越靠近边界，回避力越强
            for (int axis = 0; axis < 3; axis++)
            {
                float distToMin = pos[axis] - min[axis];
                float distToMax = max[axis] - pos[axis];
                float threshold = halfSize[axis] * 0.3f; // 30% 边界区域开始回避

                if (distToMin < threshold)
                {
                    float strength = 1f - (distToMin / threshold);
                    avoidance[axis] += strength * BoundaryAvoidance;
                }
                if (distToMax < threshold)
                {
                    float strength = 1f - (distToMax / threshold);
                    avoidance[axis] -= strength * BoundaryAvoidance;
                }
            }

            return avoidance;
        }
    }
}
