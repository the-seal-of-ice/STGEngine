using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景系统集成测试引导（样条线版）。
    /// 创建一条蜿蜒的 3D 样条线通路，验证沿曲线生成和滚动效果。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/SceneTestSetup")]
    public class SceneTestSetup : MonoBehaviour
    {
        [SerializeField, Tooltip("地面材质（可选）")]
        private Material _groundMaterial;

        [SerializeField, Tooltip("场景流动速度倍率")]
        private float _speedMultiplier = 1f;

        private ChunkGenerator _generator;

        private void Start()
        {
            // 创建一条蜿蜒的样条线：在 XZ 平面上左右弯曲
            var spline = new PathSpline();
            float segLen = 80f; // 每个控制点间距
            int pointCount = 15;
            float x = 0f, z = 0f;
            float angle = 0f; // 当前前进方向角度（弧度）

            for (int i = 0; i < pointCount; i++)
            {
                spline.Points.Add(new SplinePoint { Position = new Vector3(x, 0f, z) });

                // 每段随机转向，模拟蜿蜒
                if (i > 0 && i < pointCount - 1)
                {
                    angle += Random.Range(-0.5f, 0.5f);
                }
                x += Mathf.Sin(angle) * segLen;
                z += Mathf.Cos(angle) * segLen;
            }

            var style = new SceneStyle
            {
                Name = "Spline Test Path",
                PathProfile = new PathProfile
                {
                    Spline = spline,
                    // 通路宽度：正常 20m，在 ~400m 处展开到 60m（Boss 战场），然后收窄
                    WidthCurve = new Core.Serialization.SerializableCurve(
                        (0, 20f), (350, 20f), (400, 60f), (500, 60f), (550, 20f), (2000, 20f)
                    ),
                    HeightCurve = new Core.Serialization.SerializableCurve(
                        (0, 20f), (2000, 20f)
                    ),
                    // 速度：正常 15m/s，Boss 前减速到 5m/s
                    ScrollSpeed = new Core.Serialization.SerializableCurve(
                        (0, 15f), (350, 15f), (380, 5f), (500, 5f), (550, 15f), (2000, 15f)
                    )
                },
                HasGround = true
            };

            _generator = GetComponent<ChunkGenerator>();
            if (_generator == null)
                _generator = gameObject.AddComponent<ChunkGenerator>();

            _generator.Initialize(style, _groundMaterial);
        }

        private void Update()
        {
            if (_generator != null && _generator.Scroll != null)
            {
                _generator.Scroll.SpeedMultiplier = _speedMultiplier;
            }
        }

        private void OnGUI()
        {
            if (_generator == null || _generator.Scroll == null) return;

            var scroll = _generator.Scroll;
            GUILayout.BeginArea(new Rect(10, 10, 300, 130));
            GUILayout.Label($"Scrolled: {scroll.TotalScrolled:F1}m");
            GUILayout.Label($"Speed: {scroll.CurrentSpeed:F1} m/s");
            GUILayout.Label($"Active Chunks: {_generator.ActiveChunks.Count}");
            GUILayout.Label($"Speed Multiplier: {_speedMultiplier:F2}");
            _speedMultiplier = GUILayout.HorizontalSlider(_speedMultiplier, 0f, 3f);
            GUILayout.EndArea();
        }
    }
}
