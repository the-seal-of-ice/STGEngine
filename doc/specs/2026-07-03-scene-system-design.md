# 场景系统设计规格

> 日期：2026-07-03
> 状态：已批准，待实施
> 范围：场景空间与环境系统（3D 卷轴模式 + 程序化通路生成 + 镜头系统）

## 1. 概述

### 1.1 目标

为 STGEngine 构建场景空间与环境系统，让 3D 弹幕射击的关卡拥有真实的空间感和视觉动势。首期聚焦于"3D 卷轴 + 程序化通路"模式——场景向玩家方向流动，玩家在固定区域内自由 3D 移动，通路两侧由程序化散布的障碍物（竹林、巨石等）构成视觉边界。

### 1.2 设计原则

- 数据驱动：场景形态由可序列化的配置数据定义，通过时间轴编辑器编排
- 分层管线：生成流程拆分为独立阶段，每层职责单一，可独立替换和测试
- 风格可切换：不同关卡可使用不同的障碍物风格（竹林、巨石、晶体等），通过配置切换
- 与现有架构一致：遵循 Core（纯数据）/ Runtime（MonoBehaviour）/ Editor 三层架构

### 1.3 范围界定

**首期实现：**
- 3D 卷轴场景流动
- 通路轮廓系统（宽度渐变、Boss 战场展开/收窄）
- 障碍物程序化散布（预制体变体 + 泊松采样）
- 软边界力场
- 道路内危险障碍物（碰撞掉残机）
- 镜头动态响应 + 脚本演出

**后期扩展（记录但不实现）：**
- 视觉支路 / 风格分区（通路内不同区域不同风格）
- 蜿蜒路径模式（方案 B，玩家沿弯曲路径前进）
- 运行时实时 mesh 生成（方案 B 的障碍物生成）
- 关卡流程管理（加载、过渡、卸载）

## 2. Core 层数据模型

所有数据结构为纯 POCO，无 Unity 依赖，可通过现有 YAML 序列化系统持久化。

### 2.1 PathProfile（通路轮廓）

描述通路随时间的形态变化：

| 字段 | 类型 | 说明 |
|------|------|------|
| `WidthCurve` | `SerializableCurve` | 通路宽度随距离变化。窄通道 ~15m，Boss 战场 ~60m |
| `HeightCurve` | `SerializableCurve` | 通路高度（3D 纵向范围），默认与宽度等比 |
| `ScrollSpeed` | `SerializableCurve` | 场景流动速度（m/s），可变速。Boss 前减速，道中加速 |
| `DriftCurve` | `SerializableCurve` | 通路中心线横向偏移，让通路有蜿蜒感而非死直 |

所有曲线复用现有 `SerializableCurve` 类型。曲线的 X 轴为"滚动距离"（非时间），这样速度变化不会扭曲通路形态。

### 2.2 ObstacleConfig（障碍物配置）

定义一种障碍物风格的散布规则：

| 字段 | 类型 | 说明 |
|------|------|------|
| `PrefabVariants` | `string[]` | 预制体变体资源路径列表 |
| `Density` | `float` | 基础散布密度（个/m²） |
| `ScaleRange` | `FloatRange` | 缩放随机范围 (min, max)，Core 层自定义值类型 |
| `RotationRange` | `FloatRange` | Y 轴旋转随机范围（度），Core 层自定义值类型 |
| `PlacementZone` | `PlacementZone` | 放置区域枚举：`Roadside`（通路两侧）/ `Interior`（通路内部） |
| `IsHazard` | `bool` | 是否为危险障碍物（碰撞掉残机） |
| `MinSpacing` | `float` | 泊松采样最小间距 |
| `Durability` | `float` | 耐久度（0 = 不可破坏）。危险障碍物默认低耐久度，鼓励玩家清除 |
| `ContactResponse` | `ContactResponse` | 玩家擦过时的反应枚举：`Sway`（摇晃）/ `Nudge`（轻推）/ `None` |

### 2.3 SceneStyle（场景风格）

组合一个完整的场景视觉配置：

| 字段 | 类型 | 说明 |
|------|------|------|
| `PathProfile` | `PathProfile` | 通路轮廓 |
| `ObstacleConfigs` | `ObstacleConfig[]` | 障碍物配置列表（可混合多种风格） |
| `BoundaryFalloff` | `SerializableCurve` | 软边界推力衰减曲线 |
| `BoundaryInnerRatio` | `float` | 自由区比例，默认 0.8（通路宽度的 80% 内无推力） |
| `HazardFrequency` | `float` | 道路内危险物出现频率（个/100m） |
| `HasGround` | `bool` | 是否生成可见地面，默认 true。月面虚空等特殊场景设为 false |
| `GroundPrefab` | `string` | 地面预制体/材质资源路径 |
| `SkyboxProfile` | `string` | 天空盒/远景配置资源路径 |
| `LightingPreset` | `string` | 光照预设资源路径（方向光、环境光、雾效参数） |
| `AmbientParticles` | `string` | 全局粒子效果资源路径（落叶、尘埃等） |
| `AmbientAudio` | `string` | 环境音循环资源路径 |

### 2.4 后期扩展数据模型（仅记录）

**BranchPoint（视觉支路分叉点）：**
- `BranchCount` — 支路数量
- `BranchStyles[]` — 每条支路的 SceneStyle
- `MergeDistance` — 汇合距离
- `TransitionLength` — 分叉/汇合过渡长度

**StyleTransition（风格过渡）：**
- `CrossfadeZone` — 两种风格在过渡区内混合散布
- 密度渐变：风格 A 从 1→0，风格 B 从 0→1

## 3. Runtime 层系统

### 3.1 ChunkGenerator（分块生成器）

**职责：** 管理场景的分块生成与回收，维护滑动窗口。

**Chunk 定义：**
- 每个 Chunk 沿滚动轴（+Z）长度固定（建议 40m，与现有 `WorldScale` 边界半径一致）
- 每个 Chunk 在生成时从 `PathProfile` 采样当前滚动距离的宽度/高度/偏移
- 相邻 Chunk 之间保证 C1 连续（位置连续 + 切线连续），无可见接缝

**通路连续性保证：**

通路的平滑性需要在四个层面解决：

*a) 通路轮廓插值*
- PathProfile 的各条曲线（WidthCurve、HeightCurve、DriftCurve）以滚动距离为 X 轴，曲线求值天然连续
- 每个 Chunk 在起点和终点各采样一次，Chunk 内部通路形态在两个采样值之间做 Hermite 插值（三次样条），而非线性插值——线性插值会在 Chunk 边界产生"角"
- 相邻 Chunk 共享边界采样值（位置 + 切线），保证 C1 连续

*b) 地面 mesh 接缝*
- 地面 mesh 在 Chunk 边界处的顶点位置和法线与相邻 Chunk 共享
- 每个 Chunk 的地面 mesh 在 Z 方向多延伸半个顶点间距（overlap 区），与下一个 Chunk 的起始顶点重合
- 地面 UV 沿滚动方向连续递增，避免纹理接缝

*c) 障碍物散布跨 Chunk 连续*
- 泊松采样时，每个 Chunk 的采样区域向相邻 Chunk 方向扩展一个 `MinSpacing` 的距离（采样溢出区）
- 溢出区内生成的采样点不实际放置障碍物，但参与距离检测，确保边界处的间距约束跨 Chunk 生效
- `DeterministicRng` 的种子由 Chunk 索引派生，保证相邻 Chunk 的溢出区计算结果一致

*d) DriftCurve 曲率约束*
- DriftCurve 让通路中心线横向偏移，偏移变化过快会导致急转弯
- 编辑器中对 DriftCurve 施加最大斜率约束（建议最大横向偏移速率 ≤ 通路宽度/Chunk长度，确保通路不会在一个 Chunk 内偏移超过自身宽度）
- 运行时对采样值做滑动平均滤波作为安全兜底

**滑动窗口：**
- 玩家前方保持 N 个活跃 Chunk（建议 N=3，即 120m 可视深度）
- 身后超出 1 个 Chunk 距离的 Chunk 回收进对象池
- 场景流动通过整体平移所有活跃 Chunk 实现（而非移动玩家）

**场景流动机制：**
- 每帧将所有活跃 Chunk 沿 -Z 方向移动 `ScrollSpeed * deltaTime`
- 当最近的 Chunk 完全移过玩家后方，回收该 Chunk 并在最远端生成新 Chunk
- 玩家的世界坐标始终在原点附近，避免浮点精度问题

### 3.2 ObstacleScatterer（障碍物散布器）

**职责：** 在每个 Chunk 生成时，根据 ObstacleConfig 散布障碍物实例。

**散布算法：**
- 使用泊松圆盘采样（Poisson Disk Sampling）生成散布点
- 采样区域：Chunk 的"通路外侧带"（PathProfile 宽度之外，到视觉消失距离之间）
- 每个采样点从 `PrefabVariants` 中随机选取变体，施加随机变换
- 使用 `DeterministicRng` 保证同一关卡配置下生成结果一致

**密度联动：**
- 实际密度 = `ObstacleConfig.Density` × 密度系数
- 密度系数与 `PathProfile.WidthCurve` 反相关：通路越窄，两侧障碍物越密集
- Boss 战场展开时密度降低，障碍物稀疏，视野开阔

**危险障碍物：**
- `IsHazard = true` 的障碍物散布在通路内部（`PlacementZone.Interior`）
- 出现频率由 `SceneStyle.HazardFrequency` 控制
- 碰撞判定体积比视觉体积略小（给玩家容错）
- 碰撞结果：不减速不阻挡移动，直接触发残机判定

**对象池：**
- 所有障碍物实例由对象池管理
- 按预制体类型分池
- Chunk 回收时，其上所有障碍物归还对应池

### 3.3 BoundaryForce（软边界力场）

**职责：** 在通路边缘施加渐变推力，柔和地约束玩家活动范围。

**力场计算：**
- 从 `PathProfile.WidthCurve` 实时获取当前通路宽度 W
- 自由区半径 = W × `BoundaryInnerRatio`（默认 0.8）
- 玩家在自由区内：无推力
- 玩家在自由区外：受到指向通路中心的推力，强度由 `BoundaryFalloff` 曲线决定
- 推力输入为归一化深度：`(playerDist - freeRadius) / (W/2 - freeRadius)`，范围 0~1
- 3D 空间中高度方向同理，从 `HeightCurve` 派生

**体验特征：**
- 不是硬墙弹回，而是阻力渐增、移速下降
- 视觉反馈：具体表现留到实现阶段确定（候选方案：屏幕边缘暗角、玩家周围粒子效果、或依赖障碍物本身作为视觉提示）

### 3.4 DynamicCamera（动态响应镜头）

**职责：** 在现有 PlayerCamera 基础上扩展，增加对场景状态的动态响应。

**基础行为：** 跟随玩家，保持相对位置偏移（继承现有 PlayerCamera 逻辑）。

**响应参数：**

| 响应维度 | 输入源 | 效果 |
|----------|--------|------|
| FOV | `PathProfile.WidthCurve` 当前值 | 窄通路 → FOV 收小（压迫感），展开 → FOV 放大（开阔感） |
| 前后推拉 | `ScrollSpeed` 变化率 | 加速时微推前，减速时微拉后 |
| 震动 | `SimulationLoop` 活跃弹幕数量 | 弹幕密度高时轻微震动（可配置强度/频率） |
| 滞后 | 玩家移动速度/方向变化 | 急转向时镜头略微滞后，产生惯性感 |

所有响应效果的强度均可配置，且有平滑插值避免突变。

### 3.5 CameraScript（演出镜头）

**职责：** 在关键时刻接管镜头，执行预编排的关键帧动画。

**数据结构：**
- `CameraKeyframe`：position、lookAt、fov、easing、time
- 关键帧序列通过时间轴 ActionEvent（`CameraScriptEvent`）触发

**混合机制：**
- `CameraControl` 参数（0~1）控制 DynamicCamera 与 CameraScript 的混合权重
- 演出开始：`CameraControl` 从 1 渐降到 0（`blend_in` 时长内）
- 演出结束：`CameraControl` 从 0 渐升到 1（`blend_out` 时长内）
- 与设计文档中 `ModeParams.CameraControl` 的设计一致

**典型用途：**
- Boss 登场：镜头环绕 Boss，展示全貌
- 符卡宣言：镜头推近 Boss 特写
- 通路转折：引导镜头暗示前方变化

## 4. 与敌人系统的场景联动

场景不只是视觉装饰，而是与小怪/Boss 系统深度耦合的叙事空间。

### 4.1 小怪出生点与场景物体绑定

小怪的出生可以与场景障碍物关联，实现"从树后飞出""从巨石后方涌出"等演出效果：

**SceneAnchoredSpawn（场景锚定出生）：**

| 字段 | 类型 | 说明 |
|------|------|------|
| `AnchorType` | `SpawnAnchorType` | 锚定方式：`BehindObstacle`（障碍物后方）/ `AboveObstacle`（障碍物上方）/ `BetweenObstacles`（障碍物之间） |
| `ObstacleTag` | `string` | 目标障碍物标签（如 `"bamboo"`、`"rock"`），从散布的障碍物中匹配 |
| `RelativeOffset` | `FloatVector3` | 相对于锚定障碍物的偏移 |
| `SpawnDistance` | `float` | 在玩家前方多远处生成（滚动距离），确保出生点在恰当时机位于恰当位置 |

**时序协调：**
- 时间轴中 EnemyInstance 的出生时间 → 换算为滚动距离 → ChunkGenerator 在生成对应 Chunk 时预留锚定障碍物
- 散布器在放置障碍物时，标记哪些障碍物被预留为出生锚点，确保这些障碍物不会被跳过或回收过早
- 小怪出生动画可以利用锚定障碍物做遮挡：先隐藏在障碍物后方，时间到时从后方飞出

### 4.2 Boss 场景存在感

Boss 不应该在触发战斗时突然凭空出现，而是通过场景系统提前建立存在感：

**BossPresence（Boss 场景存在）：**

| 模式 | 说明 |
|------|------|
| `DistantObserve` | Boss 在场景远处静止观察，作为场景元素的一部分可见。细心的玩家能在道中阶段就注意到远处的身影 |
| `DistantFollow` | Boss 在场景远处伴随移动，与场景流动保持相对静止（即跟着玩家走），偶尔做微小动作 |
| `DistantTrail` | Boss 在场景后方尾随，玩家回头看能发现。随时间逐渐接近 |
| `Hidden` | Boss 隐藏在场景元素中（如巨石后、云层中），通过局部异常暗示存在（微光、阴影、粒子） |

**触发与过渡：**
- BossPresence 通过时间轴 ActionEvent 控制，可以在道中任意时间点开始
- 当 Boss 战正式触发时，BossPresence 平滑过渡到 Boss 战斗状态：
  - `DistantObserve/Follow` → 镜头演出（CameraScript）拉近 Boss → 通路展开 → 战斗开始
  - `DistantTrail` → Boss 加速追上 → 镜头演出 → 战斗开始
  - `Hidden` → 场景元素破碎/散开 → Boss 显现 → 战斗开始
- 这些过渡序列由时间轴中的复合事件编排（CameraScriptEvent + SceneStyleSwitch + Boss 出场事件）

### 4.3 场景物体的语义标签

为了支持上述联动，障碍物实例需要携带语义信息：

**ObstacleInstance 运行时扩展：**
- `Tag` — 从 ObstacleConfig 继承的类型标签（`"bamboo"`、`"rock"` 等）
- `AnchorId` — 如果被预留为出生锚点，记录关联的 EnemyInstance ID
- `ChunkIndex` — 所属 Chunk 索引，用于生命周期管理

### 4.4 小怪退场与场景互动

小怪被击败后不应凌空消逝，而是与场景产生物理互动，走可爱/生动的风格：

**EnemyExitBehaviour（退场行为）：**

| 模式 | 说明 |
|------|------|
| `CrashLand` | 突然飞不动，坠落到最近的地面/障碍物表面，弹跳几下后消失（可爱风妖精） |
| `FleeToObstacle` | 立刻高速逃向最近的障碍物（竹林/巨石后方），钻进去消失。逃跑方向由最近障碍物位置决定 |
| `Tumble` | 失控翻滚坠落，碰到障碍物后弹开，最终消失 |
| `Dissolve` | 传统消散（作为保底默认行为，或用于特殊敌人） |

**场景感知：**
- 退场行为需要查询附近的障碍物位置（通过 ObstacleScatterer 的空间索引）
- `CrashLand` 需要知道"地面在哪"——在 3D 卷轴模式下，通路的下边界即为地面参考
- `FleeToObstacle` 需要找到最近的、在逃跑方向上的障碍物，并计算隐藏点
- 退场动画期间敌人不再参与碰撞判定（已被击败），纯视觉表现

**配置方式：**
- `EnemyType` 数据模型扩展 `ExitBehaviour` 字段，指定该类型敌人的退场模式
- 可配置退场速度、坠落重力、弹跳次数等参数
- 同一类型敌人可配置多种退场行为的权重随机

### 4.5 对话与场景节奏联动

玩家与 Boss 的对话不应总是在高速滚动的场景中进行，场景节奏应配合对话氛围：

**DialogueSceneMode（对话场景模式）：**

| 模式 | 说明 |
|------|------|
| `SlowScroll` | 场景流动减速（如降到 20%），对话在缓慢移动的场景中进行，保持一定动势但不紧张 |
| `FullStop` | 场景完全停止流动，玩家和 Boss 在静止的场景中对话。适合严肃/重要的剧情时刻 |
| `Grounded` | 场景停止 + 角色"落地"——玩家和 Boss 降落到通路地面上，以站立姿态对话。最具临场感 |
| `KeepScrolling` | 场景保持正常流动，对话在战斗间隙快速进行（适合挑衅/短对话） |

**实现要点：**
- 对话模式通过时间轴 ActionEvent 触发，与对话事件绑定
- `SlowScroll` / `FullStop`：修改 `ScrollSpeed` 并平滑插值过渡
- `Grounded`：额外需要临时修改玩家/Boss 的位置约束，让他们"站"在地面上，对话结束后平滑恢复自由飞行
- 镜头在对话期间自动切换到对话镜头模式（可由 CameraScript 编排，如正反打、双人中景等）

### 4.6 Boss 退场演出

Boss 被击败后的退场同样应与场景互动，而非简单消失：

**BossExitBehaviour（Boss 退场行为）：**

| 模式 | 说明 |
|------|------|
| `RetreatToDistance` | Boss 向场景远处撤退，逐渐缩小消失（为后续再次出场留伏笔） |
| `CrashIntoScene` | Boss 坠落/撞向场景障碍物，引发场景破坏效果（障碍物碎裂飞散） |
| `DissolveWithScene` | Boss 与周围场景元素一起消散/变化，场景风格随之过渡（如竹林枯萎→新场景生长） |
| `FlyAway` | Boss 高速飞离画面（经典东方风格），可指定飞离方向 |
| `Scripted` | 完全由 CameraScript + 时间轴事件序列编排的自定义退场 |

**场景联动效果：**
- Boss 退场可以触发场景变化：通路宽度变化、障碍物风格切换、流动速度改变
- 这些通过时间轴中的复合事件序列实现（BossExit + SceneStyleSwitch + ScrollSpeedChange）
- `CrashIntoScene` 需要场景破坏系统支持——被撞击的障碍物播放碎裂动画并从场景中移除

## 5. 场景环境与视觉层次

### 5.1 远景背景层

通路两侧障碍物有间隙，玩家可以透过缝隙看到远处的背景：

- 每种 SceneStyle 配置一套远景层：天空盒/远景几何体/粒子雾
- 远景层不参与场景流动（或以极慢的视差速率移动），与近处障碍物形成纵深层次
- 风格切换时远景层同步过渡（交叉淡入淡出）

### 5.2 通路地面

通路有可见的地面，提供速度感知的直接参照：

- 地面类型随 SceneStyle 配置：泥土小路、石板路、水面、草地等
- 地面由 ChunkGenerator 随 Chunk 一起生成，使用地面预制体或程序化平面 mesh + 材质
- 地面宽度跟随 `PathProfile.WidthCurve` 变化
- 少数特殊场景可以没有地面（如月面虚空），通过 SceneStyle 配置 `HasGround = false`

### 5.3 光照与氛围

**全局光照配置：**
- 每种 SceneStyle 预设一套光照参数：方向光角度/颜色/强度、环境光颜色、雾效颜色/密度/距离
- 风格切换时所有光照参数平滑插值过渡

**局部光照效果：**
- 障碍物影响局部光照：竹林中光束穿过缝隙的体积光（URP Light Cookie 或 VolumetricFog）、巨石投射的动态阴影
- 体积光方向与全局方向光一致，密度受障碍物间距影响
- 危险障碍物可以有自发光效果（警示色），增强辨识度

### 5.4 场景可破坏性

障碍物可以被玩家射击和 Boss 弹幕破坏：

**破坏机制：**
- 每个障碍物实例有 `Durability`（耐久度），被射击时减少
- 耐久度归零时触发碎裂：播放碎裂动画/粒子效果，障碍物从场景中移除
- 危险障碍物可以被玩家主动清除（耐久度较低），增加玩法策略性
- 普通路侧障碍物耐久度较高，不容易被清除，但 Boss 高威力弹幕可以打穿

**破坏效果：**
- 碎裂产生碎片粒子（方向与击中方向相关）
- 被破坏的障碍物留下残根/碎片（可选，低 LOD 替代物）
- Boss 的 `CrashIntoScene` 退场触发区域性批量破坏

**配置：**
- `ObstacleConfig` 扩展 `Durability` 字段（0 = 不可破坏）
- `IsHazard` 障碍物默认低耐久度（鼓励玩家清除）

### 5.5 玩家与普通障碍物的物理交互

玩家擦过普通障碍物时有反馈但无惩罚：

- 竹子/树木：摇晃动画 + 掉落叶片粒子
- 巨石：玩家受到轻微推力（比软边界弱），石头表面产生微粒子
- 所有普通障碍物：擦过时的触觉反馈（手柄震动，如果支持）
- 不掉残机、不造成伤害，纯粹的环境互动感

### 5.6 场景粒子系统

**全局粒子：**
- 每种 SceneStyle 配一套全局粒子效果：竹林落叶、巨石区尘埃、月面微光粒子、雪花等
- 粒子随场景流动方向飘动，增强速度感
- 风格切换时粒子系统交叉过渡

**局部粒子：**
- 障碍物自身产生局部粒子：竹子摇晃时掉落叶片、巨石被破坏时碎片飞溅
- 玩家擦过障碍物时触发的互动粒子
- 危险障碍物的警示粒子（持续发出微光/烟雾）

### 5.7 场景音效

**环境音：**
- 每种 SceneStyle 配一套环境音循环（竹林风声、水流声、月面寂静中的低频嗡鸣等）
- 风格过渡时环境音交叉淡入淡出
- 场景流动速度影响风切声强度

**空间化音效：**
- 障碍物有 3D 空间定位音效：经过竹子时听到近处的沙沙声，经过巨石时有低沉的风声
- 音效强度随距离衰减，使用 Unity 的 AudioSource 3D 空间化
- 危险障碍物有更强、更显著的音效提示（持续的警示音/嗡鸣），作为玩法警告
- 障碍物被破坏时的碎裂音效（空间定位）

## 7. 与时间轴系统的集成

新增 ActionEvent 类型（扩展现有 `ActionParams`）：

| ActionEvent | 说明 |
|-------------|------|
| `SceneStyleSwitch` | 切换场景风格，指定目标 SceneStyle、过渡时长、过渡曲线 |
| `CameraScriptEvent` | 触发镜头演出，指定关键帧序列、blend_in/blend_out 时长 |
| `ScrollSpeedChange` | 改变场景流动速度（也可通过 PathProfile.ScrollSpeed 曲线实现，此为即时覆盖） |
| `BossPresenceEvent` | 控制 Boss 场景存在模式，指定模式、起始距离、接近速率 |
| `DialogueSceneEvent` | 触发对话场景模式（SlowScroll/FullStop/Grounded/KeepScrolling），指定过渡时长 |
| `BossExitEvent` | 触发 Boss 退场演出，指定退场模式和场景联动效果 |

这些 ActionEvent 在时间轴编辑器中以 Block 形式呈现，与现有的 `BackgroundSwitch`、`ScreenEffect` 等并列。

## 8. 与现有系统的关系

| 现有系统 | 关系 | 备注 |
|----------|------|------|
| `SandboxBoundary` | 被 BoundaryForce 替代 | 现有线框可视化可保留用于编辑器调试 |
| `PlayerCamera` | 被 DynamicCamera 扩展 | 基础跟随逻辑继承，增加响应层 |
| `FreeCameraController` | 保持不变 | 编辑器自由相机，与游戏镜头独立 |
| `CollisionSystem` | 被危险障碍物复用 | 需评估现有实现成熟度，可能需要完善 |
| `DeterministicRng` | 被散布算法复用 | 需评估现有实现成熟度 |
| `SerializableCurve` | 被 PathProfile 各曲线复用 | 需评估现有实现成熟度 |
| `PlayerState` | 被残机判定复用 | 需评估现有实现成熟度 |
| `SimulationLoop` | 被镜头弹幕密度响应读取 | 只读依赖 |
| `TimelineEvent` / `ActionParams` | 扩展新的 ActionEvent 类型 | 遵循现有扩展模式 |

注意：上述"复用"的现有系统均为早期实现，成熟度不高。实施时需逐一评估，按需完善。

## 9. 编辑器预览

### 9.1 PatternSandbox 扩展（完整预览）

在现有 PatternSandbox 中集成场景系统，实现弹幕 + 场景的完整预览：

- 加载 SceneStyle 配置后，沙盒中实时运行 ChunkGenerator + ObstacleScatterer
- 场景流动与时间轴播放同步
- 可以在完整的场景环境中测试弹幕模式、敌人出生、Boss 演出

### 9.2 独立场景预览模式（快速调参）

专门的场景预览窗口，用于快速迭代场景参数：

- 实时预览通路生成：拖动 WidthCurve 控制点，立即看到通路宽窄变化
- 障碍物密度/类型调参：滑块调整密度，下拉切换预制体变体，实时刷新散布
- 场景流动速度预览：可暂停/加速/倒退场景流动
- 不需要弹幕模拟和玩家控制，纯场景环境预览
- 支持自由相机浏览（复用现有 FreeCameraController）

## 10. 文件结构规划

```
Assets/STGEngine/
├── Core/
│   ├── Scene/
│   │   ├── PathProfile.cs          # 通路轮廓数据
│   │   ├── ObstacleConfig.cs       # 障碍物配置数据
│   │   ├── SceneStyle.cs           # 场景风格组合（含地面、远景、光照、粒子、音效配置）
│   │   ├── CameraKeyframe.cs       # 镜头关键帧数据
│   │   ├── SceneAnchoredSpawn.cs   # 场景锚定出生点数据
│   │   ├── BossPresence.cs         # Boss 场景存在模式数据
│   │   ├── EnemyExitBehaviour.cs   # 小怪退场行为数据
│   │   ├── DialogueSceneMode.cs    # 对话场景模式数据
│   │   └── BossExitBehaviour.cs    # Boss 退场行为数据
│   └── Timeline/
│       └── ActionParams.cs         # 扩展新的 ActionEvent 类型（已有文件）
├── Runtime/
│   ├── Scene/
│   │   ├── ChunkGenerator.cs       # 分块生成与回收（含地面生成）
│   │   ├── ObstacleScatterer.cs    # 障碍物散布（含空间索引）
│   │   ├── ObstaclePool.cs         # 障碍物对象池
│   │   ├── ObstacleInteraction.cs  # 障碍物物理交互（摇晃、推力、破坏）
│   │   ├── BoundaryForce.cs        # 软边界力场
│   │   ├── HazardCollision.cs      # 危险障碍物碰撞判定
│   │   ├── SpawnAnchorResolver.cs  # 出生锚点解析
│   │   ├── BossPresenceController.cs # Boss 场景存在控制
│   │   ├── EnemyExitController.cs  # 小怪退场动画控制
│   │   ├── DialogueSceneController.cs # 对话场景模式控制
│   │   ├── SceneLighting.cs        # 场景光照管理（全局 + 局部体积光）
│   │   ├── SceneParticles.cs       # 场景粒子管理（全局 + 局部）
│   │   └── SceneAudio.cs           # 场景音效管理（环境音 + 空间化音效）
│   └── Camera/
│       ├── DynamicCamera.cs        # 动态响应镜头
│       └── CameraScriptPlayer.cs   # 演出镜头播放器
├── Editor/
│   └── Scene/
│       └── ScenePreviewPanel.cs    # 独立场景预览面板（快速调参）
```
