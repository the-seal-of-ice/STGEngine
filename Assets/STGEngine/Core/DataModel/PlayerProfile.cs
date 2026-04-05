using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.DataModel
{
    public enum AutoCollectTrigger
    {
        Manual,
        HighPower
    }

    [TypeTag("power_tier")]
    public class PowerTier
    {
        /// <summary>Power 阈值（达到此值进入该段位）。</summary>
        public float Threshold { get; set; }

        /// <summary>该段位下浮游炮数量。</summary>
        public int OptionCount { get; set; }
    }

    /// <summary>
    /// 自机完整数据模型。所有字段使用 WorldScale 常量作为默认值。
    /// </summary>
    [TypeTag("player_profile")]
    public class PlayerProfile
    {
        public string Id { get; set; } = "default";
        public string Name { get; set; } = "Default";

        // ── 移动 ──
        public float MoveSpeed { get; set; } = WorldScale.PlayerMoveSpeed;
        public float SlowMultiplier { get; set; } = WorldScale.PlayerSlowMultiplier;

        // ── 判定 ──
        public float HitboxRadius { get; set; } = WorldScale.PlayerHitboxRadius;
        public float GrazeRadius { get; set; } = WorldScale.PlayerGrazeRadius;
        public float VisualScale { get; set; } = WorldScale.PlayerVisualDiameter;

        // ── 生存 ──
        public int InitialLives { get; set; } = 3;
        public int InitialBombs { get; set; } = 3;
        public float InvincibleDuration { get; set; } = 2f;
        public float RespawnInvincibleDuration { get; set; } = 3f;

        // ── Power ──
        public float MaxPower { get; set; } = 4.0f;
        public float InitialPower { get; set; } = 1.0f;
        public float PowerPerSmallItem { get; set; } = 0.05f;
        public float PowerPerLargeItem { get; set; } = 1.0f;
        public float DeathPowerLoss { get; set; } = 0.5f;

        // ── Power 段位 → 浮游炮数量 ──
        public List<PowerTier> PowerTiers { get; set; } = new()
        {
            new PowerTier { Threshold = 0f, OptionCount = 2 },
            new PowerTier { Threshold = 1f, OptionCount = 2 },
            new PowerTier { Threshold = 2f, OptionCount = 4 },
            new PowerTier { Threshold = 3f, OptionCount = 4 },
            new PowerTier { Threshold = 4f, OptionCount = 6 },
        };

        /// <summary>
        /// 各浮游炮配置对应的偏移列表。
        /// 索引 0 = 2 个炮的偏移，索引 1 = 4 个炮，索引 2 = 6 个炮。
        /// </summary>
        public List<List<Vector3>> OptionOffsetsByTier { get; set; } = new()
        {
            // 2 个浮游炮
            new List<Vector3>
            {
                new Vector3(-1.5f, 0f, -0.5f),
                new Vector3( 1.5f, 0f, -0.5f),
            },
            // 4 个浮游炮
            new List<Vector3>
            {
                new Vector3(-1.5f, 0f, -0.5f),
                new Vector3( 1.5f, 0f, -0.5f),
                new Vector3(-2.5f, 0f, -1.2f),
                new Vector3( 2.5f, 0f, -1.2f),
            },
            // 6 个浮游炮
            new List<Vector3>
            {
                new Vector3(-1.5f, 0f, -0.5f),
                new Vector3( 1.5f, 0f, -0.5f),
                new Vector3(-2.5f, 0f, -1.2f),
                new Vector3( 2.5f, 0f, -1.2f),
                new Vector3(-3.5f, 0f, -2.0f),
                new Vector3( 3.5f, 0f, -2.0f),
            },
        };

        // ── 射击（普通模式）──
        public float ShotInterval { get; set; } = 0.5f;
        public float ShotSpeed { get; set; } = 140f;
        public float ShotDamage { get; set; } = 10f;
        public float ShotRadius { get; set; } = 0.15f;
        public int ShotsPerOption { get; set; } = 2;
        public float ShotConeAngle { get; set; } = 12f;
        public float ShotHomingStrength { get; set; } = 2f;

        // ── 射击（低速模式）──
        public float FocusShotInterval { get; set; } = 0.35f;
        public float FocusShotSpeed { get; set; } = 200f;
        public float FocusShotDamage { get; set; } = 15f;
        public int FocusShotsPerOption { get; set; } = 1;
        public float FocusShotConeAngle { get; set; } = 3f;
        public float FocusShotHomingStrength { get; set; } = 1f;

        // ── Bomb ──
        public float BombDuration { get; set; } = 3f;
        public float BombInvincibleDuration { get; set; } = 5f;
        public float BombClearRadius { get; set; } = 30f;

        // ── 道具拾取 ──
        public float ItemCollectRadius { get; set; } = 1.5f;
        public AutoCollectTrigger AutoCollectMode { get; set; } = AutoCollectTrigger.HighPower;
        public int LifeFragmentsPerLife { get; set; } = 3;
        public int BombFragmentsPerBomb { get; set; } = 3;
        public int BasePointItemValue { get; set; } = 10000;

        /// <summary>返回使用所有默认值的东方风实例。</summary>
        public static PlayerProfile TouhouDefault => new()
        {
            Id = "touhou_default",
            Name = "东方系默认",
        };
    }
}
