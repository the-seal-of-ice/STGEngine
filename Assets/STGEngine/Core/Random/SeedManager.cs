namespace STGEngine.Core.Random
{
    /// <summary>
    /// Distributes deterministic sub-seeds from a master seed.
    /// Each call to NextSeed() returns a unique, reproducible seed.
    /// Reset() restarts the sequence — used for replay.
    /// </summary>
    public class SeedManager
    {
        private readonly int _masterSeed;
        private int _counter;

        public SeedManager(int masterSeed)
        {
            _masterSeed = masterSeed;
            _counter = 0;
        }

        /// <summary>Current master seed.</summary>
        public int MasterSeed => _masterSeed;

        /// <summary>
        /// Derive the next sub-seed in sequence.
        /// Deterministic: same master seed + same call order = same sub-seeds.
        /// </summary>
        public int NextSeed()
        {
            return Hash(_masterSeed, _counter++);
        }

        /// <summary>Reset counter to 0. Call when restarting playback.</summary>
        public void Reset() => _counter = 0;

        /// <summary>
        /// Create a DeterministicRng from the next sub-seed.
        /// Convenience method combining NextSeed() + new DeterministicRng().
        /// </summary>
        public DeterministicRng NextRng() => new DeterministicRng(NextSeed());

        /// <summary>
        /// Simple integer hash combining two values.
        /// Based on Wang hash / LCG mixing.
        /// </summary>
        private static int Hash(int a, int b)
        {
            unchecked
            {
                int h = a * 1664525 + b * 1013904223 + 1;
                h ^= h >> 16;
                h *= -2048144789; // 0x85EBCA6B
                h ^= h >> 13;
                h *= -1028477387; // 0xC2B2AE35
                h ^= h >> 16;
                return h;
            }
        }
    }
}
