using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.DataModel;
using STGEngine.Core.Emitters;
using STGEngine.Core.Modifiers;

namespace STGEngine.Runtime.Bullet
{
    /// <summary>
    /// Computed state of a single bullet at a given time.
    /// </summary>
    public struct BulletState
    {
        public Vector3 Position;
        public float Scale;
        public Color Color;
    }

    /// <summary>
    /// Stateless evaluator: computes all bullet positions for a BulletPattern
    /// at an arbitrary time t. Formula modifiers are evaluated analytically,
    /// enabling instant seek to any point in time.
    /// </summary>
    public static class BulletEvaluator
    {
        /// <summary>
        /// Evaluate all bullets for the given pattern at time t.
        /// Returns a list of BulletState (position + visual data).
        /// </summary>
        public static List<BulletState> EvaluateAll(BulletPattern pattern, float t)
        {
            if (pattern?.Emitter == null)
                return new List<BulletState>(0);

            var emitter = pattern.Emitter;
            int count = emitter.Count;
            var results = new List<BulletState>(count);

            // Separate formula modifiers (evaluated per-bullet)
            List<IFormulaModifier> formulaMods = null;
            bool hasSpeedCurve = false;
            SpeedCurveModifier speedCurveMod = null;
            bool hasIndependentWave = false;

            if (pattern.Modifiers != null)
            {
                formulaMods = new List<IFormulaModifier>(pattern.Modifiers.Count);
                foreach (var mod in pattern.Modifiers)
                {
                    if (mod is IFormulaModifier fm)
                    {
                        formulaMods.Add(fm);
                        if (mod is SpeedCurveModifier scm)
                        {
                            hasSpeedCurve = true;
                            speedCurveMod = scm;
                        }
                        if (mod is IndependentWaveModifier)
                            hasIndependentWave = true;
                    }
                    // ISimulationModifier: skipped in formula evaluation path
                }
            }

            for (int i = 0; i < count; i++)
            {
                var spawn = emitter.Evaluate(i, t);
                var dir = spawn.Direction;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector3.forward;
                else
                    dir.Normalize();

                // Base linear displacement: position = spawnPos + dir * speed * t
                Vector3 pos = spawn.Position;

                if (formulaMods != null && formulaMods.Count > 0)
                {
                    // Compute travel distance for IndependentWaveModifier
                    float distance = 0f;
                    if (hasIndependentWave)
                    {
                        if (speedCurveMod != null)
                            distance = speedCurveMod.Evaluate(t, spawn.Position, dir).magnitude;
                        else
                            distance = spawn.Speed * t;
                    }

                    // If a SpeedCurveModifier exists, it replaces the linear displacement.
                    // Other formula modifiers (Wave etc.) add offsets on top.
                    if (hasSpeedCurve)
                    {
                        foreach (var fm in formulaMods)
                        {
                            if (fm is IndependentWaveModifier)
                                pos += fm.Evaluate(distance, spawn.Position, dir);
                            else
                                pos += fm.Evaluate(t, spawn.Position, dir);
                        }
                    }
                    else
                    {
                        // No speed curve: use linear displacement + additive modifiers
                        pos += dir * (spawn.Speed * t);
                        foreach (var fm in formulaMods)
                        {
                            if (fm is IndependentWaveModifier)
                                pos += fm.Evaluate(distance, spawn.Position, dir);
                            else
                                pos += fm.Evaluate(t, spawn.Position, dir);
                        }
                    }
                }
                else
                {
                    // No modifiers: simple linear motion
                    pos += dir * (spawn.Speed * t);
                }

                results.Add(new BulletState
                {
                    Position = pos,
                    Scale = pattern.BulletScale,
                    Color = pattern.BulletColor
                });
            }

            return results;
        }
    }
}
