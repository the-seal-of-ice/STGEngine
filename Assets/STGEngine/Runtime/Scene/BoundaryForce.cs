using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 边界力场。两层机制：
    /// 1. 障碍物推力：每个路侧障碍物都是推力源，玩家靠近时被推开。
    ///    边界形状由障碍物的实际分布决定，而非平滑曲线。
    /// 2. 安全兜底：通路宽度硬限制，防止没有障碍物的区域飞出去。
    /// 3. 地面约束：Y 方向软边界。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/BoundaryForce")]
    public class BoundaryForce : MonoBehaviour
    {
        [Header("Obstacle Repulsion")]
        [SerializeField, Tooltip("障碍物推力开始生效的距离（米，从障碍物表面算起）")]
        private float _repulsionRange = 2f;

        [SerializeField, Tooltip("障碍物最大推力强度（m/s）")]
        private float _repulsionForce = 18f;

        [SerializeField, Tooltip("推力指数（越大越接近硬墙）")]
        private float _repulsionExponent = 3f;

        [Header("Safety Fallback")]
        [SerializeField, Tooltip("安全硬限制倍率（相对于通路半宽）")]
        private float _safetyLimitRatio = 1.3f;

        [Header("Vertical Boundary")]
        [SerializeField, Tooltip("地面推力强度")]
        private float _groundForce = 200f;

        [SerializeField, Tooltip("上方自由区高度（米）")]
        private float _ceilingHeight = 15f;

        [SerializeField, Tooltip("上方推力强度")]
        private float _ceilingForce = 20f;

        private PlayerAnchorController _player;
        private ChunkGenerator _generator;
        private bool _initialized;

        public void Initialize(PlayerAnchorController player, ChunkGenerator generator)
        {
            _player = player;
            _generator = generator;
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized || _player == null || _generator == null) return;

            Vector2 force = Vector2.zero;
            Vector3 playerPos = _player.WorldPosition;

            // --- 障碍物推力 ---
            // 每个路侧障碍物都是推力源，玩家靠近时被推开
            foreach (var chunk in _generator.ActiveChunks)
            {
                if (!chunk.IsActive) continue;

                foreach (var obs in chunk.Obstacles)
                {
                    if (obs.GameObject == null || !obs.GameObject.activeSelf) continue;
                    if (obs.Config == null) continue;
                    // 只有路侧障碍物产生边界推力（道路内的危险障碍物不推）
                    if (obs.Config.PlacementZone != PlacementZone.Roadside) continue;

                    Vector3 obsPos = obs.GameObject.transform.position;

                    // XZ 平面距离
                    float dx = playerPos.x - obsPos.x;
                    float dz = playerPos.z - obsPos.z;
                    float horizDist = Mathf.Sqrt(dx * dx + dz * dz);

                    // 障碍物 XZ 半径
                    float obsRadius = 1f;
                    var renderer = obs.GameObject.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        var ext = renderer.bounds.extents;
                        obsRadius = Mathf.Min(ext.x, ext.z);
                    }

                    // 从障碍物表面算起的距离
                    float surfaceDist = horizDist - obsRadius - 0.8f; // 减去玩家半径

                    if (surfaceDist >= _repulsionRange) continue;
                    if (surfaceDist < 0f) surfaceDist = 0f;

                    // 归一化深度：0 = 刚进入范围，1 = 贴着表面
                    float depth = 1f - (surfaceDist / _repulsionRange);
                    float strength = Mathf.Pow(depth, _repulsionExponent) * _repulsionForce;

                    // 推力方向：从障碍物指向玩家（XZ 平面）
                    if (horizDist > 0.01f)
                    {
                        Vector3 pushDir = new Vector3(dx / horizDist, 0f, dz / horizDist);
                        // 投影到玩家的局部坐标系（Normal 方向）
                        float lateralPush = Vector3.Dot(pushDir, _player.CurrentAnchor.Normal);
                        force.x += lateralPush * strength;
                    }
                }
            }

            // --- 地面约束 ---
            Vector2 offset = _player.LocalOffset;
            float groundLevel = 0.8f;
            if (offset.y < groundLevel)
            {
                force.y = (groundLevel - offset.y) * _groundForce;
            }
            else if (offset.y > _ceilingHeight)
            {
                float depth = (offset.y - _ceilingHeight) / 5f;
                force.y = -Mathf.Min(depth, 1f) * _ceilingForce;
            }

            // 应用推力
            if (force.sqrMagnitude > 0f)
            {
                _player.LocalOffset += force * Time.deltaTime;
            }

            // 安全硬限制（兜底，正常情况下障碍物推力已经阻止了）
            var anchor = _player.CurrentAnchor;
            float safeLimit = anchor.Width * 0.5f * _safetyLimitRatio;
            Vector2 clamped = _player.LocalOffset;
            clamped.x = Mathf.Clamp(clamped.x, -safeLimit, safeLimit);
            clamped.y = Mathf.Clamp(clamped.y, groundLevel - 0.1f, _ceilingHeight + 5f);
            _player.LocalOffset = clamped;
        }
    }
}
