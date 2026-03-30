namespace STGEngine.Core.Timeline
{
    public enum WaitConditionType
    {
        AllEnemiesDefeated,
        BossHealthBelow,
        TimeElapsed,
        PlayerConfirm
    }

    public class WaitConditionParams : IActionParams
    {
        public WaitConditionType Condition { get; set; } = WaitConditionType.AllEnemiesDefeated;
        /// <summary>Threshold for BossHealthBelow (percentage 0–1).</summary>
        public float TargetValue { get; set; } = 0f;
        // Timeout is controlled by ActionEvent.Timeout (default 30s).
    }
}
