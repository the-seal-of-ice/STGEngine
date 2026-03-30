namespace STGEngine.Core.Timeline
{
    public enum BranchCondition
    {
        DifficultyIs,
        BossHealthBelow,
        PlayerLivesBelow,
        Always
    }

    public class BranchJumpParams : IActionParams
    {
        public BranchCondition Condition { get; set; } = BranchCondition.Always;
        public float ConditionValue { get; set; } = 0f;
        /// <summary>Segment ID to jump to when condition is met.</summary>
        public string TargetSegmentId { get; set; } = "";
        /// <summary>Segment ID when condition is NOT met. Empty = continue normally.</summary>
        public string FallbackSegmentId { get; set; } = "";
    }
}
