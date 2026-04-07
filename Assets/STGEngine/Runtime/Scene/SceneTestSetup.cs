using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Scene;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景系统集成测试引导（Phase 3：含玩家、边界、碰撞、交互）。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/SceneTestSetup")]
    public class SceneTestSetup : MonoBehaviour
    {
        [SerializeField, Tooltip("地面材质（可选）")]
        private Material _groundMaterial;

        [SerializeField, Tooltip("场景流动速度倍率")]
        private float _speedMultiplier = 1f;

        private ChunkGenerator _generator;
        private PlayerAnchorController _player;
        private BoundaryForce _boundary;
        private HazardCollision _hazard;
        private ObstacleInteraction _interaction;
        private CameraScriptPlayer _cameraScriptPlayer;
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
                        (0, 15f), (2000, 15f)
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
                        ScaleRange = new Vector2(0.6f, 1.5f),
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
                        ScaleRange = new Vector2(0.6f, 1.8f),
                        RotationRange = new Vector2(0f, 360f),
                        PlacementZone = PlacementZone.Roadside,
                        ContactResponse = ContactResponse.Nudge,
                        Tag = "rock"
                    }
                },
                HazardFrequency = 2f
            };

            // ChunkGenerator
            _generator = GetComponent<ChunkGenerator>();
            if (_generator == null)
                _generator = gameObject.AddComponent<ChunkGenerator>();

            CreateAndRegisterTestPrefabs();
            _generator.Initialize(style, _groundMaterial, _testPrefabs);

            // Player
            var playerGo = new GameObject("Player");
            _player = playerGo.AddComponent<PlayerAnchorController>();
            _player.Initialize(_generator);
            _generator.Player = _player;

            // Boundary
            var boundaryGo = new GameObject("BoundaryForce");
            _boundary = boundaryGo.AddComponent<BoundaryForce>();
            _boundary.Initialize(_player, _generator);

            // Hazard collision
            var hazardGo = new GameObject("HazardCollision");
            _hazard = hazardGo.AddComponent<HazardCollision>();
            _hazard.Initialize(_player, _generator);
            _hazard.OnHazardHit += obs =>
            {
                Debug.Log($"HAZARD HIT! {obs.Config.Tag} at dist={obs.ArcDistance:F0}m");
            };

            // Obstacle interaction
            var interactionGo = new GameObject("ObstacleInteraction");
            _interaction = interactionGo.AddComponent<ObstacleInteraction>();
            _interaction.Initialize(_player, _generator);

            // Camera script player (for scene mode)
            if (Camera.main != null)
            {
                _cameraScriptPlayer = Camera.main.gameObject.AddComponent<CameraScriptPlayer>();
                var splineFrame = new SplineCameraFrame(_player);
                _cameraScriptPlayer.Initialize(splineFrame);
            }
        }

        private void CreateAndRegisterTestPrefabs()
        {
            var bamboo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bamboo.name = "TestBamboo";
            bamboo.transform.localScale = new Vector3(0.5f, 6f, 0.5f);
            var bambooRenderer = bamboo.GetComponent<Renderer>();
            var bambooMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bambooMat.color = new Color(0.15f, 0.55f, 0.1f);
            bambooRenderer.sharedMaterial = bambooMat;
            bamboo.SetActive(false);
            bamboo.transform.SetParent(transform);

            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "TestRock";
            rock.transform.localScale = new Vector3(1.5f, 1.2f, 1.8f);
            var rockRenderer = rock.GetComponent<Renderer>();
            var rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            rockMat.color = new Color(0.45f, 0.43f, 0.4f);
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

            // 临时测试：按 C 触发镜头演出
            if (Input.GetKeyDown(KeyCode.C) && _cameraScriptPlayer != null && !_cameraScriptPlayer.IsActive)
            {
                var testParams = new CameraScriptParams
                {
                    BlendIn = 0.5f,
                    BlendOut = 0.5f,
                    Keyframes = new System.Collections.Generic.List<CameraKeyframe>
                    {
                        new CameraKeyframe { Time = 0f, PositionOffset = new Vector3(0, 10, -8), Rotation = new Vector3(30, 0, 0), FOV = 60f },
                        new CameraKeyframe { Time = 1f, PositionOffset = new Vector3(5, 12, -5), Rotation = new Vector3(20, -15, 0), FOV = 50f },
                        new CameraKeyframe { Time = 2f, PositionOffset = new Vector3(-3, 8, -10), Rotation = new Vector3(35, 10, 5), FOV = 65f },
                        new CameraKeyframe { Time = 3f, PositionOffset = new Vector3(0, 10, -8), Rotation = new Vector3(30, 0, 0), FOV = 60f },
                    }
                };
                _cameraScriptPlayer.Play(testParams);
            }

            // 临时测试：按 V 触发镜头震动
            if (Input.GetKeyDown(KeyCode.V) && _cameraScriptPlayer != null)
            {
                _cameraScriptPlayer.Shake(new CameraShakePreset
                {
                    Duration = 0.5f,
                    Amplitude = 0.5f,
                    Frequency = 30f,
                    DecayRate = 1.5f
                });
            }
        }

        private void OnGUI()
        {
            if (_generator == null || _generator.Scroll == null) return;

            var scroll = _generator.Scroll;
            GUILayout.BeginArea(new Rect(10, 10, 350, 200));
            GUILayout.Label($"Scrolled: {scroll.TotalScrolled:F1}m");
            GUILayout.Label($"Speed: {scroll.CurrentSpeed:F1} m/s");
            GUILayout.Label($"Active Chunks: {_generator.ActiveChunks.Count}");
            int totalObstacles = 0;
            foreach (var c in _generator.ActiveChunks) totalObstacles += c.Obstacles.Count;
            GUILayout.Label($"Obstacles: {totalObstacles}");
            if (_player != null)
                GUILayout.Label($"Player Offset: ({_player.LocalOffset.x:F1}, {_player.LocalOffset.y:F1})");
            if (_hazard != null)
                GUILayout.Label($"Hazard Hits: {_hazard.HitCount} {(_hazard.IsInvincible ? "(invincible)" : "")}");
            GUILayout.Label($"Speed Multiplier: {_speedMultiplier:F2}");
            _speedMultiplier = GUILayout.HorizontalSlider(_speedMultiplier, 0f, 3f);
            GUILayout.EndArea();
        }
    }
}
