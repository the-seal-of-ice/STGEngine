namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Marker interface for action-specific parameter classes.
    /// Each ActionType maps to a concrete IActionParams implementation
    /// via <see cref="ActionParamsRegistry"/>.
    /// </summary>
    public interface IActionParams { }
}
