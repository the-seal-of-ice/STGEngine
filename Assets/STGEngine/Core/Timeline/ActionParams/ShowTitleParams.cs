using UnityEngine;

namespace STGEngine.Core.Timeline
{
    public enum TitleAnimationType { FadeIn, SlideLeft, SlideRight, Expand, TypeWriter }
    public enum ScreenPosition { TopCenter, Center, BottomCenter, TopLeft, TopRight }
    public enum TitleFontStyle { Normal, Bold, Italic, BoldItalic }

    public class ShowTitleParams : IActionParams
    {
        public string Text { get; set; } = "";
        public string SubText { get; set; } = "";
        public TitleAnimationType Animation { get; set; } = TitleAnimationType.FadeIn;
        public ScreenPosition Position { get; set; } = ScreenPosition.Center;

        /// <summary>Pixel offset from the anchor position (x=right, y=down).</summary>
        public Vector2 Offset { get; set; } = Vector2.zero;

        /// <summary>Fade-in duration in seconds.</summary>
        public float FadeInDuration { get; set; } = 0.3f;

        /// <summary>Fade-out duration in seconds.</summary>
        public float FadeOutDuration { get; set; } = 0.5f;

        /// <summary>Main title font size.</summary>
        public int FontSize { get; set; } = 28;

        /// <summary>Subtitle font size.</summary>
        public int SubFontSize { get; set; } = 16;

        /// <summary>Font style for the main title.</summary>
        public TitleFontStyle FontStyle { get; set; } = TitleFontStyle.Bold;

        /// <summary>Main title color (RGBA).</summary>
        public Color TitleColor { get; set; } = Color.white;

        /// <summary>Subtitle color (RGBA).</summary>
        public Color SubTitleColor { get; set; } = new Color(0.85f, 0.85f, 0.85f, 1f);

        /// <summary>
        /// Optional image path (relative to Assets/Resources or STGData).
        /// When set, displays an image alongside or instead of text.
        /// Empty = text only.
        /// </summary>
        public string ImagePath { get; set; } = "";

        /// <summary>Image width in pixels. 0 = auto from texture.</summary>
        public int ImageWidth { get; set; } = 0;

        /// <summary>Image height in pixels. 0 = auto from texture.</summary>
        public int ImageHeight { get; set; } = 0;
    }
}
