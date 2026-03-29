using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家运行时状态。纯数据容器，不含逻辑。
    /// 由 PlayerController 驱动更新，CollisionSystem 读写。
    /// </summary>
    public class PlayerState
    {
        public Vector3 Position;

        // ── 生存 ──
        public int Lives = 3;
        public int Bombs = 3;
        public bool IsInvincible;
        public float InvincibleTimer;

        /// <summary>无敌持续时间（秒）。被弹后进入无敌。</summary>
        public float InvincibleDuration = 2f;

        // ── 擦弹 ──
        public int GrazeTotal;
        /// <summary>本帧擦弹数（每帧重置）。</summary>
        public int GrazeThisFrame;

        // ── 输入状态 ──
        public bool IsSlow;

        // ── 判定 ──
        /// <summary>被弹判定半径。</summary>
        public float HitboxRadius = 0.15f;
        /// <summary>擦弹判定半径。</summary>
        public float GrazeRadius = 0.8f;

        /// <summary>更新无敌计时器。在逻辑 tick 中调用。</summary>
        public void TickInvincibility(float dt)
        {
            if (!IsInvincible) return;
            InvincibleTimer -= dt;
            if (InvincibleTimer <= 0f)
            {
                IsInvincible = false;
                InvincibleTimer = 0f;
            }
        }

        /// <summary>触发被弹。</summary>
        public void OnHit()
        {
            if (IsInvincible) return;
            Lives--;
            IsInvincible = true;
            InvincibleTimer = InvincibleDuration;
        }
    }
}
