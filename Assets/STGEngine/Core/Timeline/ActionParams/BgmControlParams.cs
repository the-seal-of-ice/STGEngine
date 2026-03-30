namespace STGEngine.Core.Timeline
{
    public enum BgmAction { Play, Stop, FadeOut, CrossFade }

    public class BgmControlParams : IActionParams
    {
        public BgmAction Action { get; set; } = BgmAction.Play;
        public string BgmId { get; set; } = "";
        public float FadeInDuration { get; set; } = 1.0f;
        public float FadeOutDuration { get; set; } = 1.0f;
        /// <summary>Loop restart point in seconds.</summary>
        public float LoopStartTime { get; set; } = 0f;
    }
}
