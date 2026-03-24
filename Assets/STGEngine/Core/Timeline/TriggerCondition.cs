using System.Collections.Generic;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Trigger types for segment transitions.
    /// </summary>
    public enum TriggerType
    {
        /// <summary>Start immediately after previous segment ends.</summary>
        Immediate,
        /// <summary>Start after N seconds from previous segment start.</summary>
        TimeElapsed,
        /// <summary>Start when all enemies in previous segment are defeated.</summary>
        AllEnemiesDefeated,
        /// <summary>Start when boss is defeated.</summary>
        BossDefeated,
        /// <summary>Custom scripted condition.</summary>
        Custom
    }

    /// <summary>
    /// Condition that triggers a segment transition.
    /// First segment in a stage has null trigger (starts immediately).
    /// </summary>
    public class TriggerCondition
    {
        public TriggerType Type { get; set; } = TriggerType.Immediate;

        /// <summary>
        /// Type-specific parameters.
        /// TimeElapsed: "delay" (float seconds).
        /// Custom: user-defined key-value pairs.
        /// </summary>
        public Dictionary<string, object> Params { get; set; } = new();
    }
}
