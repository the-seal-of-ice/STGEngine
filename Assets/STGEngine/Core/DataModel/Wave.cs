using System;
using System.Collections.Generic;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// A wave of enemies. Contains enemy instances with individual paths.
    /// Stored as independent YAML files for cross-stage reuse.
    /// </summary>
    public class Wave
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Name { get; set; } = "";

        /// <summary>Enemy instances in this wave.</summary>
        public List<EnemyInstance> Enemies { get; set; } = new();

        /// <summary>Total wave duration for timeline display (seconds).</summary>
        public float Duration { get; set; } = 10f;
    }

    /// <summary>
    /// A single enemy instance within a wave.
    /// References an EnemyType template and defines its own movement path.
    /// </summary>
    public class EnemyInstance
    {
        /// <summary>Referenced EnemyType template ID.</summary>
        public string EnemyTypeId { get; set; } = "";

        /// <summary>Spawn delay relative to wave start (seconds).</summary>
        public float SpawnDelay { get; set; }

        /// <summary>Movement path keyframes (time relative to this enemy's spawn).</summary>
        public List<PathKeyframe> Path { get; set; } = new();
    }
}
