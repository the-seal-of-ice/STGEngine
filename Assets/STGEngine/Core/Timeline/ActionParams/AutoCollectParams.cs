namespace STGEngine.Core.Timeline
{
    public class AutoCollectParams : IActionParams
    {
        /// <summary>Delay before triggering auto-collect (seconds).</summary>
        public float Delay { get; set; } = 0f;
    }
}
