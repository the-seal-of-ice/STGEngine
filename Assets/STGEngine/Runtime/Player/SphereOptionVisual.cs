using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>默认浮游炮视觉：半透明球体占位。</summary>
    public class SphereOptionVisual : IOptionVisual
    {
        private GameObject _go;

        public GameObject Create(Transform parent, int optionIndex)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _go.transform.SetParent(parent);
            _go.transform.localScale = Vector3.one * 0.5f;
            var col = _go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var rend = _go.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = new Color(0.8f, 0.9f, 1f, 0.7f);
            return _go;
        }

        public void UpdateTransform(Vector3 worldPos, Quaternion rot, float dt)
        {
            if (_go != null)
            {
                _go.transform.position = worldPos;
                _go.transform.rotation = rot;
            }
        }

        public void OnPowerTierChanged(int newOptionCount) { }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }
    }
}
