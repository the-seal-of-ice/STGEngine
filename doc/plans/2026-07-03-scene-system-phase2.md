# 场景系统 Phase 2: 障碍物系统 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在样条线通路两侧程序化散布障碍物预制体（竹林、巨石等），支持对象池回收、密度联动、危险障碍物。

**Architecture:** ObstacleConfig（Core 数据模型）定义散布规则，ObstacleScatterer（Runtime）在 Chunk 生成时沿样条线法线方向散布障碍物。泊松圆盘采样保证自然间距，DeterministicRng 保证确定性。对象池按预制体类型分池。

**Spec:** `doc/specs/2026-07-03-scene-system-design.md` §2.2, §2.3, §3.2

---

## 文件结构

**新建文件：**

| 文件 | 职责 |
|------|------|
| `Assets/STGEngine/Core/Scene/ObstacleConfig.cs` | 障碍物散布配置数据 |
| `Assets/STGEngine/Runtime/Scene/PoissonDiskSampler.cs` | 2D 泊松圆盘采样算法 |
| `Assets/STGEngine/Runtime/Scene/ObstaclePool.cs` | 按预制体类型分池的对象池 |
| `Assets/STGEngine/Runtime/Scene/ObstacleScatterer.cs` | 障碍物散布器，在 Chunk 生成时调用 |

**修改文件：**

| 文件 | 改动 |
|------|------|
| `Assets/STGEngine/Core/Scene/SceneStyle.cs` | 添加 ObstacleConfigs 字段 |
| `Assets/STGEngine/Runtime/Scene/Chunk.cs` | 添加障碍物实例列表 |
| `Assets/STGEngine/Runtime/Scene/ChunkGenerator.cs` | 集成 ObstacleScatterer，Chunk 回收时归还障碍物 |
| `Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs` | 添加测试用障碍物配置（用 Unity 原始几何体代替预制体） |
