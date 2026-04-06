# 场景系统开发交接文档

> 日期：2026-07-03
> 状态：Phase 1-3 完成，可运行原型

## 已完成的工作

### Phase 1: 核心管线（样条线通路 + 分块生成 + 场景流动）

基于 Catmull-Rom 样条线的 3D 卷轴场景系统。Chunk 几何体沿样条线在世界坐标中生成（静止），摄像头沿样条线移动。

### Phase 2: 障碍物系统（泊松散布 + 对象池）

路侧障碍物通过泊松圆盘采样散布在通路两侧，左右独立采样。对象池按预制体类型分池，支持运行时注册预制体。

### Phase 3: 交互系统（边界 + 碰撞 + 反馈）

- 边界：基于障碍物位置拟合的平滑曲线做软推力和硬限制
- 碰撞：XZ 水平距离 + localScale 半径（不受旋转影响）
- Sway：以地面接触点为轴心摇晃，幅度随接近程度线性增大
- Nudge：仅 XZ 平面推力，比 sway 触发距离更近

## 文件清单

### Core 层 (`Assets/STGEngine/Core/Scene/`)

| 文件 | 职责 |
|------|------|
| `PathSpline.cs` | Catmull-Rom 样条线，弧长参数化 + Frenet 标架（位置/切线/法线） |
| `PathProfile.cs` | 通路轮廓：PathSpline + WidthCurve + HeightCurve + ScrollSpeed |
| `SceneStyle.cs` | 场景风格组合：PathProfile + ObstacleConfigs + HazardFrequency |
| `ObstacleConfig.cs` | 障碍物散布配置：密度/间距/缩放/旋转/放置区域/危险标记/接触反应 |

### Runtime 层 (`Assets/STGEngine/Runtime/Scene/`)

| 文件 | 职责 |
|------|------|
| `Chunk.cs` | 分块运行时表示：弧长区间 + Ground + Obstacles 列表 |
| `ChunkGenerator.cs` | 分块生成器 MonoBehaviour：滑动窗口、对象池、摄像头跟随 |
| `GroundMeshBuilder.cs` | 沿样条线生成地面 mesh（通路 + 路侧延伸 40m），世界坐标 UV |
| `ScrollController.cs` | 弧长推进 + 速度采样，纯逻辑无场景移动 |
| `PlayerAnchorController.cs` | 玩家锚点：自动沿样条线前进，WASD 在 Normal/Up 平面内自由移动 |
| `BoundaryCurveBuilder.cs` | 从路侧障碍物位置拟合左右边界曲线（分桶 + 滑动平均） |
| `BoundaryForce.cs` | 基于拟合曲线的软推力 + 硬限制 + 地面/天花板约束 |
| `ObstacleScatterer.cs` | 泊松采样散布障碍物，左右独立，路边 3m 间距 |
| `ObstaclePool.cs` | 按预制体类型分池，Get 时重置 scale，支持 RegisterPrefab |
| `PoissonDiskSampler.cs` | Bridson 算法泊松圆盘采样 |
| `HazardCollision.cs` | 危险障碍物碰撞检测（XZ 距离 + localScale 半径） |
| `ObstacleInteraction.cs` | Sway（底部轴心摇晃，幅度随距离）+ Nudge（XZ 推力，更近触发） |
| `SceneTestSetup.cs` | 集成测试引导：创建样条线 + 风格 + 所有子系统 |

### 修改的现有文件

| 文件 | 改动 |
|------|------|
| `Core/Serialization/SerializableCurve.cs` | Evaluate 从线性插值升级为三次 Hermite 插值，自动计算 Catmull-Rom 切线 |

## 关键设计决策与经验

### 样条线方案（重构）

最初用固定 Z 轴 + DriftCurve 补偿方案，导致 Chunk 接缝、深度冲突、旋转不自然。重构为 Catmull-Rom 样条线方案：Chunk 几何体沿样条线在世界坐标中生成（静止），摄像头沿样条线移动。这是正确的基础架构。

### 边界系统演进

经历了三个阶段：
1. WidthCurve 软边界 → 太紧或太松，不贴合障碍物
2. 逐障碍物推力 → 抖动，离散感强
3. 障碍物位置拟合曲线 → 平滑且贴合实际分布（最终方案）

### 碰撞检测

- 用 XZ 水平距离，不用 3D 距离（竹子等竖直物体在地面也能触发）
- 用 localScale 计算半径，不用 renderer.bounds（不受旋转导致的 AABB 膨胀影响）
- Nudge 推力只在 XZ 平面，不影响 Y（防止上弹）

### 当前参数

| 参数 | 值 | 位置 |
|------|-----|------|
| 边界推力范围 | 3m | BoundaryForce._pushRange |
| 边界推力强度 | 20 m/s | BoundaryForce._pushForce |
| 硬边界内缩 | 1m | BoundaryForce._hardMargin |
| Sway 触发距离 | 1.5m | ObstacleInteraction._swayRange |
| Nudge 触发距离 | 0.5m | ObstacleInteraction._nudgeRange |
| 障碍物路边间距 | 3m | ObstacleScatterer margin |
| 地面延伸 | 40m | GroundMeshBuilder.RoadsideExtension |
| 摄像头高度/后退 | 10m / 12m | ChunkGenerator.UpdateCamera |
| 远裁剪面 | 1000m | ChunkGenerator.UpdateCamera |
| 前方生成距离 | 200m | ChunkGenerator._forwardDistance |

## 待实施的后续 Phase

### Phase 4: 镜头系统
- DynamicCamera（FOV 响应、速度感、弹幕密度震动、惯性滞后）
- CameraScriptPlayer（关键帧演出、blend_in/blend_out）

### Phase 5: 敌人场景联动
- SceneAnchoredSpawn（出生点绑定障碍物）
- EnemyExitController（坠地/逃向障碍物退场）
- BossPresenceController（远处观察/伴随/尾随）
- PostBattleSequence（停火→对话→退场→过渡）
- DialogueSceneController（减速/停止/落地对话）

### Phase 6: 环境层
- 远景背景层（天空盒/远景几何体）
- 光照管理（全局预设 + 局部体积光）
- 场景粒子（全局落叶/尘埃 + 局部障碍物粒子）
- 场景音效（环境音 + 空间化 + 危险物警示音）

### Phase 7: 编辑器集成
- ActionType 枚举扩展（6 种新事件）
- STGCatalog SceneStyles 资源类型
- 属性面板 UI
- PatternSandbox 场景预览集成
- 独立 ScenePreviewPanel

## 设计文档

- 完整规格：`doc/specs/2026-07-03-scene-system-design.md`
- Phase 1 计划：`doc/plans/2026-07-03-scene-system-phase1.md`（已过时，样条线重构后部分内容不适用）
- Phase 2 计划：`doc/plans/2026-07-03-scene-system-phase2.md`
