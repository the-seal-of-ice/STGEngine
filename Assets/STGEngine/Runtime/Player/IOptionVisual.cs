using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 浮游炮视觉渲染接口。
    /// 当前实现：球体占位。后期可替换为实际模型（阴阳玉、魔法阵等）。
    /// </summary>
    public interface IOptionVisual
    {
        GameObject Create(Transform parent, int optionIndex);
        void UpdateTransform(Vector3 worldPosition, Quaternion rotation, float dt);
        void OnPowerTierChanged(int newOptionCount);
        void Destroy();
    }
}
