using UnityEngine;
using System.Collections.Generic;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景分块生成器（样条线版）。
    /// Chunk 的几何体沿样条线生成，天然贴合曲线。
    /// 每帧根据玩家在样条线上的弧长位置，回收身后的 Chunk，在前方生成新 Chunk。
    /// 所有 Chunk 的顶点是相对于玩家位置的局部坐标，每帧重建 mesh 以跟随滚动。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/ChunkGenerator")]
    public class ChunkGenerator : MonoBehaviour
    {
        [Header("Chunk Settings")]
        [SerializeField, Tooltip("每个 Chunk 的弧长长度（米）")]
        private float _chunkLength = 40f;

        [SerializeField, Tooltip("玩家前方保持的弧长距离（米）")]
        private float _forwardDistance = 200f;

        [SerializeField, Tooltip("玩家后方保留的弧长距离（米）")]
        private float _behindDistance = 40f;

        [Header("Ground")]
        [SerializeField, Tooltip("地面材质")]
        private Material _groundMaterial;

        /// <summary>当前活跃的 Chunk 列表。</summary>
        public IReadOnlyList<Chunk> ActiveChunks => _activeChunks;

        /// <summary>场景流动控制器。</summary>
        public ScrollController Scroll => _scroll;

        private readonly List<Chunk> _activeChunks = new();
        private readonly Queue<Chunk> _chunkPool = new();
        private ScrollController _scroll;
        private SceneStyle _style;
        private int _nextChunkIndex;
        private float _nextChunkStartDist;
        private bool _initialized;
        private Material _defaultMaterial;
        private ObstaclePool _obstaclePool;
        private ObstacleScatterer _scatterer;

        /// <summary>
        /// 初始化生成器。
        /// </summary>
        public void Initialize(SceneStyle style, Material groundMaterial = null, Dictionary<string, GameObject> runtimePrefabs = null)
        {
            _style = style;
            if (groundMaterial != null) _groundMaterial = groundMaterial;

            _scroll = new ScrollController();
            _scroll.SetProfile(style.PathProfile);

            // Obstacle system
            var poolRoot = new GameObject("ObstaclePool");
            poolRoot.transform.SetParent(transform);
            poolRoot.SetActive(false);
            _obstaclePool = new ObstaclePool(poolRoot.transform);
            _scatterer = new ObstacleScatterer(_obstaclePool, style.PathProfile);

            if (runtimePrefabs != null)
            {
                foreach (var kvp in runtimePrefabs)
                    _obstaclePool.RegisterPrefab(kvp.Key, kvp.Value);
            }

            _nextChunkIndex = 0;
            _nextChunkStartDist = 0f;

            // 生成初始 Chunk：从起点到前方距离
            while (_nextChunkStartDist < _forwardDistance)
            {
                if (!SpawnChunk()) break; // 样条线到尽头则停止
            }

            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            _scroll.Tick(Time.deltaTime);
            float playerDist = _scroll.TotalScrolled;

            // 回收身后的 Chunk
            while (_activeChunks.Count > 0)
            {
                var oldest = _activeChunks[0];
                if (oldest.EndDistance < playerDist - _behindDistance)
                {
                    RecycleChunk(oldest);
                    _activeChunks.RemoveAt(0);
                }
                else break;
            }

            // 在前方生成新 Chunk
            while (_nextChunkStartDist < playerDist + _forwardDistance)
            {
                if (!SpawnChunk()) break;
            }

            // 更新摄像头：沿样条线移动
            UpdateCamera(playerDist);
        }

        /// <summary>生成一个新 Chunk。返回 false 表示样条线已到尽头。</summary>
        private bool SpawnChunk()
        {
            float splineLen = _style.PathProfile.Spline.TotalLength;
            float startDist = _nextChunkStartDist;

            if (startDist >= splineLen) return false; // 样条线已到尽头

            float endDist = Mathf.Min(startDist + _chunkLength, splineLen);

            Chunk chunk;
            if (_chunkPool.Count > 0)
            {
                chunk = _chunkPool.Dequeue();
                chunk.Activate();
            }
            else
            {
                chunk = new Chunk();
                chunk.Root = new GameObject();
                chunk.IsActive = true;
            }

            chunk.Index = _nextChunkIndex;
            chunk.StartDistance = startDist;
            chunk.EndDistance = endDist;
            chunk.Root.name = $"Chunk_{_nextChunkIndex}";
            chunk.Root.transform.SetParent(transform);
            chunk.Root.transform.localPosition = Vector3.zero;

            if (_style.HasGround)
            {
                EnsureGroundComponents(chunk);
                // 生成时构建 mesh（世界坐标，一次性）
                var mf = chunk.Ground.GetComponent<MeshFilter>();
                mf.sharedMesh = GroundMeshBuilder.Build(chunk, _style.PathProfile);
            }

            // Scatter obstacles
            if (_style.ObstacleConfigs.Count > 0)
            {
                chunk.Obstacles = _scatterer.Scatter(chunk, _style.ObstacleConfigs, _style.HazardFrequency);
                Debug.Log($"Chunk_{_nextChunkIndex} dist={startDist:F0}-{endDist:F0} obstacles={chunk.Obstacles.Count}");
            }

            _activeChunks.Add(chunk);
            _nextChunkIndex++;
            _nextChunkStartDist = endDist;
            return true;
        }

        /// <summary>确保 Chunk 有地面 mesh 组件。</summary>
        private void EnsureGroundComponents(Chunk chunk)
        {
            if (chunk.Ground == null)
            {
                chunk.Ground = new GameObject("Ground");
                chunk.Ground.transform.SetParent(chunk.Root.transform, false);
                chunk.Ground.AddComponent<MeshFilter>();
                chunk.Ground.AddComponent<MeshRenderer>();
            }

            var renderer = chunk.Ground.GetComponent<MeshRenderer>();
            if (_groundMaterial != null)
            {
                renderer.sharedMaterial = _groundMaterial;
            }
            else
            {
                if (_defaultMaterial == null)
                {
                    _defaultMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    _defaultMaterial.name = "DefaultGround";
                    _defaultMaterial.color = Color.white;
                    _defaultMaterial.mainTexture = GenerateCheckerTexture(256, 8);
                }
                renderer.sharedMaterial = _defaultMaterial;
            }
        }

        /// <summary>
        /// 玩家锚点在样条线上的当前采样结果。
        /// 玩家自动沿通路前进，自由移动是相对于此锚点的偏移。
        /// 敌人/Boss 的出生位置也基于此锚点的弧长距离。
        /// </summary>
        public PathSample PlayerAnchor { get; private set; }

        /// <summary>
        /// 更新摄像头：跟随玩家锚点沿样条线移动。
        /// Chunk 几何体在世界坐标中静止，摄像头在移动。
        /// 玩家自动前进，不需要按前进键。
        /// </summary>
        private void UpdateCamera(float playerDist)
        {
            // 更新玩家锚点
            PlayerAnchor = _style.PathProfile.SampleAt(playerDist);

            var cam = Camera.main;
            if (cam == null) return;

            // 确保远裁剪面足够大
            if (cam.farClipPlane < 1000f) cam.farClipPlane = 1000f;

            // 摄像头位置：锚点上方偏后，沿切线方向看向前方
            Vector3 camPos = PlayerAnchor.Position + Vector3.up * 12f - PlayerAnchor.Tangent * 8f;
            Vector3 lookTarget = PlayerAnchor.Position + PlayerAnchor.Tangent * 30f;

            cam.transform.position = Vector3.Lerp(cam.transform.position, camPos, Time.deltaTime * 5f);
            Quaternion targetRot = Quaternion.LookRotation(lookTarget - cam.transform.position, Vector3.up);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, Time.deltaTime * 5f);
        }

        /// <summary>回收 Chunk 到池中。</summary>
        private void RecycleChunk(Chunk chunk)
        {
            chunk.Deactivate();

            if (chunk.Ground != null)
            {
                var mf = chunk.Ground.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Destroy(mf.sharedMesh);
                    mf.sharedMesh = null;
                }
            }

            // Return obstacles to pool
            if (chunk.Obstacles.Count > 0)
            {
                _scatterer.ReturnAll(chunk.Obstacles);
            }

            _chunkPool.Enqueue(chunk);
        }

        private void OnDestroy()
        {
            foreach (var chunk in _activeChunks)
            {
                if (chunk.Ground != null)
                {
                    var mf = chunk.Ground.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        Destroy(mf.sharedMesh);
                }
                if (chunk.Root != null) Destroy(chunk.Root);
            }
            _activeChunks.Clear();

            while (_chunkPool.Count > 0)
            {
                var chunk = _chunkPool.Dequeue();
                if (chunk.Root != null) Destroy(chunk.Root);
            }

            _obstaclePool?.Clear();

            if (_defaultMaterial != null) Destroy(_defaultMaterial);
        }

        private static Texture2D GenerateCheckerTexture(int size, int divisions)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            int cellSize = size / divisions;
            var colorA = new Color(0.45f, 0.55f, 0.35f);
            var colorB = new Color(0.35f, 0.45f, 0.25f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isA = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                    tex.SetPixel(x, y, isA ? colorA : colorB);
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
