namespace STGEngine.Core.Timeline
{
    public enum ScreenEffectType { Shake, FlashWhite, FlashRed, RadialBlur, ColorShift }

    public class ScreenEffectParams : IActionParams
    {
        public ScreenEffectType EffectType { get; set; } = ScreenEffectType.Shake;
        /// <summary>Normalized intensity 0–1.</summary>
        public float Intensity { get; set; } = 1.0f;
        // Duration is controlled by ActionEvent.Duration.
    }
}
