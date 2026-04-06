// Assets/STGEngine/Runtime/Scene/ChunkGenerator.cs
using UnityEngine;
using System.Collections.Generic;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景分块生成器。维护 Chunk 滑动窗口：
    /// 玩家前方保持 N 个活跃 Chunk，身后超出距离的 Chunk 回收进池中。
    /// 场景流动由 ScrollController 驱动。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/ChunkGenerator")]
    public class ChunkGenerator : MonoBehaviour
    {
        [Header("Chunk Settings")]
        [SerializeField, Tooltip("每个 Chunk 的长度（米）")]
        private float _chunkLength = 40f;

        [SerializeField, Tooltip("玩家前方保持的活跃 Chunk 数量")]
        private int _forwardChunkCount = 3;

        [SerializeField, Tooltip("玩家后方保留的 Chunk 数量（超出则回收）")]
        private int _behindChunkCount = 1;

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
        private float _nextChunkDistance;
        private bool _initialized;
        private Material _defaultMaterial;

        /// <summary>
        /// 初始化生成器。由外部调用（手动 DI 模式，与现有 PlayerCamera 一致）。
        /// </summary>
        public void Initialize(SceneStyle style, Material groundMaterial = null)
        {
            _style = style;
            if (groundMaterial != null) _groundMaterial = groundMaterial;

            _scroll = new ScrollController(_activeChunks);
            _scroll.SetProfile(style.PathProfile);

            _nextChunkIndex = 0;
            _nextChunkDistance = 0f;

            // 生成初始 Chunk 填满前方窗口
            int totalInitial = _behindChunkCount + _forwardChunkCount;
            float startOffset = -_behindChunkCount * _chunkLength;
            for (int i = 0; i < totalInitial; i++)
            {
                var chunk = CreateChunk(_nextChunkIndex, _nextChunkDistance);
                chunk.Root.transform.position = new Vector3(0f, 0f, startOffset + i * _chunkLength);
                _activeChunks.Add(chunk);
                _nextChunkIndex++;
                _nextChunkDistance += _chunkLength;
            }

            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            _scroll.Tick(Time.deltaTime);
            RecycleAndSpawn();
        }

        /// <summary>
        /// 回收已移过玩家后方的 Chunk，在前方生成新 Chunk。
        /// </summary>
        private void RecycleAndSpawn()
        {
            float recycleZ = -(_behindChunkCount + 1) * _chunkLength;
            while (_activeChunks.Count > 0)
            {
                var oldest = _activeChunks[0];
                float chunkEndZ = oldest.Root.transform.position.z + oldest.Length;
                if (chunkEndZ < recycleZ)
                {
                    RecycleChunk(oldest);
                    _activeChunks.RemoveAt(0);
                }
                else break;
            }

            float spawnZ = _forwardChunkCount * _chunkLength;
            while (_activeChunks.Count > 0)
            {
                var newest = _activeChunks[_activeChunks.Count - 1];
                float newestEndZ = newest.Root.transform.position.z + newest.Length;
                if (newestEndZ < spawnZ)
                {
                    var chunk = CreateChunk(_nextChunkIndex, _nextChunkDistance);
                    chunk.Root.transform.position = new Vector3(0f, 0f, newestEndZ);
                    _activeChunks.Add(chunk);
                    _nextChunkIndex++;
                    _nextChunkDistance += _chunkLength;
                }
                else break;
            }
        }

        private Chunk CreateChunk(int index, float startDistance)
        {
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
            }

            chunk.Index = index;
            chunk.StartDistance = startDistance;
            chunk.Length = _chunkLength;
            chunk.StartSample = _style.PathProfile.SampleAt(startDistance);
            chunk.EndSample = _style.PathProfile.SampleAt(startDistance + _chunkLength);
            chunk.Root.name = $"Chunk_{index}";
            chunk.Root.transform.SetParent(transform);

            if (_style.HasGround)
            {
                BuildGround(chunk);
            }

            return chunk;
        }

        private void BuildGround(Chunk chunk)
        {
            if (chunk.Ground == null)
            {
                chunk.Ground = new GameObject("Ground");
                chunk.Ground.transform.SetParent(chunk.Root.transform, false);
                chunk.Ground.AddComponent<MeshFilter>();
                chunk.Ground.AddComponent<MeshRenderer>();
            }

            var mesh = GroundMeshBuilder.Build(chunk);
            chunk.Ground.GetComponent<MeshFilter>().sharedMesh = mesh;

            if (_groundMaterial != null)
            {
                chunk.Ground.GetComponent<MeshRenderer>().sharedMaterial = _groundMaterial;
            }
            else
            {
                if (_defaultMaterial == null)
                {
                    _defaultMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    _defaultMaterial.name = "DefaultGround";
                    _defaultMaterial.color = new Color(0.4f, 0.5f, 0.3f);
                    _defaultMaterial.mainTexture = GenerateCheckerTexture(256, 8);
                    _defaultMaterial.mainTextureScale = new Vector2(4f, 4f);
                }
                chunk.Ground.GetComponent<MeshRenderer>().sharedMaterial = _defaultMaterial;
            }
        }

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

            if (_defaultMaterial != null) Destroy(_defaultMaterial);
        }

        /// <summary>
        /// 生成棋盘格纹理，用于默认地面材质的速度参照。
        /// </summary>
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
