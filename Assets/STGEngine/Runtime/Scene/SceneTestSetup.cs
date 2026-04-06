// Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景系统集成测试引导。在 Start 中创建默认 SceneStyle
    /// 并初始化 ChunkGenerator，用于验证 3D 卷轴流动效果。
    /// 测试完成后可删除此文件。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/SceneTestSetup")]
    public class SceneTestSetup : MonoBehaviour
    {
        [SerializeField, Tooltip("地面材质（可选，不设置则使用默认白色）")]
        private Material _groundMaterial;

        [SerializeField, Tooltip("场景流动速度倍率")]
        private float _speedMultiplier = 1f;

        private ChunkGenerator _generator;

        private void Start()
        {
            var style = new SceneStyle
            {
                Name = "Test Path",
                PathProfile = new PathProfile
                {
                    WidthCurve = new Core.Serialization.SerializableCurve(
                        (0, 20f), (180, 20f), (200, 60f), (300, 60f), (320, 20f), (1000, 20f)
                    ),
                    HeightCurve = new Core.Serialization.SerializableCurve(
                        (0, 20f), (1000, 20f)
                    ),
                    ScrollSpeed = new Core.Serialization.SerializableCurve(
                        (0, 15f), (180, 15f), (195, 5f), (300, 5f), (320, 15f), (1000, 15f)
                    ),
                    DriftCurve = new Core.Serialization.SerializableCurve(
                        (0, 0f), (80, 6f), (160, -5f), (240, 4f), (320, -3f),
                        (400, 5f), (500, 0f), (600, -4f), (700, 3f), (800, -2f), (1000, 0f)
                    ),
                    TotalLength = 1000f
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
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Label($"Scrolled: {scroll.TotalScrolled:F1}m");
            GUILayout.Label($"Speed: {scroll.CurrentSpeed:F1} m/s");
            GUILayout.Label($"Heading: {scroll.CurrentHeading:F1}°");
            GUILayout.Label($"Active Chunks: {_generator.ActiveChunks.Count}");
            GUILayout.Label($"Speed Multiplier: {_speedMultiplier:F2}");
            _speedMultiplier = GUILayout.HorizontalSlider(_speedMultiplier, 0f, 3f);
            GUILayout.EndArea();
        }
    }
}
