using System;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 同参考对象内的视觉过渡效果（画面效果层）。
    /// </summary>
    public enum ScreenTransitionType
    {
        /// <summary>跳切（无过渡）。</summary>
        Cut,
        /// <summary>交叉渐变。</summary>
        CrossFade,
        /// <summary>淡入黑屏再淡出。</summary>
        FadeToBlack,
        /// <summary>淡入白屏再淡出。</summary>
        FadeToWhite,
        /// <summary>擦除。</summary>
        Wipe
    }

    /// <summary>
    /// 不同参考对象间的运动过渡方式。
    /// </summary>
    public enum MotionTransitionType
    {
        /// <summary>直接跳到新参考对象。</summary>
        Cut,
        /// <summary>平滑插值（位置+标架）。</summary>
        SmoothBlend,
        /// <summary>变速转体（先加速离开旧目标，再减速到达新目标）。</summary>
        SpeedRamp
    }
}
