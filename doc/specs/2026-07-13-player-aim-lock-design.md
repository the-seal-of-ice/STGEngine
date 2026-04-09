# 玩家朝向锁定系统设计

## 概述

玩家朝向锁定（Aim Lock）是保持型镜头脚本（Persist）的子功能。它允许在镜头演出期间控制玩家的射击方向和移动参考方向，使玩家能够自动瞄准 Boss、小怪或固定坐标点，而不依赖相机朝向。

## 问题背景

默认情况下，玩家的射击方向（`AimForward`）和移动参考方向（`ViewForward`）都基于 PlayerCamera 的 transform。当 Persist 镜头将相机移到边界中心等非玩家位置时，相机朝向不再指向玩家前方，导致射击方向错乱、移动方向与画面不一致。

## 数据模型

### PlayerAimMode 枚举

定义在 `Core/Scene/CameraKeyframe.cs`，作为 `CameraKeyframe` 的属性：

| 值 | 含义 |
|---|---|
| `Default` | 不改变朝向逻辑，使用 PlayerCamera 默认行为 |
| `FreeMouse` | 鼠标直接驱动玩家自身朝向（与相机解耦） |
| `ScreenCenter` | 射击方向 = 相机朝向（屏幕中心射击） |
| `LockPoint` | 锁定固定世界坐标点 |
| `LockBoss` | 锁定 Boss |
| `LockEnemy` | 锁定最近的敌人 |

### CameraKeyframe 相关字段

```
AimMode: PlayerAimMode          -- 朝向模式
AimTargetPosition: Vector3      -- LockPoint 的目标坐标
AimTargetId: string             -- LockBoss/LockEnemy 的目标 ID（可为空）
```

## 运行时架构

### 三层职责

```
CameraScriptPlayer          -- 决策层：解析关键帧配置，查找目标，设置 PlayerCamera 状态
    |
    v
PlayerCamera                 -- 执行层：每帧计算朝向，驱动 ViewForward/AimForward
    |
    v
PlayerController             -- 消费层：用 ViewForward 计算移动，用 AimForward 计算射击
```

### PlayerCamera 的朝向优先级

`ViewForward` 和 `AimForward` 的计算遵循统一的优先级链：

```
1. AimLockTarget (Transform)    -- 动态目标锁定（Boss/敌人）
2. AimLockPoint (Vector3?)      -- 固定坐标点锁定
3. AimScreenCenter (bool)       -- 屏幕中心（相机朝向）
4. DirectMouseControl (bool)    -- 鼠标直接控制玩家朝向
5. UseOffsetForMovement (bool)  -- 使用含 YawOffset 的相机方向
6. 默认                          -- 基于 _yaw 的相机方向
```

高优先级的模式覆盖低优先级。例如 `AimLockTarget` 非 null 时，无论其他标志如何设置，朝向都指向该目标。

### ViewForward vs AimForward

两者使用相同的优先级链，区别在于 fallback：

- `ViewForward`：水平投影（Y=0），用于 WASD 移动方向计算
- `AimForward`：包含垂直分量，用于射击方向和浮游炮定位

当有锁定目标时，两者都指向目标方向（`AimForward` 保留垂直分量，`ViewForward` 取水平投影）。

## 目标查找机制

### 两种查找回调

CameraScriptPlayer 持有两个外部注入的回调：

```
_aimTargetLookup:   (string id) → Transform       -- 按 ID 精确查找
_aimNearestLookup:  (AimMode, Vector3 pos) → Transform  -- 按距离查找最近目标
```

### 查找流程

```
FindAimTarget(mode, targetId, playerCam):
    1. targetId 非空 → 调用 _aimTargetLookup(id)
       找到 → 返回
    2. targetId 为空或查找失败 → 调用 _aimNearestLookup(mode, playerPos)
       LockBoss → 返回当前可见的 BossPlaceholder
       LockEnemy → 遍历所有 EnemyPlaceholder，返回距离玩家最近且可见的
                   没有小怪时 fallback 到 Boss
    3. 都找不到 → 返回 null（不锁定）
```

### 有效性判定

目标"有效"的条件：
- 未被销毁（Unity 的 `== null` 检查）
- `BossPlaceholder.IsVisible == true`（Boss 在场且可见）
- `EnemyPlaceholder.IsVisible == true`（敌人在场、存活、在时间窗口内）

## 目标丢失与自动切换

### 检测机制

`PlayerCamera.GetAimLockDirection()` 每帧执行，检查 `AimLockTarget`：

```
1. AimLockTarget 已被销毁（Unity destroyed）
   → 清除引用，设 AimLockTargetLost = true

2. AimLockTarget 是 BossPlaceholder 且 IsVisible == false
   → 清除引用，设 AimLockTargetLost = true

3. AimLockTarget 是 EnemyPlaceholder 且 IsVisible == false
   → 清除引用，设 AimLockTargetLost = true

4. 以上都不满足 → 目标有效，返回方向向量
```

### 重新查找机制

`CameraScriptPlayer.LateUpdate()` 在 PersistBlend 和 Idle 状态下检查 `AimLockTargetLost`：

```
if (persistTarget.AimLockTargetLost):
    AimLockTargetLost = false
    if currentAimMode is LockBoss or LockEnemy:
        persistTarget.AimLockTarget = FindAimTarget(mode, "", playerCam)
        // 空 ID → 走最近目标查找
```

这意味着：
- Boss 被击败 → 自动解除锁定（没有其他 Boss）
- 小怪被击杀 → 自动切换到下一个最近的小怪
- 所有小怪死光 → fallback 到 Boss
- Boss 也没了 → 不锁定，回退到优先级链的下一级

### 时序

```
帧 N:
  PlayerCamera.LateUpdate:
    GetAimLockDirection() 发现目标不可见
    → AimLockTarget = null, AimLockTargetLost = true
    → 本帧 ViewForward/AimForward 回退到下一优先级

  CameraScriptPlayer.LateUpdate (order 100, 在 PlayerCamera 之后):
    检测到 AimLockTargetLost
    → FindAimTarget 查找新目标
    → 设置 AimLockTarget = 新目标

帧 N+1:
  PlayerCamera.LateUpdate:
    GetAimLockDirection() 使用新目标
    → ViewForward/AimForward 指向新目标
```

目标切换有 1 帧的间隙（帧 N 回退到 fallback 方向）。在实际游戏中这个间隙不可感知。

## 编辑器集成

### 关键帧属性面板

每个关键帧卡片中显示：

- **Aim Mode** 下拉：Default / FreeMouse / ScreenCenter / LockPoint / LockBoss / LockEnemy
- **Aim Position**（仅 LockPoint 时显示）：Vector3 坐标输入
- **Aim Target ID**（仅 LockBoss/LockEnemy 时显示）：文本输入，可留空

ID 留空时自动查找最近目标，这是推荐用法。只有需要锁定特定目标（场景中有多个 Boss）时才填 ID。

### 预设

内置预设中包含 Aim Lock 示例：
- "锁定Boss(持久)"：Player 参考 + LockBoss，ID 留空自动锁定

## 文件清单

| 文件 | 职责 |
|---|---|
| `Core/Scene/CameraKeyframe.cs` | `PlayerAimMode` 枚举 + `AimMode/AimTargetPosition/AimTargetId` 字段 |
| `Runtime/Player/PlayerCamera.cs` | `AimLockTarget/AimLockPoint/AimScreenCenter` 状态 + `GetAimLockDirection` 检测 + `ViewForward/AimForward` 优先级链 |
| `Runtime/Scene/CameraScriptPlayer.cs` | `ApplyAimMode` 设置 + `FindAimTarget` 查找 + LateUpdate 中目标丢失重新查找 |
| `Runtime/Preview/ActionEventPreviewController.cs` | 注入 `_aimTargetLookup` 和 `_aimNearestLookup` 回调实现 |
| `Editor/UI/Timeline/TimelineEditorView.cs` | 关键帧 Aim Mode UI |
