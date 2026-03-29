using UnityEngine;
using STGEngine.Core.Emitters;
using STGEngine.Core.Random;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Base modifier interface.
    /// Three subtypes:
    /// - IFormulaModifier (stateless, seekable position offset)
    /// - ISimulationModifier (stateful, frame-stepped)
    /// - ISpawnModifier (one-shot, modifies BulletSpawnData at emission time)
    /// </summary>
    public interface IModifier
    {
        /// <summary>YAML type tag.</summary>
        string TypeName { get; }

        /// <summary>True = simulation modifier, false = formula or spawn modifier.</summary>
        bool RequiresSimulation { get; }
    }

    /// <summary>
    /// Formula modifier: returns a position offset = f(t, basePosition, baseDirection).
    /// BulletEvaluator sums all modifier offsets onto the base position.
    /// SpeedCurveModifier replaces the linear displacement; WaveModifier adds lateral wave.
    /// Supports instant seek to any time.
    /// </summary>
    public interface IFormulaModifier : IModifier
    {
        /// <summary>
        /// Compute position contribution at time t.
        /// For displacement modifiers (SpeedCurve): returns full displacement along direction.
        /// For additive modifiers (Wave): returns lateral offset.
        /// </summary>
        Vector3 Evaluate(float t, Vector3 basePosition, Vector3 baseDirection);
    }

    /// <summary>
    /// Simulation modifier: requires per-frame stepping.
    /// Includes state snapshot for replay/rollback (Phase 2+).
    /// </summary>
    public interface ISimulationModifier : IModifier
    {
        void Step(float dt, ref Vector3 position, ref Vector3 velocity);

        /// <summary>Export internal state for snapshot/rollback.</summary>
        object CaptureState();

        /// <summary>Restore internal state from a snapshot.</summary>
        void RestoreState(object state);
    }

    /// <summary>
    /// Spawn modifier: one-shot modification of BulletSpawnData at emission time.
    /// Applied after Emitter.Evaluate, before flight calculation begins.
    /// Use for initial position scatter, direction jitter, speed variation, etc.
    /// </summary>
    public interface ISpawnModifier : IModifier
    {
        /// <summary>
        /// Modify spawn data in-place. Called once per bullet at emission time.
        /// </summary>
        /// <param name="spawn">Spawn data to modify (Position, Direction, Speed).</param>
        /// <param name="bulletIndex">Index of this bullet in the emission batch.</param>
        /// <param name="rng">Deterministic random source for this bullet.</param>
        void Apply(ref BulletSpawnData spawn, int bulletIndex, DeterministicRng rng);
    }
}
