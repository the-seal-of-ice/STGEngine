using System.Collections.Generic;
using UnityEngine;
using STGEngine.Runtime.Bullet;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 碰撞检测结果。每个逻辑 tick 产生一个。
    /// </summary>
    public struct CollisionResult
    {
        /// <summary>是否被击中（判定点与子弹重叠）。</summary>
        public bool Hit;
        /// <summary>本 tick 擦弹数。</summary>
        public int GrazeCount;
    }

    /// <summary>
    /// 纯静态碰撞检测。球体 vs 球体，遍历所有活跃子弹。
    /// 设计为无状态，方便单元测试和确定性重放。
    /// 
    /// 后续扩展点：
    /// - 空间分区（Grid / Octree）加速大量子弹场景
    /// - Capsule / Box 碰撞形状
    /// - 连续碰撞检测（CCD）防止高速穿透
    /// </summary>
    public static class CollisionSystem
    {
        /// <summary>
        /// 检测玩家与所有子弹的碰撞。
        /// </summary>
        /// <param name="playerPos">玩家判定点位置</param>
        /// <param name="hitboxRadius">被弹判定半径</param>
        /// <param name="grazeRadius">擦弹判定半径</param>
        /// <param name="bullets">当前帧所有活跃子弹状态</param>
        /// <param name="bulletRadius">子弹碰撞半径（来自 BulletPattern.CollisionShape）</param>
        /// <param name="isInvincible">玩家是否处于无敌状态（无敌时不判定 Hit，但仍计算 Graze）</param>
        /// <returns>碰撞结果</returns>
        public static CollisionResult Check(
            Vector3 playerPos,
            float hitboxRadius,
            float grazeRadius,
            IReadOnlyList<BulletState> bullets,
            float bulletRadius,
            bool isInvincible = false)
        {
            var result = new CollisionResult();
            if (bullets == null || bullets.Count == 0) return result;

            float hitDistSq = (hitboxRadius + bulletRadius) * (hitboxRadius + bulletRadius);
            float grazeDistSq = (grazeRadius + bulletRadius) * (grazeRadius + bulletRadius);

            for (int i = 0; i < bullets.Count; i++)
            {
                float distSq = (bullets[i].Position - playerPos).sqrMagnitude;

                // 被弹判定（优先于擦弹）
                if (!isInvincible && distSq <= hitDistSq)
                {
                    result.Hit = true;
                    // 被弹后本帧不再累计擦弹
                    return result;
                }

                // 擦弹判定
                if (distSq <= grazeDistSq)
                {
                    result.GrazeCount++;
                }
            }

            return result;
        }
    }
}
