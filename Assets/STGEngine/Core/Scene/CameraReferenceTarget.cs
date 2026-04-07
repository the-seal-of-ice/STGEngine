using System;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 相机脚本的参考对象类型。决定 ICameraFrameProvider 的原点来源。
    /// </summary>
    public enum CameraReferenceTarget
    {
        /// <summary>玩家位置 + 样条线标架（默认行为）。</summary>
        Player,
        /// <summary>样条线中心点 + 高度偏移（假想边界中心）。</summary>
        BoundaryCenter,
        /// <summary>指定 Boss 位置。</summary>
        Boss,
        /// <summary>指定敌人位置。</summary>
        Enemy,
        /// <summary>绝对世界坐标。</summary>
        WorldFixed
    }

    /// <summary>
    /// 相机脚本的坐标标架模式。决定 ICameraFrameProvider 的轴方向。
    /// </summary>
    public enum CameraFrameMode
    {
        /// <summary>使用样条线 tangent/normal 作为标架（弯道自然旋转）。</summary>
        SplineAxes,
        /// <summary>使用固定世界轴。</summary>
        WorldAxes,
        /// <summary>使用目标自身朝向。</summary>
        TargetForward
    }
}
