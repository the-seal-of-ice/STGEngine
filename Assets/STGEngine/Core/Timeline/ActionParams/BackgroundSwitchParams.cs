namespace STGEngine.Core.Timeline
{
    public enum BgTransitionType { Cut, CrossFade, SlideUp, SlideDown }

    public class BackgroundSwitchParams : IActionParams
    {
        public string BackgroundId { get; set; } = "";
        public BgTransitionType Transition { get; set; } = BgTransitionType.CrossFade;
        public float TransitionDuration { get; set; } = 1.0f;
        public float ScrollSpeedX { get; set; } = 0f;
        public float ScrollSpeedY { get; set; } = -0.5f;
    }
}
