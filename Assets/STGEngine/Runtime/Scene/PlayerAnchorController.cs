using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 玩家锚点控制器。玩家自动沿样条线前进，
    /// 可在垂直于前进方向的平面内自由移动（WASD/方向键）。
    /// 位置 = 样条线锚点 + 局部偏移（受边界约束）。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/PlayerAnchorController")]
    public class PlayerAnchorController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Tooltip("横向移动速度（m/s）")]
        private float _moveSpeed = 14f;

        [SerializeField, Tooltip("低速模式倍率（按住 Shift）")]
        private float _slowMultiplier = 0.33f;

        /// <summary>当前相对于样条线锚点的局部偏移（在 Normal/Up 平面内）。</summary>
        public Vector2 LocalOffset { get; set; }

        /// <summary>当前世界坐标位置。</summary>
        public Vector3 WorldPosition { get; private set; }

        /// <summary>当前锚点的 PathSample。</summary>
        public PathSample CurrentAnchor { get; private set; }

        private ChunkGenerator _generator;
        private bool _initialized;

        /// <summary>初始化，绑定到 ChunkGenerator。</summary>
        public void Initialize(ChunkGenerator generator)
        {
            _generator = generator;
            _initialized = true;

            // 创建可视化球体
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 1.6f; // PlayerVisualDiameter
            var renderer = visual.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.2f, 0.5f, 1f);
            renderer.sharedMaterial = mat;
            // 移除碰撞体（纯视觉）
            var collider = visual.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
        }

        private void Update()
        {
            if (!_initialized || _generator == null) return;

            CurrentAnchor = _generator.PlayerAnchor;

            // 输入采集
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            bool slow = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            float speed = _moveSpeed * (slow ? _slowMultiplier : 1f);
            Vector2 input = new Vector2(h, v);
            if (input.sqrMagnitude > 1f) input.Normalize();

            // 更新局部偏移
            LocalOffset += input * speed * Time.deltaTime;

            // 计算世界位置：锚点 + Normal 方向偏移 + Up 方向偏移
            Vector3 up = Vector3.up;
            WorldPosition = CurrentAnchor.Position
                          + CurrentAnchor.Normal * LocalOffset.x
                          + up * LocalOffset.y;

            transform.position = WorldPosition;
        }
    }
}
