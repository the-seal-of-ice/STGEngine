# CameraScript 演出镜头系统实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 STGEngine 添加 ActionEvent 驱动的脚本化演出镜头，支持关键帧序列播放（位置/旋转/FOV/震动）和 blendIn/blendOut 过渡，通过 ICameraFrameProvider 桥接编辑器与程序化场景的坐标差异。

**Architecture:** 独立覆盖层方案 — CameraScriptPlayer 作为独立 MonoBehaviour，激活时在 LateUpdate 中接管 Camera.main，不修改现有三套相机控制器。关键帧偏移定义在玩家局部坐标系 (right/up/forward)，通过 ICameraFrameProvider 接口转换为世界坐标。

**Tech Stack:** Unity 2022+ / C# / URP

**Spec:** `doc/specs/2026-07-04-camera-script-design.md`

---

## 文件结构

### 新建文件

| 文件 | 职责 |
|------|------|
| `Assets/STGEngine/Core/Timeline/EasingType.cs` | 缓动类型枚举 |
| `Assets/STGEngine/Core/Scene/CameraKeyframe.cs` | 相机关键帧数据 |
| `Assets/STGEngine/Core/Scene/CameraShakePreset.cs` | 震动预设数据 |
| `Assets/STGEngine/Core/Scene/ICameraFrameProvider.cs` | 坐标标架接口 |
| `Assets/STGEngine/Core/Timeline/ActionParams/CameraScriptParams.cs` | CameraScript 事件参数 |
| `Assets/STGEngine/Core/Timeline/ActionParams/CameraShakeParams.cs` | CameraShake 事件参数 |
| `Assets/STGEngine/Runtime/Scene/CameraScriptPlayer.cs` | 演出镜头播放器（核心） |
| `Assets/STGEngine/Runtime/Scene/SplineCameraFrame.cs` | 程序化场景坐标标架 |
| `Assets/STGEngine/Runtime/Preview/EditorCameraFrame.cs` | 编辑器坐标标架 |

### 修改文件

| 文件 | 改动 |
|------|------|
| `Assets/STGEngine/Core/Timeline/ActionType.cs` | 新增 `CameraScript`, `CameraShake` |
| `Assets/STGEngine/Core/Timeline/ActionParams/ActionParamsRegistry.cs` | 注册两个新映射 |
| `Assets/STGEngine/Runtime/Preview/ActionEventPreviewController.cs` | 添加 CameraScript/CameraShake 处理 |

---

## Task 1: 数据模型 — EasingType + CameraKeyframe + CameraShakePreset

**Files:**
- Create: `Assets/STGEngine/Core/Timeline/EasingType.cs`
- Create: `Assets/STGEngine/Core/Scene/CameraKeyframe.cs`
- Create: `Assets/STGEngine/Core/Scene/CameraShakePreset.cs`

- [ ] **Step 1: 创建 EasingType 枚举**

```csharp
// Assets/STGEngine/Core/Timeline/EasingType.cs
namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// 关键帧之间的缓动类型。
    /// </summary>
    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut
    }
}
```

- [ ] **Step 2: 创建 CameraKeyframe 数据类**

```csharp
// Assets/STGEngine/Core/Scene/CameraKeyframe.cs
using System;
using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 相机演出关键帧。位置偏移定义在玩家局部坐标系 (x=right, y=up, z=forward)，
    /// 运行时由 ICameraFrameProvider 转换为世界坐标。
    /// </summary>
    [Serializable]
    public class CameraKeyframe
    {
        /// <summary>相对于演出开始的时间（秒）。</summary>
        public float Time;

        /// <summary>玩家局部坐标系偏移 (x=right, y=up, z=forward)。</summary>
        public Vector3 PositionOffset;

        /// <summary>局部空间欧拉角 (pitch, yaw, roll)。</summary>
        public Vector3 Rotation;

        /// <summary>视野角度。</summary>
        public float FOV = 60f;

        /// <summary>到下一帧的缓动类型。</summary>
        public EasingType Easing = EasingType.EaseInOut;
    }
}
```

- [ ] **Step 3: 创建 CameraShakePreset 数据类**

```csharp
// Assets/STGEngine/Core/Scene/CameraShakePreset.cs
using System;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 镜头震动预设参数。
    /// </summary>
    [Serializable]
    public class CameraShakePreset
    {
        /// <summary>震动持续时间（秒）。</summary>
        public float Duration = 0.5f;

        /// <summary>最大振幅（米）。</summary>
        public float Amplitude = 0.3f;

        /// <summary>频率（Hz）。</summary>
        public float Frequency = 25f;

        /// <summary>衰减速率（1 = 线性衰减到 0）。</summary>
        public float DecayRate = 1f;
    }
}
```

- [ ] **Step 4: 提交**

```bash
git add Assets/STGEngine/Core/Timeline/EasingType.cs Assets/STGEngine/Core/Scene/CameraKeyframe.cs Assets/STGEngine/Core/Scene/CameraShakePreset.cs
git commit -m "feat(camera): add EasingType, CameraKeyframe, CameraShakePreset data models"
```

---

## Task 2: ActionType 扩展 + ActionParams 注册

**Files:**
- Modify: `Assets/STGEngine/Core/Timeline/ActionType.cs`
- Create: `Assets/STGEngine/Core/Timeline/ActionParams/CameraScriptParams.cs`
- Create: `Assets/STGEngine/Core/Timeline/ActionParams/CameraShakeParams.cs`
- Modify: `Assets/STGEngine/Core/Timeline/ActionParams/ActionParamsRegistry.cs`

- [ ] **Step 1: 在 ActionType 枚举中新增两个值**

在 `Assets/STGEngine/Core/Timeline/ActionType.cs` 的 Presentation 组末尾（`BackgroundSwitch` 之后）添加：

```csharp
        // ── Presentation ──
        ShowTitle,
        ScreenEffect,
        BgmControl,
        SePlay,
        BackgroundSwitch,
        CameraScript,
        CameraShake,
```

- [ ] **Step 2: 创建 CameraScriptParams**

```csharp
// Assets/STGEngine/Core/Timeline/ActionParams/CameraScriptParams.cs
using System.Collections.Generic;
using STGEngine.Core.Scene;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// CameraScript ActionEvent 的参数：关键帧序列 + blend 时长。
    /// </summary>
    public class CameraScriptParams : IActionParams
    {
        /// <summary>关键帧列表（按 Time 升序）。</summary>
        public List<CameraKeyframe> Keyframes { get; set; } = new();

        /// <summary>从当前相机状态过渡到第一帧的时长（秒）。</summary>
        public float BlendIn { get; set; } = 0.5f;

        /// <summary>从最后一帧过渡回原相机的时长（秒）。</summary>
        public float BlendOut { get; set; } = 0.5f;
    }
}
```

- [ ] **Step 3: 创建 CameraShakeParams**

```csharp
// Assets/STGEngine/Core/Timeline/ActionParams/CameraShakeParams.cs
using STGEngine.Core.Scene;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// CameraShake ActionEvent 的参数。
    /// </summary>
    public class CameraShakeParams : IActionParams
    {
        public CameraShakePreset Preset { get; set; } = new();
    }
}
```

- [ ] **Step 4: 在 ActionParamsRegistry 中注册映射**

在 `Assets/STGEngine/Core/Timeline/ActionParams/ActionParamsRegistry.cs` 的 `_map` 字典中，在 `BranchJump` 行之后添加：

```csharp
            { ActionType.CameraScript, typeof(CameraScriptParams) },
            { ActionType.CameraShake,  typeof(CameraShakeParams) },
```

同时在文件顶部确认已有 `using STGEngine.Core.Scene;`（如果没有则添加）。注意：`ActionParamsRegistry` 不需要 using，因为 `CameraScriptParams` 和 `CameraShakeParams` 在同一个 namespace `STGEngine.Core.Timeline` 中。

- [ ] **Step 5: 提交**

```bash
git add Assets/STGEngine/Core/Timeline/ActionType.cs Assets/STGEngine/Core/Timeline/ActionParams/CameraScriptParams.cs Assets/STGEngine/Core/Timeline/ActionParams/CameraShakeParams.cs Assets/STGEngine/Core/Timeline/ActionParams/ActionParamsRegistry.cs
git commit -m "feat(camera): add CameraScript/CameraShake ActionType + params"
```

---

## Task 3: ICameraFrameProvider 接口 + 两套实现

**Files:**
- Create: `Assets/STGEngine/Core/Scene/ICameraFrameProvider.cs`
- Create: `Assets/STGEngine/Runtime/Preview/EditorCameraFrame.cs`
- Create: `Assets/STGEngine/Runtime/Scene/SplineCameraFrame.cs`

- [ ] **Step 1: 创建 ICameraFrameProvider 接口**

```csharp
// Assets/STGEngine/Core/Scene/ICameraFrameProvider.cs
using UnityEngine;

namespace STGEngine.Core.Scene
{
    /// <summary>
    /// 为 CameraScriptPlayer 提供玩家位置和局部坐标标架。
    /// 关键帧的 PositionOffset (right, up, forward) 通过此标架转换为世界坐标。
    /// </summary>
    public interface ICameraFrameProvider
    {
        /// <summary>玩家当前世界位置（偏移基准点）。</summary>
        Vector3 PlayerWorldPosition { get; }

        /// <summary>局部坐标系 Right 方向（世界空间单位向量）。</summary>
        Vector3 FrameRight { get; }

        /// <summary>局部坐标系 Up 方向（世界空间单位向量）。</summary>
        Vector3 FrameUp { get; }

        /// <summary>局部坐标系 Forward 方向（世界空间单位向量）。</summary>
        Vector3 FrameForward { get; }
    }
}
```

- [ ] **Step 2: 创建 EditorCameraFrame**

编辑器环境的坐标标架实现。非 Player 模式时用 FreeCameraController 的 pivot 作为基准，Player 模式时用 IPlayerProvider 的位置。标架方向使用固定世界轴（编辑器中通路不弯曲）。

```csharp
// Assets/STGEngine/Runtime/Preview/EditorCameraFrame.cs
using UnityEngine;
using STGEngine.Core.Scene;
using STGEngine.Runtime.Player;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// 编辑器环境的相机坐标标架。
    /// 使用固定世界轴方向（编辑器中无样条线弯曲）。
    /// </summary>
    public class EditorCameraFrame : ICameraFrameProvider
    {
        private readonly FreeCameraController _freeCam;
        private IPlayerProvider _player;

        public EditorCameraFrame(FreeCameraController freeCam)
        {
            _freeCam = freeCam;
        }

        /// <summary>设置活跃玩家（Player 模式进入时调用，退出时传 null）。</summary>
        public void SetPlayer(IPlayerProvider player) => _player = player;

        public Vector3 PlayerWorldPosition =>
            _player != null && _player.IsActive
                ? _player.Position
                : (_freeCam != null ? _freeCam.Pivot : Vector3.zero);

        public Vector3 FrameRight => Vector3.right;
        public Vector3 FrameUp => Vector3.up;
        public Vector3 FrameForward => Vector3.forward;
    }
}
```

注意：`FreeCameraController._pivot` 是 private 字段。需要在 `FreeCameraController` 中添加一个公共只读属性：

在 `Assets/STGEngine/Runtime/Preview/FreeCameraController.cs` 的 `ShakeOffset` 属性附近添加：

```csharp
        /// <summary>轨道中心点（世界坐标）。</summary>
        public Vector3 Pivot => _pivot;
```

- [ ] **Step 3: 创建 SplineCameraFrame**

程序化场景的坐标标架实现。从 PlayerAnchorController 获取位置，从样条线 PathSample 获取 Frenet 标架。

```csharp
// Assets/STGEngine/Runtime/Scene/SplineCameraFrame.cs
using UnityEngine;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 程序化场景的相机坐标标架。
    /// 使用样条线 Frenet 标架（Tangent/Normal/Up），在弯道处自然旋转。
    /// </summary>
    public class SplineCameraFrame : ICameraFrameProvider
    {
        private readonly PlayerAnchorController _anchor;

        public SplineCameraFrame(PlayerAnchorController anchor)
        {
            _anchor = anchor;
        }

        public Vector3 PlayerWorldPosition => _anchor.WorldPosition;

        /// <summary>Right = 样条线法线方向（通路右侧）。</summary>
        public Vector3 FrameRight => _anchor.CurrentAnchor.Normal;

        public Vector3 FrameUp => Vector3.up;

        /// <summary>Forward = 样条线切线方向（通路前进方向）。</summary>
        public Vector3 FrameForward => _anchor.CurrentAnchor.Tangent;
    }
}
```

- [ ] **Step 4: 提交**

```bash
git add Assets/STGEngine/Core/Scene/ICameraFrameProvider.cs Assets/STGEngine/Runtime/Preview/EditorCameraFrame.cs Assets/STGEngine/Runtime/Scene/SplineCameraFrame.cs Assets/STGEngine/Runtime/Preview/FreeCameraController.cs
git commit -m "feat(camera): add ICameraFrameProvider interface + Editor/Spline implementations"
```

---

## Task 4: CameraScriptPlayer 核心组件

**Files:**
- Create: `Assets/STGEngine/Runtime/Scene/CameraScriptPlayer.cs`

这是整个系统的核心。状态机 `Idle → BlendIn → Playing → BlendOut → Idle`，LateUpdate 中接管相机。

- [ ] **Step 1: 创建 CameraScriptPlayer**

```csharp
// Assets/STGEngine/Runtime/Scene/CameraScriptPlayer.cs
using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Scene;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 演出镜头播放器。通过 ActionEvent 驱动，播放关键帧序列并接管相机。
    /// 支持 blendIn/blendOut 平滑过渡和独立的镜头震动。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/CameraScriptPlayer")]
    public class CameraScriptPlayer : MonoBehaviour
    {
        private enum State { Idle, BlendIn, Playing, BlendOut }

        private State _state = State.Idle;
        private ICameraFrameProvider _frameProvider;
        private Camera _camera;

        // 当前演出数据
        private CameraScriptParams _params;
        private float _elapsed;
        private float _scriptDuration; // 最后一帧的 Time

        // BlendIn/Out 快照
        private Vector3 _snapshotPos;
        private Quaternion _snapshotRot;
        private float _snapshotFov;

        // 被接管的相机控制器
        private MonoBehaviour _disabledController;

        // 震动状态（可独立于关键帧演出）
        private readonly List<ActiveShake> _activeShakes = new();

        /// <summary>当前是否在演出中（BlendIn/Playing/BlendOut 都算）。</summary>
        public bool IsActive => _state != State.Idle;

        /// <summary>初始化，注入坐标系提供者。</summary>
        public void Initialize(ICameraFrameProvider frameProvider)
        {
            _frameProvider = frameProvider;
            _camera = Camera.main;
        }

        /// <summary>开始播放关键帧序列。</summary>
        public void Play(CameraScriptParams scriptParams)
        {
            if (scriptParams == null || scriptParams.Keyframes.Count == 0) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            _params = scriptParams;
            _elapsed = 0f;
            _scriptDuration = scriptParams.Keyframes[scriptParams.Keyframes.Count - 1].Time;

            // 快照当前相机状态
            _snapshotPos = _camera.transform.position;
            _snapshotRot = _camera.transform.rotation;
            _snapshotFov = _camera.fieldOfView;

            // 禁用当前活跃的相机控制器
            DisableActiveController();

            _state = scriptParams.BlendIn > 0f ? State.BlendIn : State.Playing;
        }

        /// <summary>触发镜头震动（可与关键帧演出叠加，也可独立使用）。</summary>
        public void Shake(CameraShakePreset preset)
        {
            if (preset == null || preset.Duration <= 0f) return;
            _activeShakes.Add(new ActiveShake
            {
                Preset = preset,
                Elapsed = 0f,
                Seed = Random.value * 1000f
            });
        }

        /// <summary>立即停止演出，跳到 Idle。</summary>
        public void Stop()
        {
            if (_state != State.Idle)
            {
                RestoreController();
                _state = State.Idle;
            }
            _params = null;
        }

        private void LateUpdate()
        {
            UpdateShakes();

            if (_state == State.Idle)
            {
                // 即使 Idle，如果有活跃震动也要叠加到相机上
                if (_activeShakes.Count > 0 && _camera != null)
                {
                    _camera.transform.position += ComputeShakeOffset();
                }
                return;
            }

            if (_camera == null || _frameProvider == null) return;

            _elapsed += Time.deltaTime;

            Vector3 targetPos;
            Quaternion targetRot;
            float targetFov;

            switch (_state)
            {
                case State.BlendIn:
                {
                    float t = Mathf.Clamp01(_elapsed / _params.BlendIn);
                    var firstFrame = EvaluateKeyframes(0f);
                    var worldTarget = LocalToWorld(firstFrame.pos, firstFrame.rot);
                    targetPos = Vector3.Lerp(_snapshotPos, worldTarget.pos, SmoothStep(t));
                    targetRot = Quaternion.Slerp(_snapshotRot, worldTarget.rot, SmoothStep(t));
                    targetFov = Mathf.Lerp(_snapshotFov, firstFrame.fov, SmoothStep(t));

                    if (_elapsed >= _params.BlendIn)
                    {
                        _elapsed -= _params.BlendIn;
                        _state = State.Playing;
                    }
                    break;
                }

                case State.Playing:
                {
                    var frame = EvaluateKeyframes(_elapsed);
                    var world = LocalToWorld(frame.pos, frame.rot);
                    targetPos = world.pos;
                    targetRot = world.rot;
                    targetFov = frame.fov;

                    if (_elapsed >= _scriptDuration)
                    {
                        if (_params.BlendOut > 0f)
                        {
                            // 快照当前演出状态作为 BlendOut 起点
                            _snapshotPos = targetPos;
                            _snapshotRot = targetRot;
                            _snapshotFov = targetFov;
                            RestoreController();
                            _elapsed = 0f;
                            _state = State.BlendOut;
                        }
                        else
                        {
                            RestoreController();
                            _state = State.Idle;
                            _params = null;
                        }
                    }
                    break;
                }

                case State.BlendOut:
                {
                    float t = Mathf.Clamp01(_elapsed / _params.BlendOut);
                    // 目标：原控制器此刻应该产生的相机状态（它已被重新启用）
                    Vector3 restorePos = _camera.transform.position;
                    Quaternion restoreRot = _camera.transform.rotation;
                    float restoreFov = _camera.fieldOfView;

                    targetPos = Vector3.Lerp(_snapshotPos, restorePos, SmoothStep(t));
                    targetRot = Quaternion.Slerp(_snapshotRot, restoreRot, SmoothStep(t));
                    targetFov = Mathf.Lerp(_snapshotFov, restoreFov, SmoothStep(t));

                    if (_elapsed >= _params.BlendOut)
                    {
                        _state = State.Idle;
                        _params = null;
                        return; // 不再覆写相机
                    }
                    break;
                }

                default:
                    return;
            }

            // 叠加震动
            targetPos += ComputeShakeOffset();

            // 写入相机
            _camera.transform.position = targetPos;
            _camera.transform.rotation = targetRot;
            _camera.fieldOfView = targetFov;
        }

        // ── 关键帧插值 ──

        private (Vector3 pos, Vector3 rot, float fov) EvaluateKeyframes(float time)
        {
            var kfs = _params.Keyframes;
            if (kfs.Count == 1 || time <= kfs[0].Time)
                return (kfs[0].PositionOffset, kfs[0].Rotation, kfs[0].FOV);

            if (time >= kfs[kfs.Count - 1].Time)
            {
                var last = kfs[kfs.Count - 1];
                return (last.PositionOffset, last.Rotation, last.FOV);
            }

            // 找到当前区间
            for (int i = 0; i < kfs.Count - 1; i++)
            {
                if (time >= kfs[i].Time && time < kfs[i + 1].Time)
                {
                    float segLen = kfs[i + 1].Time - kfs[i].Time;
                    float localT = (time - kfs[i].Time) / segLen;
                    float easedT = ApplyEasing(localT, kfs[i].Easing);

                    Vector3 pos = Vector3.Lerp(kfs[i].PositionOffset, kfs[i + 1].PositionOffset, easedT);
                    Vector3 rot = LerpEuler(kfs[i].Rotation, kfs[i + 1].Rotation, easedT);
                    float fov = Mathf.Lerp(kfs[i].FOV, kfs[i + 1].FOV, easedT);
                    return (pos, rot, fov);
                }
            }

            var fallback = kfs[kfs.Count - 1];
            return (fallback.PositionOffset, fallback.Rotation, fallback.FOV);
        }

        private static Vector3 LerpEuler(Vector3 a, Vector3 b, float t)
        {
            // 通过四元数插值避免万向锁
            Quaternion qa = Quaternion.Euler(a);
            Quaternion qb = Quaternion.Euler(b);
            return Quaternion.Slerp(qa, qb, t).eulerAngles;
        }

        // ── 局部 → 世界坐标转换 ──

        private (Vector3 pos, Quaternion rot) LocalToWorld(Vector3 localOffset, Vector3 localEuler)
        {
            Vector3 origin = _frameProvider.PlayerWorldPosition;
            Vector3 right = _frameProvider.FrameRight;
            Vector3 up = _frameProvider.FrameUp;
            Vector3 forward = _frameProvider.FrameForward;

            Vector3 worldPos = origin
                + right   * localOffset.x
                + up      * localOffset.y
                + forward * localOffset.z;

            // 构建标架旋转矩阵，再叠加局部旋转
            Quaternion frameRot = Quaternion.LookRotation(forward, up);
            Quaternion localRot = Quaternion.Euler(localEuler);
            Quaternion worldRot = frameRot * localRot;

            return (worldPos, worldRot);
        }

        // ── 缓动函数 ──

        private static float ApplyEasing(float t, EasingType easing)
        {
            switch (easing)
            {
                case EasingType.EaseIn:    return t * t;
                case EasingType.EaseOut:   return 1f - (1f - t) * (1f - t);
                case EasingType.EaseInOut: return SmoothStep(t);
                case EasingType.Linear:
                default:                   return t;
            }
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        // ── 震动 ──

        private void UpdateShakes()
        {
            for (int i = _activeShakes.Count - 1; i >= 0; i--)
            {
                _activeShakes[i] = new ActiveShake
                {
                    Preset = _activeShakes[i].Preset,
                    Elapsed = _activeShakes[i].Elapsed + Time.deltaTime,
                    Seed = _activeShakes[i].Seed
                };
                if (_activeShakes[i].Elapsed >= _activeShakes[i].Preset.Duration)
                    _activeShakes.RemoveAt(i);
            }
        }

        private Vector3 ComputeShakeOffset()
        {
            Vector3 offset = Vector3.zero;
            foreach (var shake in _activeShakes)
            {
                float progress = shake.Elapsed / shake.Preset.Duration;
                float decay = Mathf.Pow(1f - progress, shake.Preset.DecayRate);
                float amp = shake.Preset.Amplitude * decay;
                float freq = shake.Preset.Frequency;
                float t = shake.Elapsed;

                offset += new Vector3(
                    (Mathf.PerlinNoise(t * freq, shake.Seed) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(shake.Seed, t * freq) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(t * freq + shake.Seed, t * freq) - 0.5f) * 2f
                ) * amp;
            }
            return offset;
        }

        // ── 相机控制器管理 ──

        private void DisableActiveController()
        {
            if (_camera == null) return;

            // 按优先级检查：FreeCameraController > PlayerCamera > ChunkGenerator
            var freeCam = _camera.GetComponent<Preview.FreeCameraController>();
            if (freeCam != null && freeCam.enabled)
            {
                freeCam.enabled = false;
                _disabledController = freeCam;
                return;
            }

            var playerCam = _camera.GetComponent<Player.PlayerCamera>();
            if (playerCam != null && playerCam.enabled)
            {
                playerCam.enabled = false;
                _disabledController = playerCam;
                return;
            }

            var chunkGen = FindAnyObjectByType<ChunkGenerator>();
            if (chunkGen != null && chunkGen.enabled)
            {
                chunkGen.enabled = false;
                _disabledController = chunkGen;
            }
        }

        private void RestoreController()
        {
            if (_disabledController != null)
            {
                _disabledController.enabled = true;
                _disabledController = null;
            }
        }

        // ── 内部数据 ──

        private struct ActiveShake
        {
            public CameraShakePreset Preset;
            public float Elapsed;
            public float Seed;
        }
    }
}
```

注意 `using` 中引用了 `STGEngine.Runtime.Preview` 和 `STGEngine.Runtime.Player` 命名空间（用于 `FreeCameraController` 和 `PlayerCamera` 类型检查）。由于这些是同一程序集内的引用，需要确认 `PlayerCamera` 的完整命名空间路径。在 `DisableActiveController` 中使用了完全限定名 `Preview.FreeCameraController` 和 `Player.PlayerCamera` 来避免歧义。如果编译报错，改为在文件顶部添加：

```csharp
using STGEngine.Runtime.Preview;
using STGEngine.Runtime.Player;
```

并将方法内的类型引用简化。

- [ ] **Step 2: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/CameraScriptPlayer.cs
git commit -m "feat(camera): add CameraScriptPlayer with keyframe playback, blend, and shake"
```

---

## Task 5: ActionEventPreviewController 集成

**Files:**
- Modify: `Assets/STGEngine/Runtime/Preview/ActionEventPreviewController.cs`

在现有的 Tick() 方法中添加 CameraScript 和 CameraShake 的处理逻辑。

- [ ] **Step 1: 添加字段声明**

在 `Assets/STGEngine/Runtime/Preview/ActionEventPreviewController.cs` 的字段声明区域（约 L70 附近，`_triggeredAudioIds` 之后）添加：

```csharp
        // ── Camera Script ──
        private CameraScriptPlayer _cameraScriptPlayer;
        private readonly HashSet<string> _triggeredCameraIds = new();
```

在文件顶部添加 using（如果没有）：

```csharp
using STGEngine.Runtime.Scene;
using STGEngine.Core.Scene;
```

- [ ] **Step 2: 初始化 CameraScriptPlayer**

在构造函数中（约 L77–85），在获取 `_freeCam` 之后添加 CameraScriptPlayer 的初始化：

```csharp
            // Camera script player
            if (camera != null)
            {
                _cameraScriptPlayer = camera.GetComponent<CameraScriptPlayer>();
                if (_cameraScriptPlayer == null)
                    _cameraScriptPlayer = camera.gameObject.AddComponent<CameraScriptPlayer>();
                var editorFrame = new EditorCameraFrame(_freeCam);
                _cameraScriptPlayer.Initialize(editorFrame);
            }
```

- [ ] **Step 3: 在 Tick() 的 Seek 回退逻辑中添加 CameraScript 处理**

在 Seek 回退检测块中（约 L122–173），在最后一个 `RemoveWhere` 块之后、音频停止之前添加：

```csharp
            // Camera script seek reset
            _triggeredCameraIds.RemoveWhere(id =>
            {
                foreach (var evt in _segment.Events)
                {
                    if (evt is ActionEvent ae && ae.Id == id && ae.StartTime >= currentTime)
                        return true;
                }
                return false;
            });
            if (_cameraScriptPlayer != null && _cameraScriptPlayer.IsActive)
                _cameraScriptPlayer.Stop();
```

- [ ] **Step 4: 在 Tick() 的第二层 switch（fire-once 事件）中添加 CameraShake**

在第二层 switch 中（约 L241–283），在 `AutoCollect` case 之后添加：

```csharp
                    case ActionType.CameraShake:
                        if (!_triggeredCameraIds.Contains(ae.Id))
                        {
                            _triggeredCameraIds.Add(ae.Id);
                            if (ae.Params is CameraShakeParams cshk && _cameraScriptPlayer != null)
                                _cameraScriptPlayer.Shake(cshk.Preset);
                        }
                        break;
```

- [ ] **Step 5: 在 Tick() 的第三层 switch（range-based 视觉效果）中添加 CameraScript**

在第三层 switch 中（约 L289–329），在 `ScoreTally` case 之后添加：

```csharp
                    case ActionType.CameraScript:
                        if (ae.Params is CameraScriptParams csp
                            && _cameraScriptPlayer != null
                            && !_cameraScriptPlayer.IsActive)
                        {
                            _cameraScriptPlayer.Play(csp);
                        }
                        break;
```

- [ ] **Step 6: 在 Reset() 方法中添加清理**

在 `Reset()` 方法中（约 L440–466），在 `_triggeredAudioIds.Clear()` 附近添加：

```csharp
            _triggeredCameraIds.Clear();
            if (_cameraScriptPlayer != null && _cameraScriptPlayer.IsActive)
                _cameraScriptPlayer.Stop();
```

- [ ] **Step 7: 提交**

```bash
git add Assets/STGEngine/Runtime/Preview/ActionEventPreviewController.cs
git commit -m "feat(camera): integrate CameraScript/CameraShake into ActionEventPreviewController"
```

---

## Task 6: 冒烟测试 — SceneTestSetup 集成

**Files:**
- Modify: `Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs`

在现有的场景测试引导中添加 CameraScriptPlayer 初始化，验证整个管线在程序化场景中可运行。

- [ ] **Step 1: 在 SceneTestSetup 中初始化 CameraScriptPlayer**

在 `Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs` 中，找到创建 `PlayerAnchorController` 的位置，在其之后添加 CameraScriptPlayer 的创建和初始化：

```csharp
            // Camera script player (for scene mode)
            var cameraScriptPlayer = Camera.main.gameObject.AddComponent<CameraScriptPlayer>();
            var splineFrame = new SplineCameraFrame(playerAnchor);
            cameraScriptPlayer.Initialize(splineFrame);
```

其中 `playerAnchor` 是已创建的 `PlayerAnchorController` 实例。根据 SceneTestSetup 中的实际变量名调整。

- [ ] **Step 2: 添加测试用的镜头演出触发**

在 SceneTestSetup 的 Update 中添加一个临时测试快捷键（按 C 触发测试演出）：

```csharp
            // 临时测试：按 C 触发镜头演出
            if (Input.GetKeyDown(KeyCode.C))
            {
                var csp = FindAnyObjectByType<CameraScriptPlayer>();
                if (csp != null && !csp.IsActive)
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
                    csp.Play(testParams);
                }
            }

            // 临时测试：按 V 触发镜头震动
            if (Input.GetKeyDown(KeyCode.V))
            {
                var csp = FindAnyObjectByType<CameraScriptPlayer>();
                if (csp != null)
                {
                    csp.Shake(new CameraShakePreset
                    {
                        Duration = 0.5f,
                        Amplitude = 0.5f,
                        Frequency = 30f,
                        DecayRate = 1.5f
                    });
                }
            }
```

需要在文件顶部添加 using：

```csharp
using STGEngine.Core.Scene;
using STGEngine.Core.Timeline;
```

- [ ] **Step 3: 手动测试验证**

在 Unity 中运行 SceneTestSetup 场景：

1. 场景正常加载，玩家沿样条线前进 → 基础功能不受影响
2. 按 C → 镜头平滑过渡到演出位置，3 秒关键帧序列播放，然后平滑回到跟随镜头
3. 按 V → 镜头短暂震动后恢复
4. 在弯道处按 C → 镜头偏移方向跟随样条线标架旋转（验证 SplineCameraFrame）
5. 演出期间 WASD 移动玩家 → 镜头偏移基准跟随玩家移动

- [ ] **Step 4: 提交**

```bash
git add Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs
git commit -m "feat(camera): add CameraScriptPlayer to SceneTestSetup with test triggers"
```
