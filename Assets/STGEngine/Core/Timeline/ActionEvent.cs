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
        /// <summary>
        /// Duration of this action (seconds).
        /// - Blocking=false: visual effect duration (block width on timeline).
        /// - Blocking=true, Duration>0: timeout upper bound (auto-resume after this time).
        /// - Blocking=true, Duration=0: infinite wait (zero-width marker).
        /// </summary>
        public override float Duration { get; set; } = 0f;

        /// <summary>What kind of action this is.</summary>
        public ActionType ActionType { get; set; }

        /// <summary>Type-specific parameters. Concrete type determined by ActionType.</summary>
        public IActionParams Params { get; set; }

        /// <summary>
        /// Whether this action freezes timeline progression until completed.
        /// When true, CurrentTime stops advancing at this event's StartTime.
        /// Duration controls the timeout (0 = infinite wait).
        /// </summary>
        public bool Blocking { get; set; } = false;
    }
}
