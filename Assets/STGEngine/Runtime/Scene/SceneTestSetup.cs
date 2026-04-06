using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景系统集成测试引导（样条线版 + 障碍物）。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/SceneTestSetup")]
    public class SceneTestSetup : MonoBehaviour
    {
        [SerializeField, Tooltip("地面材质（可选）")]
        private Material _groundMaterial;

        [SerializeField, Tooltip("场景流动速度倍率")]
        private float _speedMultiplier = 1f;

        private ChunkGenerator _generator;
        private Dictionary<string, GameObject> _testPrefabs;

        private void Start()
        {
            // Create spline
            var spline = new PathSpline();
            float segLen = 80f;
            int pointCount = 15;
            float x = 0f, z = 0f;
            float angle = 0f;

            for (int i = 0; i < pointCount; i++)
            {
                spline.Points.Add(new SplinePoint { Position = new Vector3(x, 0f, z) });
                if (i > 0 && i < pointCount - 1)
                    angle += Random.Range(-0.5f, 0.5f);
                x += Mathf.Sin(angle) * segLen;
                z += Mathf.Cos(angle) * segLen;
            }

            var style = new SceneStyle
            {
                Name = "Spline Test Path",
                PathProfile = new PathProfile
                {
                    Spline = spline,
                    WidthCurve = new Core.Serialization.SerializableCurve(
                        (0, 20f), (350, 20f), (400, 60f), (500, 60f), (550, 20f), (2000, 20f)
                    ),
                    HeightCurve = new Core.Serialization.SerializableCurve(
                        (0, 20f), (2000, 20f)
                    ),
                    ScrollSpeed = new Core.Serialization.SerializableCurve(
                        (0, 15f), (350, 15f), (380, 5f), (500, 5f), (550, 15f), (2000, 15f)
                    )
                },
                HasGround = true,
                ObstacleConfigs = new List<ObstacleConfig>
                {
                    new ObstacleConfig
                    {
                        PrefabVariants = new List<string> { "test_bamboo" },
                        Density = 0.08f,
                        MinSpacing = 2.5f,
                        ScaleRange = new Vector2(0.7f, 1.3f),
                        RotationRange = new Vector2(0f, 360f),
                        PlacementZone = PlacementZone.Roadside,
                        ContactResponse = ContactResponse.Sway,
                        Tag = "bamboo"
                    },
                    new ObstacleConfig
                    {
                        PrefabVariants = new List<string> { "test_rock" },
                        Density = 0.02f,
                        MinSpacing = 5f,
                        ScaleRange = new Vector2(0.8f, 2.0f),
                        RotationRange = new Vector2(0f, 360f),
                        PlacementZone = PlacementZone.Roadside,
                        ContactResponse = ContactResponse.Nudge,
                        Tag = "rock"
                    }
                },
                HazardFrequency = 2f
            };

            _generator = GetComponent<ChunkGenerator>();
            if (_generator == null)
                _generator = gameObject.AddComponent<ChunkGenerator>();

            // Create test prefabs and register with pool before Initialize
            CreateAndRegisterTestPrefabs();

            _generator.Initialize(style, _groundMaterial, _testPrefabs);
        }

        /// <summary>
        /// Create primitive GameObjects as test obstacle prefabs
        /// and register them with the obstacle pool.
        /// </summary>
        private void CreateAndRegisterTestPrefabs()
        {
            // Bamboo: tall thin cylinder (green)
            var bamboo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bamboo.name = "TestBamboo";
            bamboo.transform.localScale = new Vector3(0.3f, 4f, 0.3f);
            var bambooRenderer = bamboo.GetComponent<Renderer>();
            var bambooMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bambooMat.color = new Color(0.2f, 0.6f, 0.15f);
            bambooRenderer.sharedMaterial = bambooMat;
            bamboo.SetActive(false);
            bamboo.transform.SetParent(transform);

            // Rock: scaled cube (gray)
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "TestRock";
            rock.transform.localScale = new Vector3(2f, 1.5f, 2f);
            var rockRenderer = rock.GetComponent<Renderer>();
            var rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            rockMat.color = new Color(0.5f, 0.5f, 0.45f);
            rockRenderer.sharedMaterial = rockMat;
            rock.SetActive(false);
            rock.transform.SetParent(transform);

            _testPrefabs = new Dictionary<string, GameObject>
            {
                { "test_bamboo", bamboo },
                { "test_rock", rock }
            };
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
