using UnityEngine;

namespace STGEngine.Core.Random
{
    /// <summary>
    /// Deterministic PRNG based on Xoshiro256**.
    /// Each instance maintains independent state — safe for per-bullet usage.
    /// Cross-platform consistent (no dependency on System.Random or UnityEngine.Random).
    /// </summary>
    public class DeterministicRng
    {
        private ulong _s0, _s1, _s2, _s3;

        public DeterministicRng(int seed)
        {
            // SplitMix64 to initialize 4 state words from a single seed
            ulong s = (ulong)seed;
            _s0 = SplitMix64(ref s);
            _s1 = SplitMix64(ref s);
            _s2 = SplitMix64(ref s);
            _s3 = SplitMix64(ref s);
        }

        private DeterministicRng(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
        }

        /// <summary>Returns next 64-bit unsigned integer.</summary>
        public ulong NextULong()
        {
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;

            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);

            return result;
        }

        /// <summary>Returns a float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextULong() >> 40) * (1.0f / (1L << 24));
        }

        /// <summary>Returns a float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + NextFloat() * (max - min);
        }

        /// <summary>Returns an int in [min, max) (exclusive upper bound).</summary>
        public int Range(int min, int max)
        {
            if (min >= max) return min;
            return min + (int)(NextULong() % (ulong)(max - min));
        }

        /// <summary>
        /// Returns a uniformly distributed point on the unit sphere.
        /// Uses Marsaglia's method.
        /// </summary>
        public Vector3 OnUnitSphere()
        {
            float u = Range(-1f, 1f);
            float theta = Range(0f, 2f * Mathf.PI);
            float s = Mathf.Sqrt(1f - u * u);
            return new Vector3(s * Mathf.Cos(theta), s * Mathf.Sin(theta), u);
        }

        // ─── State Snapshot ───

        /// <summary>Capture full PRNG state for replay/rollback.</summary>
        public RngState CaptureState() => new RngState(_s0, _s1, _s2, _s3);

        /// <summary>Restore PRNG state from a previous snapshot.</summary>
        public void RestoreState(RngState state)
        {
            _s0 = state.S0; _s1 = state.S1; _s2 = state.S2; _s3 = state.S3;
        }

        /// <summary>Create an independent clone with the same state.</summary>
        public DeterministicRng Clone() => new DeterministicRng(_s0, _s1, _s2, _s3);

        // ─── Helpers ───

        private static ulong RotateLeft(ulong x, int k)
        {
            return (x << k) | (x >> (64 - k));
        }

        private static ulong SplitMix64(ref ulong state)
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Serializable PRNG state snapshot.</summary>
        public readonly struct RngState
        {
            public readonly ulong S0, S1, S2, S3;
            public RngState(ulong s0, ulong s1, ulong s2, ulong s3)
            {
                S0 = s0; S1 = s1; S2 = s2; S3 = s3;
            }
        }
    }
}
