using System.Collections.Generic;
using STGEngine.Core.Scene;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// CameraScript ActionEvent 的参数：关键帧序列 + blend 时长。
    /// </summary>
    public class CameraScriptParams : IActionParams
    {
        /// <summary>关键帧列表（按 Time 升序）。</summary>
        public List<CameraKeyframe> Keyframes { get; set; } = new();

        /// <summary>从当前相机状态过渡到第一帧的时长（秒）。</summary>
        public float BlendIn { get; set; } = 0.5f;

        /// <summary>从最后一帧过渡回原相机的时长（秒）。</summary>
        public float BlendOut { get; set; } = 0.5f;
    }
}
