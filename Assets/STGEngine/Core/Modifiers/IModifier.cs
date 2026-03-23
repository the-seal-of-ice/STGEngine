using UnityEngine;

namespace STGEngine.Core.Modifiers
{
    /// <summary>
    /// Base modifier interface.
    /// Two subtypes: IFormulaModifier (stateless, seekable) and
    /// ISimulationModifier (stateful, frame-stepped).
    /// </summary>
    public interface IModifier
    {
        /// <summary>YAML type tag.</summary>
        string TypeName { get; }

        /// <summary>True = simulation modifier, false = formula modifier.</summary>
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
}
