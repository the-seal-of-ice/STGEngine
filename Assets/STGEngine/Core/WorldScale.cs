namespace STGEngine.Core
{
    /// <summary>
    /// 世界尺寸标准。1 unit = 1 meter。
    /// 所有值为初始参考，可通过 PlayerProfile 覆盖。
    /// </summary>
    public static class WorldScale
    {
        public const float PlayerVisualDiameter = 1.6f;
        public const float PlayerHitboxRadius   = 0.08f;
        public const float PlayerGrazeRadius    = 0.5f;

        public const float BulletSmallRadius    = 0.15f;
        public const float BulletNormalRadius   = 0.4f;
        public const float BulletLargeRadius    = 1.2f;

        public const float BossVisualScale      = 5.0f;
        public const float EnemyVisualScale     = 2.0f;

        public const float ItemSmallRadius      = 0.3f;
        public const float ItemLargeRadius      = 0.5f;

        public const float DefaultBoundaryHalf  = 40f;

        public const float PlayerMoveSpeed      = 14f;
        public const float PlayerSlowMultiplier = 0.33f;
    }
}
