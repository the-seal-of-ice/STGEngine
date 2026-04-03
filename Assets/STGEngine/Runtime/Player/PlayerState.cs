using UnityEngine;
using STGEngine.Core.DataModel;

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

        // ── Power（新增）──
        public float Power;
        public float MaxPower;

        // ── Score（新增）──
        public long Score;
        public int PointItemValue;

        // ── Bomb 状态（新增）──
        public bool IsBombing;
        public float BombTimer;
        public float BombDuration;
        public float BombInvincibleDuration;

        // ── 死亡/复活（新增）──
        public bool IsDead;
        public float RespawnTimer;
        public float RespawnInvincibleDuration;

        // ── 碎片（新增）──
        public int LifeFragments;
        public int BombFragments;

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
            if (IsInvincible || IsBombing) return;
            Lives--;
            IsInvincible = true;
            InvincibleTimer = InvincibleDuration;
            IsDead = Lives <= 0;
        }

        /// <summary>更新 Bomb 计时器。在逻辑 tick 中调用。</summary>
        public void TickBomb(float dt)
        {
            if (!IsBombing) return;
            BombTimer -= dt;
            if (BombTimer <= 0f)
            {
                IsBombing = false;
                BombTimer = 0f;
            }
        }

        /// <summary>从 PlayerProfile 创建初始状态。</summary>
        public static PlayerState FromProfile(PlayerProfile profile, Vector3 spawnPos)
        {
            return new PlayerState
            {
                Position = spawnPos,
                Lives = profile.InitialLives,
                Bombs = profile.InitialBombs,
                Power = profile.InitialPower,
                MaxPower = profile.MaxPower,
                PointItemValue = profile.BasePointItemValue,
                HitboxRadius = profile.HitboxRadius,
                GrazeRadius = profile.GrazeRadius,
                InvincibleDuration = profile.InvincibleDuration,
                BombDuration = profile.BombDuration,
                BombInvincibleDuration = profile.BombInvincibleDuration,
                RespawnInvincibleDuration = profile.RespawnInvincibleDuration,
            };
        }
    }
}
