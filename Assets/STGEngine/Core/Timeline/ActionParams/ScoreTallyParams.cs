namespace STGEngine.Core.Timeline
{
    public enum TallyType { SpellCardBonus, ChapterClear, StageClear }

    public class ScoreTallyParams : IActionParams
    {
        public TallyType Type { get; set; } = TallyType.ChapterClear;
        /// <summary>How long the tally screen is displayed (seconds).</summary>
        public float DisplayDuration { get; set; } = 3.0f;
        // Blocking behavior is controlled by ActionEvent.Blocking (default true)
        // and ActionEvent.Timeout (default = DisplayDuration).
    }
}
