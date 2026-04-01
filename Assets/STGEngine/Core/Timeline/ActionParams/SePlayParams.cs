namespace STGEngine.Core.Timeline
{
    public class SePlayParams : IActionParams
    {
        public string SeId { get; set; } = "";
        public float Volume { get; set; } = 1.0f;
        public float Pitch { get; set; } = 1.0f;
        /// <summary>Whether the SE loops. When true, block Duration = N * clip length.</summary>
        public bool Loop { get; set; } = false;
    }
}
