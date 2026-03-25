using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Emitters;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Serialization;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// Bullet mesh shape types. Renderer creates corresponding mesh at runtime.
    /// </summary>
    public enum MeshType
    {
        Sphere,
        Diamond,
        Arrow,
        Rice
    }

    /// <summary>
    /// Collision shape types for bullet hitbox definition.
    /// </summary>
    public enum CollisionShapeType
    {
        Sphere,
        Capsule,
        Box
    }

    /// <summary>
    /// Collision shape data for a bullet pattern.
    /// Defines the hitbox used for collision detection (Phase 4+).
    /// Phase 2 only stores data and provides visualization.
    /// </summary>
    public class CollisionShape
    {
        public CollisionShapeType ShapeType { get; set; } = CollisionShapeType.Sphere;

        /// <summary>Radius for Sphere/Capsule shapes.</summary>
        public float Radius { get; set; } = 0.1f;

        /// <summary>Half-extents for Box shape (x, y, z).</summary>
        public Vector3 HalfExtents { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);

        /// <summary>Height for Capsule shape.</summary>
        public float Height { get; set; } = 0.3f;
    }

    /// <summary>
    /// A bullet pattern = one emitter + N modifiers.
    /// This is the primary data unit for the pattern editor.
    /// </summary>
    public class BulletPattern
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>Emitter: determines initial bullet distribution.</summary>
        public IEmitter Emitter { get; set; }

        /// <summary>Modifiers: applied in order to alter bullet behavior.</summary>
        public List<IModifier> Modifiers { get; set; } = new();

        /// <summary>Bullet visual scale.</summary>
        public float BulletScale { get; set; } = 0.15f;

        /// <summary>Bullet color (RGBA).</summary>
        public Color BulletColor { get; set; } = new Color(1f, 0.3f, 0.3f, 1f);

        /// <summary>Pattern duration in seconds.</summary>
        public float Duration { get; set; } = 5f;

        /// <summary>
        /// Deterministic seed for PRNG. Controls all random behavior in this pattern
        /// (e.g. HomingModifier anti-parallel axis). Same seed = same trajectory.
        /// </summary>
        public int Seed { get; set; } = 0;

        /// <summary>Bullet mesh shape type.</summary>
        public MeshType MeshType { get; set; } = MeshType.Sphere;

        /// <summary>Color curve: maps normalized time (0..1) to color multiplier. Null = constant color.</summary>
        public SerializableCurve ColorCurve { get; set; }

        /// <summary>Collision shape definition for hitbox visualization.</summary>
        public CollisionShape Collision { get; set; }
    }
}
