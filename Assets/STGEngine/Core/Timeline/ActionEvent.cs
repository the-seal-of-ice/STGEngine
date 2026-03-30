using STGEngine.Core.Serialization;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// A scripted action event on the timeline (title display, bullet clear,
    /// BGM switch, score tally, wait condition, etc.).
    /// Uses <see cref="ActionType"/> to identify the function and
    /// <see cref="IActionParams"/> for type-specific parameters.
    /// </summary>
    [TypeTag("action")]
    public class ActionEvent : TimelineEvent
    {
        /// <summary>Duration of this action. 0 = instant (point event / marker).</summary>
        public override float Duration { get; set; } = 0f;

        /// <summary>What kind of action this is.</summary>
        public ActionType ActionType { get; set; }

        /// <summary>Type-specific parameters. Concrete type determined by ActionType.</summary>
        public IActionParams Params { get; set; }

        /// <summary>
        /// Whether this action freezes timeline progression until completed.
        /// When true, CurrentTime stops advancing at this event's StartTime.
        /// Default depends on ActionType (ScoreTally/WaitCondition default true).
        /// </summary>
        public bool Blocking { get; set; } = false;

        /// <summary>
        /// Maximum wait time before auto-resuming (seconds). 0 = no timeout (infinite wait).
        /// Only meaningful when Blocking = true.
        /// Editor display: Timeout > 0 → block with width=Timeout; Timeout = 0 → marker (zero-width).
        /// </summary>
        public float Timeout { get; set; } = 0f;
    }
}
