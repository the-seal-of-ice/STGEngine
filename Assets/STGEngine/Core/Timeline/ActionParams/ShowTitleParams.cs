namespace STGEngine.Core.Timeline
{
    public enum TitleAnimationType { FadeIn, SlideLeft, SlideRight, Expand, TypeWriter }
    public enum ScreenPosition { TopCenter, Center, BottomCenter, TopLeft, TopRight }

    public class ShowTitleParams : IActionParams
    {
        public string Text { get; set; } = "";
        public string SubText { get; set; } = "";
        public TitleAnimationType Animation { get; set; } = TitleAnimationType.FadeIn;
        public ScreenPosition Position { get; set; } = ScreenPosition.Center;
        /// <summary>Seconds to hold before fade-out begins.</summary>
        public float FadeOutDelay { get; set; } = 2.0f;
    }
}
