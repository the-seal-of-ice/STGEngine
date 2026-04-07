using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Serialization;
using STGEngine.Core.Timeline;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 将 CameraKeyframe 列表分解为每属性的 SerializableCurve，
    /// 支持曲线编辑器的可视化和编辑。
    /// </summary>
    public class CameraPropertyCurves
    {
        public SerializableCurve OffsetX { get; set; } = new();
        public SerializableCurve OffsetY { get; set; } = new();
        public SerializableCurve OffsetZ { get; set; } = new();
        public SerializableCurve RotationX { get; set; } = new();
        public SerializableCurve RotationY { get; set; } = new();
        public SerializableCurve RotationZ { get; set; } = new();
        public SerializableCurve FOVCurve { get; set; } = new();

        /// <summary>从关键帧列表构建属性曲线。</summary>
        public static CameraPropertyCurves FromKeyframes(List<CameraKeyframe> keyframes)
        {
            var curves = new CameraPropertyCurves();
            if (keyframes == null || keyframes.Count == 0) return curves;

            foreach (var kf in keyframes)
            {
                curves.OffsetX.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.PositionOffset.x });
                curves.OffsetY.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.PositionOffset.y });
                curves.OffsetZ.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.PositionOffset.z });
                curves.RotationX.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.Rotation.x });
                curves.RotationY.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.Rotation.y });
                curves.RotationZ.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.Rotation.z });
                curves.FOVCurve.Keyframes.Add(new CurveKeyframe { Time = kf.Time, Value = kf.FOV });
            }

            curves.OffsetX.AutoComputeTangents();
            curves.OffsetY.AutoComputeTangents();
            curves.OffsetZ.AutoComputeTangents();
            curves.RotationX.AutoComputeTangents();
            curves.RotationY.AutoComputeTangents();
            curves.RotationZ.AutoComputeTangents();
            curves.FOVCurve.AutoComputeTangents();

            return curves;
        }

        /// <summary>
        /// 将曲线数据同步回关键帧列表。
        /// 以 OffsetX 的关键帧时间点为基准，在每个时间点采样所有曲线。
        /// </summary>
        public void ApplyToKeyframes(List<CameraKeyframe> keyframes)
        {
            keyframes.Clear();
            if (OffsetX.Keyframes.Count == 0) return;

            foreach (var ckf in OffsetX.Keyframes)
            {
                float t = ckf.Time;
                keyframes.Add(new CameraKeyframe
                {
                    Time = t,
                    PositionOffset = new Vector3(
                        OffsetX.Evaluate(t),
                        OffsetY.Evaluate(t),
                        OffsetZ.Evaluate(t)),
                    Rotation = new Vector3(
                        RotationX.Evaluate(t),
                        RotationY.Evaluate(t),
                        RotationZ.Evaluate(t)),
                    FOV = FOVCurve.Evaluate(t),
                    Easing = EasingType.EaseInOut
                });
            }
        }

        /// <summary>获取所有曲线及其显示名称和颜色。</summary>
        public IReadOnlyList<(string name, SerializableCurve curve, Color color)> GetAllCurves()
        {
            return new[]
            {
                ("Offset X", OffsetX, new Color(1f, 0.3f, 0.3f)),
                ("Offset Y", OffsetY, new Color(0.3f, 1f, 0.3f)),
                ("Offset Z", OffsetZ, new Color(0.3f, 0.5f, 1f)),
                ("Rot X", RotationX, new Color(1f, 0.5f, 0.5f)),
                ("Rot Y", RotationY, new Color(0.5f, 1f, 0.5f)),
                ("Rot Z", RotationZ, new Color(0.5f, 0.7f, 1f)),
                ("FOV", FOVCurve, new Color(1f, 0.9f, 0.3f))
            };
        }
    }
}
