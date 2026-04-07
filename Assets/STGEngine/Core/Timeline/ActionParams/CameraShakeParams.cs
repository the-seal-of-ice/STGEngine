using STGEngine.Core.Scene;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// CameraShake ActionEvent 的参数。
    /// </summary>
    public class CameraShakeParams : IActionParams
    {
        public CameraShakePreset Preset { get; set; } = new();
    }
}
