namespace STGEngine.Core.Emitters
{
    /// <summary>
    /// Emitter: determines the initial spatial distribution of bullets.
    /// Given index i (0..Count-1), returns the spawn data for bullet i.
    /// </summary>
    public interface IEmitter
    {
        /// <summary>YAML type tag (e.g. "point", "ring").</summary>
        string TypeName { get; }

        /// <summary>Number of bullets per emission.</summary>
        int Count { get; set; }

        /// <summary>
        /// Compute spawn data for bullet at given index.
        /// Time parameter supports rotating emitters; stateless emitters may ignore it.
        /// </summary>
        BulletSpawnData Evaluate(int index, float time);
    }
}
