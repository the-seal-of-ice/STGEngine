using System;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Bitmask filter for difficulty levels.
    /// Used by TimelineSegment to control which segments appear at which difficulty.
    /// </summary>
    [Flags]
    public enum DifficultyFilter
    {
        Easy    = 1,
        Normal  = 2,
        Hard    = 4,
        Lunatic = 8,
        All     = Easy | Normal | Hard | Lunatic
    }
}
