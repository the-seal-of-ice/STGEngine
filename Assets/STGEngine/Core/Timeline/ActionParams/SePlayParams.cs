namespace STGEngine.Core.Timeline
{
    public class SePlayParams : IActionParams
    {
        public string SeId { get; set; } = "";
        public float Volume { get; set; } = 1.0f;
        public float Pitch { get; set; } = 1.0f;
    }
}
