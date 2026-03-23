using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Base class for all timeline events within a segment.
    /// Subclasses use [TypeTag] for polymorphic YAML serialization.
    /// </summary>
    public abstract class TimelineEvent
    {
        public string Id { get; set; } = "";

        /// <summary>Start time relative to the segment's beginning (seconds).</summary>
        public float StartTime { get; set; }

        /// <summary>End time shortcut.</summary>
        public float EndTime => StartTime + Duration;

        /// <summary>Duration of this event (seconds).</summary>
        public abstract float Duration { get; set; }
    }

    /// <summary>
    /// Spawns a bullet pattern at a given position for a duration.
    /// Pattern is referenced by ID and resolved at load time.
    /// </summary>
    [TypeTag("spawn_pattern")]
    public class SpawnPatternEvent : TimelineEvent
    {
        public override float Duration { get; set; } = 5f;

        /// <summary>Reference to a BulletPattern file by its ID.</summary>
        public string PatternId { get; set; } = "";

        /// <summary>World-space position of the emitter.</summary>
        public Vector3 SpawnPosition { get; set; } = new Vector3(0, 5, 0);

        /// <summary>
        /// Resolved at runtime after loading. Not serialized.
        /// </summary>
        [YamlDotNet.Serialization.YamlIgnore]
        public BulletPattern ResolvedPattern { get; set; }
    }
}
