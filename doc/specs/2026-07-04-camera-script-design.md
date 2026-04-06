# CameraScript 演出镜头系统设计

> 日期：2026-07-04
> 状态：设计稿
> 前置：Phase 1-3 场景系统已完成

## 概述

为 STGEngine 编辑器 Timeline 系统添加脚本化演出镜头能力。通过 ActionEvent 驱动，支持关键帧序列播放（位置/旋转/FOV）和镜头震动，带 blendIn/blendOut 平滑过渡。

DynamicCamera（FOV 响应、速度感等）已废弃，不在本设计范围内。

## 设计决策

- **触发方式**：纯 ActionEvent 驱动，不提供运行时 API 直接调用
- **架构**：独立覆盖层，激活时接管相机，不修改现有三套相机控制器代码
- **位置坐标系**：关键帧位置为玩家局部坐标系偏移 (right, up, forward)，运行时根据环境自动转换为世界坐标
- **坐标桥接**：通过 `ICameraFrameProvider` 接口统一编辑器和程序化场景的坐标系转换
- **blend 方式**：每个 CameraScript ActionEvent 自带 blendIn/blendOut 时长参数

## 数据模型

### EasingType 枚举

```
Core/Timeline/EasingType.cs（新建，如果不存在）
```

```csharp
public enum EasingType
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut
}
```

### CameraKeyframe

```
Core/Scene/CameraKeyframe.cs（新建）
```

```csharp
[Serializable]
public class CameraKeyframe
{
    public float Time;              // 相对于演出开始的秒数
    public Vector3 PositionOffset;  // 玩家局部坐标系偏移 (x=right, y=up, z=forward)
    public Vector3 Rotation;        // 欧拉角 (pitch, yaw, roll)，局部空间
    public float FOV = 60f;         // 视野角度
    public EasingType Easing = EasingType.EaseInOut;
}
```

关键帧按 `Time` 升序排列。第一帧 `Time` 通常为 0。插值在相邻帧之间进行，超出最后一帧后保持最后一帧的值。

`PositionOffset` 语义：`(x=right, y=up, z=forward)` 相对于玩家当前朝向的局部偏移。运行时由 `ICameraFrameProvider` 转换为世界坐标。`Rotation` 同理，是相对于玩家局部标架的旋转。

### CameraShakePreset

```
Core/Scene/CameraShakePreset.cs（新建）
```

```csharp
[Serializable]
public class CameraShakePreset
{
    public float Duration = 0.5f;   // 震动持续秒数
    public float Amplitude = 0.3f;  // 最大振幅（米）
    public float Frequency = 25f;   // 频率（Hz）
    public float DecayRate = 1f;    // 衰减速率（1 = 线性衰减到 0）
}
```

### ActionType 扩展

在 `ActionType` 枚举的 Presentation 组中新增：

```csharp
CameraScript,   // 播放关键帧序列
CameraShake,    // 触发镜头震动
```

### CameraScriptParams

```
Core/Timeline/ActionParams/CameraScriptParams.cs（新建）
```

```csharp
public class CameraScriptParams : IActionParams
{
    public List<CameraKeyframe> Keyframes { get; set; } = new();
    public float BlendIn { get; set; } = 0.5f;   // 过渡进入时长（秒）
    public float BlendOut { get; set; } = 0.5f;   // 过渡退出时长（秒）
}
```

`ActionEvent.Duration` 由关键帧序列的最后一帧 Time + BlendOut 自动计算，或由用户手动设置（取较大值）。

### CameraShakeParams

```
Core/Timeline/ActionParams/CameraShakeParams.cs（新建）
```

```csharp
public class CameraShakeParams : IActionParams
{
    public CameraShakePreset Preset { get; set; } = new();
}
```

### ActionParamsRegistry 注册

```csharp
{ ActionType.CameraScript, typeof(CameraScriptParams) }
{ ActionType.CameraShake,  typeof(CameraShakeParams)  }
```

## 坐标桥接：ICameraFrameProvider

### 问题

编辑器和程序化场景的玩家坐标系完全不同：

| 维度 | 编辑器沙盒 | 程序化场景 |
|------|-----------|-----------|
| 玩家组件 | `PlayerController` / `SimulatedPlayer` | `PlayerAnchorController` |
| 坐标系 | 世界坐标，原点为中心 | 样条线弧长 + 局部偏移 |
| 边界 | 80m AABB 立方体，硬 Clamp | 障碍物拟合曲线，软推力 + 硬限制 |
| 朝向标架 | 相机视角方向 / 固定 Z-forward | 样条线 Frenet 标架 (Tangent/Normal/Up) |

如果 CameraScript 的偏移直接用世界坐标，同一组关键帧在两个环境中的视觉效果会不一致（尤其在弯道处）。

### 解决方案

引入 `ICameraFrameProvider` 接口，提供"玩家位置 + 局部标架"，CameraScriptPlayer 通过它将关键帧的局部偏移转换为世界坐标。

```
Core/Scene/ICameraFrameProvider.cs（新建）
```

```csharp
/// <summary>
/// 为 CameraScriptPlayer 提供玩家位置和局部坐标标架。
/// 关键帧的 PositionOffset (right, up, forward) 通过此标架转换为世界坐标。
/// </summary>
public interface ICameraFrameProvider
{
    /// <summary>玩家当前世界位置（偏移基准点）。</summary>
    Vector3 PlayerWorldPosition { get; }

    /// <summary>玩家局部坐标系的 Right 方向（世界空间单位向量）。</summary>
    Vector3 FrameRight { get; }

    /// <summary>玩家局部坐标系的 Up 方向（世界空间单位向量）。</summary>
    Vector3 FrameUp { get; }

    /// <summary>玩家局部坐标系的 Forward 方向（世界空间单位向量）。</summary>
    Vector3 FrameForward { get; }
}
```

### 转换公式

CameraScriptPlayer 将关键帧偏移转换为世界坐标：

```csharp
Vector3 worldPos = provider.PlayerWorldPosition
    + provider.FrameRight   * offset.x
    + provider.FrameUp      * offset.y
    + provider.FrameForward * offset.z;
```

Rotation 同理：先构造局部旋转四元数，再乘以标架旋转得到世界旋转。

### 两套实现

#### EditorCameraFrame（编辑器环境）

```
Runtime/Preview/EditorCameraFrame.cs（新建）
```

- `PlayerWorldPosition`：从 `IPlayerProvider.Position` 获取（Player 模式），或从 `FreeCameraController._pivot` 获取（非 Player 模式）
- `FrameForward`：相机视角的水平投影方向，或固定 `Vector3.forward`
- `FrameRight`：`Vector3.Cross(Up, Forward)`
- `FrameUp`：`Vector3.up`

编辑器中标架基本是固定的世界轴方向，行为与之前的"世界坐标偏移"几乎一致。

#### SplineCameraFrame（程序化场景环境）

```
Runtime/Scene/SplineCameraFrame.cs（新建）
```

- `PlayerWorldPosition`：从 `PlayerAnchorController.WorldPosition` 获取
- `FrameForward`：`PlayerAnchorController.CurrentAnchor.Tangent`（样条线切线方向）
- `FrameRight`：`PlayerAnchorController.CurrentAnchor.Normal`（样条线法线方向）
- `FrameUp`：`Vector3.up`

在弯道处，标架随样条线自然旋转，关键帧偏移 `(5, 10, -8)` 始终表示"玩家右方 5m、上方 10m、后方 8m"，无论通路朝哪个方向。

### 注入方式

CameraScriptPlayer 持有 `ICameraFrameProvider` 引用，在初始化时注入：

- `ActionEventPreviewController` 创建 CameraScriptPlayer 时注入 `EditorCameraFrame`
- `SceneTestSetup` / `ChunkGenerator` 创建时注入 `SplineCameraFrame`

## 运行时组件

### CameraScriptPlayer

```
Runtime/Scene/CameraScriptPlayer.cs（新建）
```

独立 MonoBehaviour，挂载在场景中（由 ActionEventPreviewController 或 SceneTestSetup 创建/获取）。持有 `ICameraFrameProvider` 引用，用于将关键帧局部偏移转换为世界坐标。

#### 状态机

```
Idle → BlendIn → Playing → BlendOut → Idle
```

- **Idle**：不干预相机，其他控制器正常工作
- **BlendIn**：记录进入时的相机状态（position/rotation/fov）作为起点，在 blendIn 时长内从起点插值到第一帧目标
- **Playing**：完全由关键帧控制相机
- **BlendOut**：从当前关键帧状态插值回"原相机控制器应该产生的状态"，同时重新启用原控制器让它开始计算

#### 公共接口

```csharp
// 初始化，注入坐标系提供者
void Initialize(ICameraFrameProvider frameProvider);

// 开始播放关键帧序列
void Play(CameraScriptParams scriptParams);

// 触发震动（可与关键帧演出叠加，也可独立使用）
void Shake(CameraShakePreset preset);

// 立即停止演出，跳到 Idle
void Stop();

// 当前是否在演出中（BlendIn/Playing/BlendOut 都算）
bool IsActive { get; }
```

#### 每帧逻辑（LateUpdate）

1. 如果 `Idle`，直接返回
2. 推进内部时钟 `_elapsed += Time.deltaTime`
3. 根据状态计算目标相机状态：
   - **BlendIn**：`t = _elapsed / blendIn`，Lerp(进入时快照, 第一帧目标, t)
   - **Playing**：在关键帧列表中找到当前时间所在的区间，按 Easing 插值得到局部偏移，通过 `ICameraFrameProvider` 转换为世界坐标
   - **BlendOut**：`t = (_elapsed - blendOutStart) / blendOut`，Lerp(当前关键帧状态, 原控制器状态, t)
4. 叠加 shake 偏移（如果有活跃震动）
5. 写入 `Camera.main.transform.position/rotation` 和 `Camera.main.fieldOfView`
6. 状态转换检查：BlendIn 结束 → Playing，Playing 到最后一帧 → BlendOut，BlendOut 结束 → Idle

#### 关键帧插值

- Position：Vector3.Lerp（应用 easing 后的 t）
- Rotation：Quaternion.Slerp（从欧拉角构造四元数）
- FOV：Mathf.Lerp
- Easing 函数：根据 `EasingType` 将线性 t 映射为缓动 t

#### 与现有相机的协作

进入 BlendIn 时：
- 检测当前活跃的相机控制器（FreeCameraController / PlayerCamera / ChunkGenerator）
- 禁用它（`enabled = false` 或设置标志）
- 快照当前相机状态

进入 BlendOut 时：
- 重新启用原控制器
- 原控制器开始计算目标位置，CameraScriptPlayer 在 BlendOut 期间插值过渡

回到 Idle 时：
- 完全释放控制权

#### 震动实现

- 使用 Perlin noise 生成 3D 偏移：`noise(time * frequency, seed)` 三个轴独立采样
- 振幅随时间衰减：`amplitude * (1 - elapsed/duration)^decayRate`
- 偏移叠加到最终相机位置上（不影响 rotation/fov）
- 多个震动可叠加（amplitude 相加）

## ActionEventPreviewController 集成

在 `Tick()` 方法中扩展：

### CameraScript — 范围激活型

在第二层 switch（范围激活的视觉效果）中添加：

```csharp
case ActionType.CameraScript:
    if (ae.Params is CameraScriptParams csp && !_cameraScriptPlayer.IsActive)
        _cameraScriptPlayer.Play(csp);
    break;
```

Seek 回退时：如果 `currentTime < _lastTickTime` 且 CameraScriptPlayer 正在播放，调用 `Stop()` 重置。

### CameraShake — 一次性触发型

在第一层 switch（一次性触发事件）中添加：

```csharp
case ActionType.CameraShake:
    if (ae.Params is CameraShakeParams cshk)
        _cameraScriptPlayer.Shake(cshk.Preset);
    break;
```

需要新增 `_triggeredCameraIds` HashSet 做去重（与现有 audio/bg/clear 模式一致）。

### 字段新增

```csharp
private CameraScriptPlayer _cameraScriptPlayer;
private readonly HashSet<int> _triggeredCameraIds = new();
```

`_cameraScriptPlayer` 在初始化时获取或创建（`GetComponentInChildren<CameraScriptPlayer>()` 或 `AddComponent`）。

## 编辑器预览兼容

编辑器中 CameraScript 演出通过 `ActionEventPreviewController` 同样触发 `CameraScriptPlayer`。

- CameraScriptPlayer 接管时禁用 `FreeCameraController`
- 演出结束后恢复 `FreeCameraController`
- 坐标系由 `EditorCameraFrame` 提供：Player 模式用玩家位置，非 Player 模式用 `FreeCameraController._pivot`

## 文件清单

### 新建文件

| 文件 | 职责 |
|------|------|
| `Core/Scene/ICameraFrameProvider.cs` | 坐标标架接口 |
| `Core/Scene/CameraKeyframe.cs` | 相机关键帧数据 |
| `Core/Scene/CameraShakePreset.cs` | 震动预设数据 |
| `Core/Timeline/EasingType.cs` | 缓动类型枚举（如已存在则复用） |
| `Core/Timeline/ActionParams/CameraScriptParams.cs` | CameraScript 事件参数 |
| `Core/Timeline/ActionParams/CameraShakeParams.cs` | CameraShake 事件参数 |
| `Runtime/Scene/CameraScriptPlayer.cs` | 演出镜头播放器 |
| `Runtime/Scene/SplineCameraFrame.cs` | 程序化场景坐标标架实现 |
| `Runtime/Preview/EditorCameraFrame.cs` | 编辑器坐标标架实现 |

### 修改文件

| 文件 | 改动 |
|------|------|
| `Core/Timeline/ActionType.cs` | 新增 `CameraScript`, `CameraShake` |
| `Core/Timeline/ActionParams/ActionParamsRegistry.cs` | 注册两个新映射 |
| `Runtime/Preview/ActionEventPreviewController.cs` | 添加 CameraScript/CameraShake 处理分支 |

## 不做的事

- DynamicCamera（已废弃）
- 运行时 API 直接调用（纯 ActionEvent 驱动）
- 编辑器 UI 面板（属于 Phase 7）
- 相机控制器抽象层重构（当前阶段不需要）
