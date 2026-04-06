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
- **位置坐标系**：关键帧位置为相对玩家偏移（运行时加上玩家世界坐标）
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
    public Vector3 PositionOffset;  // 相对玩家的 XYZ 偏移
    public Vector3 Rotation;        // 欧拉角 (pitch, yaw, roll)
    public float FOV = 60f;         // 视野角度
    public EasingType Easing = EasingType.EaseInOut;
}
```

关键帧按 `Time` 升序排列。第一帧 `Time` 通常为 0。插值在相邻帧之间进行，超出最后一帧后保持最后一帧的值。

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

## 运行时组件

### CameraScriptPlayer

```
Runtime/Scene/CameraScriptPlayer.cs（新建）
```

独立 MonoBehaviour，挂载在场景中（由 ActionEventPreviewController 或 SceneTestSetup 创建/获取）。

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
   - **Playing**：在关键帧列表中找到当前时间所在的区间，按 Easing 插值
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
- 编辑器中的"玩家位置"参考点：使用 `FreeCameraController._pivot`（轨道中心）作为偏移基准。如果 Player 模式激活，则使用玩家实际位置

## 文件清单

### 新建文件

| 文件 | 职责 |
|------|------|
| `Core/Scene/CameraKeyframe.cs` | 相机关键帧数据 |
| `Core/Scene/CameraShakePreset.cs` | 震动预设数据 |
| `Core/Timeline/EasingType.cs` | 缓动类型枚举（如已存在则复用） |
| `Core/Timeline/ActionParams/CameraScriptParams.cs` | CameraScript 事件参数 |
| `Core/Timeline/ActionParams/CameraShakeParams.cs` | CameraShake 事件参数 |
| `Runtime/Scene/CameraScriptPlayer.cs` | 演出镜头播放器 |

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
