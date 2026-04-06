# 场景系统 Phase 1: 核心管线 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立场景系统的最小可运行原型——PathProfile 数据模型 + ChunkGenerator 分块生成/回收 + 基础 3D 卷轴场景流动。

**Architecture:** 遵循现有三层架构（Core/Runtime/Editor）。Core 层定义 PathProfile 数据模型（纯数据，YAML 可序列化）。Runtime 层实现 ChunkGenerator（MonoBehaviour，管理 Chunk 滑动窗口和场景流动）。使用现有 SerializableCurve 做曲线插值，DeterministicRng 做确定性随机。本阶段不含障碍物、边界、镜头——只有空的 Chunk 地面在流动。

**Tech Stack:** Unity 2022.3 LTS, URP, C#, YamlDotNet, 现有 STGEngine 框架

**Spec:** `doc/specs/2026-07-03-scene-system-design.md`

---

## 文件结构

**新建文件：**

| 文件 | 职责 |
|------|------|
| `Assets/STGEngine/Core/Scene/PathProfile.cs` | 通路轮廓数据模型 |
| `Assets/STGEngine/Core/Scene/SceneStyle.cs` | 场景风格组合数据模型（本阶段精简版） |
| `Assets/STGEngine/Runtime/Scene/Chunk.cs` | 单个 Chunk 的运行时表示 |
| `Assets/STGEngine/Runtime/Scene/ChunkGenerator.cs` | 分块生成器 MonoBehaviour |
| `Assets/STGEngine/Runtime/Scene/ScrollController.cs` | 场景流动控制（从 ChunkGenerator 分离，职责单一） |

**依赖的现有文件（只读）：**

| 文件 | 用途 |
|------|------|
| `Assets/STGEngine/Core/Serialization/SerializableCurve.cs` | 曲线数据 + Evaluate() |
| `Assets/STGEngine/Core/WorldScale.cs` | DefaultBoundaryHalf 等常量 |
| `Assets/STGEngine/Core/Random/DeterministicRng.cs` | 确定性随机（本阶段暂不使用，Phase 2 障碍物散布时用） |

---

<!-- SPLICE_TASK1 -->

### Task 1: PathProfile 数据模型

**Files:**
- Create: `Assets/STGEngine/Core/Scene/PathProfile.cs`

- [ ] **Step 1: 创建 PathProfile 数据类**

```csharp
// Assets/STGEngine/Core/Scene/PathProfile.cs
using STGEngine.Core.Serialization;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 通路轮廓定义。描述通路随滚动距离的形态变化。
    /// 所有曲线的 X 轴为滚动距离（米），非时间。
    /// </summary>
    public class PathProfile
    {
        /// <summary>通路宽度（米）随滚动距离变化。窄通道 ~15m，Boss 战场 ~60m。</summary>
        public SerializableCurve WidthCurve { get; set; } = new((0, 20f), (100, 20f));

        /// <summary>通路高度（米）随滚动距离变化。3D 纵向活动范围。</summary>
        public SerializableCurve HeightCurve { get; set; } = new((0, 20f), (100, 20f));

        /// <summary>场景流动速度（m/s）。可变速：Boss 前减速，道中加速。</summary>
        public SerializableCurve ScrollSpeed { get; set; } = new((0, 10f), (100, 10f));

        /// <summary>通路中心线横向偏移（米）。让通路有蜿蜒感而非死直。</summary>
        public SerializableCurve DriftCurve { get; set; } = new((0, 0f), (100, 0f));

        /// <summary>通路总长度（米）。等于曲线 X 轴的最大值。</summary>
        public float TotalLength { get; set; } = 1000f;

        /// <summary>
        /// 在指定滚动距离处采样通路形态。
        /// </summary>
        public PathSample SampleAt(float distance)
        {
            float d = UnityEngine.Mathf.Clamp(distance, 0f, TotalLength);
            return new PathSample
            {
                Width = WidthCurve.Evaluate(d),
                Height = HeightCurve.Evaluate(d),
                Speed = ScrollSpeed.Evaluate(d),
                Drift = DriftCurve.Evaluate(d)
            };
        }
    }

    /// <summary>
    /// 通路在某一滚动距离处的采样结果。
    /// </summary>
    public struct PathSample
    {
        /// <summary>通路宽度（米）。</summary>
        public float Width;
        /// <summary>通路高度（米）。</summary>
        public float Height;
        /// <summary>场景流动速度（m/s）。</summary>
        public float Speed;
        /// <summary>通路中心线横向偏移（米）。</summary>
        public float Drift;
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。检查 `STGEngine.Core.asmdef` 是否已覆盖 `Core/Scene/` 目录（应该自动覆盖，因为它在 Core/ 下）。

- [ ] **Step 3: 提交**

```bash
git add Assets/STGEngine/Core/Scene/PathProfile.cs
git commit -m "feat(scene): add PathProfile data model with curve-based path definition"
```

---

<!-- SPLICE_TASK2 -->

### Task 2: SceneStyle 精简版数据模型

**Files:**
- Create: `Assets/STGEngine/Core/Scene/SceneStyle.cs`

- [ ] **Step 1: 创建 SceneStyle 数据类（Phase 1 精简版）**

Phase 1 只需要 PathProfile 和地面配置，障碍物/光照/粒子/音效在后续 Phase 添加。

```csharp
// Assets/STGEngine/Core/Scene/SceneStyle.cs
using System;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 场景风格配置。组合通路轮廓和视觉参数。
    /// Phase 1 精简版：仅含 PathProfile 和基础配置。
    /// 后续 Phase 将扩展障碍物、光照、粒子、音效等字段。
    /// </summary>
    public class SceneStyle
    {
        /// <summary>唯一标识符。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>显示名称。</summary>
        public string Name { get; set; } = "New Scene Style";

        /// <summary>通路轮廓定义。</summary>
        public PathProfile PathProfile { get; set; } = new();

        /// <summary>是否生成可见地面。月面虚空等特殊场景设为 false。</summary>
        public bool HasGround { get; set; } = true;
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/STGEngine/Core/Scene/SceneStyle.cs
git commit -m "feat(scene): add SceneStyle data model (phase 1 minimal)"
```

---

<!-- SPLICE_TASK3 -->

### Task 3: Chunk 运行时表示

**Files:**
- Create: `Assets/STGEngine/Runtime/Scene/Chunk.cs`

- [ ] **Step 1: 创建 Chunk 类**

Chunk 是场景的一个分块单元，持有自己的 GameObject、地面 mesh、以及从 PathProfile 采样的形态数据。

```csharp
// Assets/STGEngine/Runtime/Scene/Chunk.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 场景分块的运行时表示。每个 Chunk 沿滚动轴（+Z）占据固定长度，
    /// 持有地面 mesh 和从 PathProfile 采样的形态数据。
    /// </summary>
    public class Chunk
    {
        /// <summary>Chunk 在序列中的索引（0, 1, 2...），用于确定性随机种子派生。</summary>
        public int Index { get; set; }

        /// <summary>该 Chunk 起始处的滚动距离（米）。</summary>
        public float StartDistance { get; set; }

        /// <summary>该 Chunk 的长度（米）。</summary>
        public float Length { get; set; }

        /// <summary>Chunk 起始处的 PathProfile 采样。</summary>
        public PathSample StartSample { get; set; }

        /// <summary>Chunk 终点处的 PathProfile 采样。</summary>
        public PathSample EndSample { get; set; }

        /// <summary>Chunk 的根 GameObject。</summary>
        public GameObject Root { get; set; }

        /// <summary>地面 mesh 的 GameObject（Root 的子物体）。</summary>
        public GameObject Ground { get; set; }

        /// <summary>Chunk 是否处于活跃状态。</summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 在 Chunk 内部的归一化位置（0~1）处，用 Hermite 插值采样通路形态。
        /// 保证相邻 Chunk 边界处 C1 连续（位置 + 切线连续）。
        /// </summary>
        /// <param name="t">Chunk 内归一化位置，0 = 起始，1 = 终点。</param>
        public PathSample LerpAt(float t)
        {
            t = Mathf.Clamp01(t);
            // Hermite 插值（SmoothStep）保证 C1 连续
            float h = t * t * (3f - 2f * t);
            return new PathSample
            {
                Width = Mathf.Lerp(StartSample.Width, EndSample.Width, h),
                Height = Mathf.Lerp(StartSample.Height, EndSample.Height, h),
                Speed = Mathf.Lerp(StartSample.Speed, EndSample.Speed, h),
                Drift = Mathf.Lerp(StartSample.Drift, EndSample.Drift, h)
            };
        }

        /// <summary>
        /// 停用 Chunk，隐藏 GameObject 并标记为非活跃。
        /// </summary>
        public void Deactivate()
        {
            IsActive = false;
            if (Root != null) Root.SetActive(false);
        }

        /// <summary>
        /// 激活 Chunk，显示 GameObject 并标记为活跃。
        /// </summary>
        public void Activate()
        {
            IsActive = true;
            if (Root != null) Root.SetActive(true);
        }
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。检查 `STGEngine.Runtime.asmdef` 引用了 `STGEngine.Core.asmdef`。

- [ ] **Step 3: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/Chunk.cs
git commit -m "feat(scene): add Chunk runtime representation with Hermite interpolation"
```

---

<!-- SPLICE_TASK4 -->

### Task 4: 地面 Mesh 生成

**Files:**
- Create: `Assets/STGEngine/Runtime/Scene/GroundMeshBuilder.cs`

- [ ] **Step 1: 创建 GroundMeshBuilder 工具类**

负责为一个 Chunk 生成地面 mesh。地面是一个沿 Z 轴延伸的平面，宽度跟随 PathProfile 的 WidthCurve 变化，中心线跟随 DriftCurve 偏移。

```csharp
// Assets/STGEngine/Runtime/Scene/GroundMeshBuilder.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 为 Chunk 生成地面 mesh。地面沿 Z 轴延伸，宽度和中心偏移
    /// 跟随 PathProfile 曲线变化，使用 Hermite 插值保证 C1 连续。
    /// </summary>
    public static class GroundMeshBuilder
    {
        /// <summary>沿 Z 轴的细分段数。越多越平滑，但顶点越多。</summary>
        private const int SegmentsZ = 20;

        /// <summary>沿 X 轴的细分段数（横向）。</summary>
        private const int SegmentsX = 1;

        /// <summary>
        /// 为指定 Chunk 生成地面 mesh。
        /// 地面 Y 坐标固定为 0（通路底部），顶点沿 Z 轴分布，
        /// 宽度和 X 偏移由 Chunk 的 StartSample/EndSample Hermite 插值决定。
        /// </summary>
        /// <param name="chunk">目标 Chunk，需要已设置 StartSample/EndSample/Length。</param>
        /// <returns>生成的 Mesh，调用方负责赋给 MeshFilter。</returns>
        public static Mesh Build(Chunk chunk)
        {
            int vertsPerRow = SegmentsX + 1;
            int rowCount = SegmentsZ + 1;
            int vertCount = vertsPerRow * rowCount;
            int triCount = SegmentsX * SegmentsZ * 6;

            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[triCount];

            for (int z = 0; z < rowCount; z++)
            {
                float tz = (float)z / SegmentsZ;
                PathSample sample = chunk.LerpAt(tz);
                float halfWidth = sample.Width * 0.5f;
                float zPos = tz * chunk.Length;

                for (int x = 0; x < vertsPerRow; x++)
                {
                    float tx = (float)x / SegmentsX;
                    int idx = z * vertsPerRow + x;

                    float xPos = Mathf.Lerp(-halfWidth, halfWidth, tx) + sample.Drift;
                    vertices[idx] = new Vector3(xPos, 0f, zPos);
                    uvs[idx] = new Vector2(tx, tz);
                }
            }

            int tri = 0;
            for (int z = 0; z < SegmentsZ; z++)
            {
                for (int x = 0; x < SegmentsX; x++)
                {
                    int bl = z * vertsPerRow + x;
                    int br = bl + 1;
                    int tl = bl + vertsPerRow;
                    int tr = tl + 1;

                    triangles[tri++] = bl;
                    triangles[tri++] = tl;
                    triangles[tri++] = br;
                    triangles[tri++] = br;
                    triangles[tri++] = tl;
                    triangles[tri++] = tr;
                }
            }

            var mesh = new Mesh
            {
                name = $"GroundChunk_{chunk.Index}",
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/GroundMeshBuilder.cs
git commit -m "feat(scene): add GroundMeshBuilder for chunk ground mesh generation"
```

---

<!-- SPLICE_TASK5 -->

### Task 5: ScrollController 场景流动

**Files:**
- Create: `Assets/STGEngine/Runtime/Scene/ScrollController.cs`

- [ ] **Step 1: 创建 ScrollController**

负责每帧移动所有活跃 Chunk，实现场景向玩家方向流动的效果。与 ChunkGenerator 分离，职责单一。

```csharp
// Assets/STGEngine/Runtime/Scene/ScrollController.cs
using UnityEngine;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 控制场景的卷轴流动。每帧将所有活跃 Chunk 沿 -Z 方向移动，
    /// 速度由当前 PathProfile.ScrollSpeed 决定。
    /// 玩家始终在原点附近，场景向玩家流动。
    /// </summary>
    public class ScrollController
    {
        /// <summary>当前累计滚动距离（米）。</summary>
        public float TotalScrolled { get; private set; }

        /// <summary>当前帧的流动速度（m/s）。</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>速度倍率覆盖。1.0 = 正常，0.0 = 停止。用于对话减速等。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        private readonly List<Chunk> _activeChunks;
        private Core.Scene.PathProfile _profile;

        public ScrollController(List<Chunk> activeChunks)
        {
            _activeChunks = activeChunks;
        }

        /// <summary>设置当前使用的 PathProfile。</summary>
        public void SetProfile(Core.Scene.PathProfile profile)
        {
            _profile = profile;
        }

        /// <summary>
        /// 每帧调用。根据当前滚动距离从 PathProfile 采样速度，
        /// 移动所有活跃 Chunk。
        /// </summary>
        /// <param name="deltaTime">帧间隔时间。</param>
        /// <returns>本帧实际滚动的距离（米）。</returns>
        public float Tick(float deltaTime)
        {
            if (_profile == null) return 0f;

            var sample = _profile.SampleAt(TotalScrolled);
            CurrentSpeed = sample.Speed * SpeedMultiplier;
            float scrollDelta = CurrentSpeed * deltaTime;

            // 移动所有活跃 Chunk 沿 -Z 方向
            for (int i = 0; i < _activeChunks.Count; i++)
            {
                var chunk = _activeChunks[i];
                if (chunk.IsActive && chunk.Root != null)
                {
                    var pos = chunk.Root.transform.position;
                    pos.z -= scrollDelta;
                    chunk.Root.transform.position = pos;
                }
            }

            TotalScrolled += scrollDelta;
            return scrollDelta;
        }

        /// <summary>重置滚动状态。</summary>
        public void Reset()
        {
            TotalScrolled = 0f;
            CurrentSpeed = 0f;
            SpeedMultiplier = 1f;
        }
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/ScrollController.cs
git commit -m "feat(scene): add ScrollController for 3D scroll scene movement"
```

---

<!-- SPLICE_TASK6 -->

### Task 6: ChunkGenerator 分块生成器

**Files:**
- Create: `Assets/STGEngine/Runtime/Scene/ChunkGenerator.cs`

- [ ] **Step 1: 创建 ChunkGenerator MonoBehaviour**

核心组件，管理 Chunk 的滑动窗口：前方生成、后方回收。

```csharp
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
                // 初始位置：从玩家后方开始排列
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

            // 驱动场景流动
            _scroll.Tick(Time.deltaTime);

            // 检查是否需要回收后方 Chunk 并在前方生成新 Chunk
            RecycleAndSpawn();
        }

        /// <summary>
        /// 回收已移过玩家后方的 Chunk，在前方生成新 Chunk。
        /// </summary>
        private void RecycleAndSpawn()
        {
            // 回收：最后方的 Chunk 如果完全移过回收线，则回收
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

            // 生成：确保前方有足够的 Chunk
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

        /// <summary>创建一个新 Chunk（从池中取或新建）。</summary>
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

            // 生成地面 mesh
            if (_style.HasGround)
            {
                BuildGround(chunk);
            }

            return chunk;
        }

        /// <summary>为 Chunk 生成或更新地面 mesh。</summary>
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
        }

        /// <summary>回收 Chunk 到池中。</summary>
        private void RecycleChunk(Chunk chunk)
        {
            chunk.Deactivate();

            // 清理地面 mesh 避免内存泄漏
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
            // 清理所有 Chunk
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
        }
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/ChunkGenerator.cs
git commit -m "feat(scene): add ChunkGenerator with sliding window and ground mesh"
```

---

<!-- SPLICE_TASK7 -->

### Task 7: 集成测试场景

**Files:**
- Create: `Assets/Scenes/SceneSystemTest.unity`（通过 Unity Editor 创建）
- Create: `Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs`

- [ ] **Step 1: 创建测试引导脚本**

一个简单的 MonoBehaviour，在 Start 中初始化 ChunkGenerator，用于验证整个管线。

```csharp
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
            // 创建一个测试用 SceneStyle：
            // 通路从 20m 宽开始，在 200m 处展开到 60m（模拟 Boss 战场），然后收窄回 20m
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
                        (0, 0f), (50, 3f), (100, -2f), (150, 1f), (200, 0f), (1000, 0f)
                    ),
                    TotalLength = 1000f
                },
                HasGround = true
            };

            // 获取或添加 ChunkGenerator
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
            GUILayout.BeginArea(new Rect(10, 10, 300, 120));
            GUILayout.Label($"Scrolled: {scroll.TotalScrolled:F1}m");
            GUILayout.Label($"Speed: {scroll.CurrentSpeed:F1} m/s");
            GUILayout.Label($"Active Chunks: {_generator.ActiveChunks.Count}");
            GUILayout.Label($"Speed Multiplier: {_speedMultiplier:F2}");
            _speedMultiplier = GUILayout.HorizontalSlider(_speedMultiplier, 0f, 3f);
            GUILayout.EndArea();
        }
    }
}
```

- [ ] **Step 2: 验证编译**

在 Unity Editor 中确认无编译错误。

- [ ] **Step 3: 在 Unity 中创建测试场景**

1. Unity Editor → File → New Scene → Basic (Built-in)
2. 保存为 `Assets/Scenes/SceneSystemTest.unity`
3. 创建空 GameObject，命名为 `SceneManager`
4. 添加 `SceneTestSetup` 组件
5. （可选）创建一个简单的 Unlit 材质赋给 Ground Material 字段
6. 调整 Main Camera 位置到 `(0, 15, -10)`，旋转 `(45, 0, 0)` 俯视通路

- [ ] **Step 4: 运行测试**

点击 Play，预期效果：
- 看到多个地面 Chunk 沿 Z 轴排列
- 地面持续向 -Z 方向（向玩家）流动
- 在 ~200m 处地面明显变宽（Boss 战场展开）
- 在 ~320m 处地面收窄回正常宽度
- 地面有轻微的左右蜿蜒（DriftCurve）
- 左上角 GUI 显示滚动距离、速度、活跃 Chunk 数
- 滑块可以调整速度倍率（0 = 停止，3 = 三倍速）
- 后方 Chunk 被回收，前方持续生成新 Chunk

- [ ] **Step 5: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs
git commit -m "feat(scene): add SceneTestSetup for integration testing"
```

---

## 自审检查

- [x] **Spec 覆盖：** Task 1-2 覆盖 spec §2（Core 数据模型），Task 3-6 覆盖 spec §3.1（ChunkGenerator），Task 4 覆盖 spec §5.2（通路地面），Task 5 覆盖 spec §3.1 场景流动机制，连续性保证（spec §3.1 通路连续性）由 Chunk.LerpAt() Hermite 插值和 GroundMeshBuilder 的逐段采样实现。
- [x] **占位符扫描：** 无 TBD/TODO/placeholder。所有代码步骤都有完整代码块。
- [x] **类型一致性：** PathProfile.SampleAt() 返回 PathSample，Chunk.LerpAt() 也返回 PathSample，ChunkGenerator.CreateChunk() 调用 PathProfile.SampleAt()，ScrollController 引用 PathProfile——类型链一致。
- [x] **Phase 1 不含的内容（后续 Phase）：** 障碍物散布、软边界、危险障碍物、镜头系统、敌人联动、环境层（光照/粒子/音效）、编辑器集成。这些都不在本计划范围内。
