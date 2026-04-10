# STGEngine Unity AI 自动化测试基础设施 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 STGEngine 搭建第一阶段可运行的 Unity AI 自动化测试基础设施，包括测试程序集、测试场景骨架、测试入口、结构化结果输出，以及首批高价值 EditMode / PlayMode / E2E 测试。

**Architecture:** 采用“官方测试框架 + 自建测试入口 + 结构化结果捕获”的分层方案。底层使用 Unity Test Framework 执行 EditMode/PlayMode 测试；中层通过 Test Harness、Facade、Runner 固定测试入口；上层为 AI / MCP 预留统一的结果读取与场景执行接口。首阶段只覆盖最稳定、最高价值的核心路径：命令栈、目录/覆盖逻辑、场景集成、Pattern/Timeline/Scene 三条 E2E 骨架。

**Tech Stack:** Unity Test Framework (`com.unity.test-framework`)、Unity asmdef、NUnit、PlayMode `UnityTest`、现有 `STGEngine.Core/Runtime/Editor` 程序集、后续可接 `com.coplaydev.unity-mcp`。

---

## File Structure

### Existing files to reuse
- `Assets/STGEngine/Core/STGEngine.Core.asmdef` — 现有核心程序集。
- `Assets/STGEngine/Runtime/STGEngine.Runtime.asmdef` — 现有运行时程序集。
- `Assets/STGEngine/Editor/STGEngine.Editor.asmdef` — 现有编辑器程序集。
- `Assets/STGEngine/Editor/Commands/CommandStack.cs` — 首批 EditMode 测试目标之一。
- `Assets/STGEngine/Editor/UI/FileManager/OverrideManager.cs` — 覆盖层路径与保存逻辑测试目标。
- `Assets/STGEngine/Editor/UI/FileManager/STGCatalog.cs` — 资源目录逻辑测试目标。
- `Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs` — 场景集成测试的现成引导入口。
- `Assets/STGEngine/Runtime/Preview/PatternPreviewer.cs` — Pattern 预览测试入口候选。
- `Assets/STGEngine/Runtime/Preview/TimelinePlaybackController.cs` — Timeline 工作流测试入口候选。

### New files to create
- `Assets/STGEngine/Tests/EditMode/STGEngine.Tests.EditMode.asmdef` — EditMode 测试程序集。
- `Assets/STGEngine/Tests/PlayMode/STGEngine.Tests.PlayMode.asmdef` — PlayMode 测试程序集。
- `Assets/STGEngine/Tests/Shared/STGEngine.Tests.Shared.asmdef` — 测试共享工具程序集。
- `Assets/STGEngine/Tests/Shared/Results/TestRunRecord.cs` — 统一结构化测试结果数据模型。
- `Assets/STGEngine/Tests/Shared/Results/TestResultCapture.cs` — 记录步骤、异常、附件路径、通过失败。
- `Assets/STGEngine/Tests/Shared/Results/ConsoleLogCollector.cs` — 捕获 Console 错误、异常、断言。
- `Assets/STGEngine/Tests/Shared/Results/TestSnapshotExporter.cs` — 导出 JSON 状态快照。
- `Assets/STGEngine/Tests/Shared/Runtime/TestArtifactPaths.cs` — 统一测试产物路径生成。
- `Assets/STGEngine/TestRuntime/Harness/SceneIntegrationRunner.cs` — 运行时场景集成测试入口。
- `Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs` — Pattern 预览与 Timeline 回放统一入口。
- `Assets/STGEngine/Editor/TestTools/EditorTestFacade.cs` — 编辑器侧统一测试入口。
- `Assets/STGEngine/Editor/TestTools/PatternWorkflowTestFacade.cs` — Pattern 工作流编辑器入口。
- `Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs` — Timeline 工作流编辑器入口。
- `Assets/STGEngine/Tests/EditMode/Editor/CommandStackTests.cs` — 命令栈 EditMode 测试。
- `Assets/STGEngine/Tests/EditMode/Editor/OverrideManagerTests.cs` — 覆盖层路径/保存测试。
- `Assets/STGEngine/Tests/EditMode/Editor/STGCatalogTests.cs` — 资源目录逻辑测试。
- `Assets/STGEngine/Tests/PlayMode/Scene/SceneIntegrationRunnerTests.cs` — 场景集成 PlayMode 测试。
- `Assets/STGEngine/Tests/PlayMode/Preview/PatternPreviewRunnerTests.cs` — Pattern E2E 骨架测试。
- `Assets/STGEngine/Tests/PlayMode/Preview/TimelineWorkflowRunnerTests.cs` — Timeline E2E 骨架测试。
- `Assets/STGEngine/Tests/PlayMode/Scene/SceneWorkflowRunnerTests.cs` — Scene E2E 骨架测试。
- `Assets/STGEngine/TestScenes/Scene_RuntimeIntegration.unity` — 运行时集成测试场景。
- `Assets/STGEngine/TestScenes/Scene_PatternPreview.unity` — Pattern 预览测试场景。
- `Assets/STGEngine/TestScenes/Scene_TimelineWorkflow.unity` — Timeline 工作流测试场景。
- `docs/superpowers/plans/2026-03-01-unity-ai-automation-testing-foundation.md` — 当前计划文档。

### Existing files to modify
- `Assets/STGEngine/Editor/STGEngine.Editor.asmdef` — 如需引用新测试工具所在共享程序集时调整引用策略（仅在必要时）。
- `Assets/STGEngine/Runtime/STGEngine.Runtime.asmdef` — 如运行时测试工具需被 PlayMode 测试程序集消费时确认引用边界（通常无需改动，优先由测试程序集引用 Runtime）。
- `Packages/manifest.json` — 仅在确认还缺少测试依赖时才改；当前 `com.unity.test-framework` 已存在，原则上不动。

---

## Tasks

1. 建立测试程序集与目录骨架
2. 搭建结构化结果捕获基础设施
3. 搭建编辑器侧 Test Facade
4. 为 CommandStack、OverrideManager、STGCatalog 建立首批 EditMode 测试
5. 搭建运行时 Runner 与测试场景骨架
6. 建立 Pattern / Timeline / Scene 三条 E2E 骨架测试
7. 编写计划文档并记录执行入口
