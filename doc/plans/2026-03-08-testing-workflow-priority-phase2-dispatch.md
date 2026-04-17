# Testing Workflow Priority Phase 2 Dispatch

## Shared rules
- 每个子代理必须使用独立 git worktree 或独立 feature branch。
- 开工前必须执行 `git status --short`，并在汇报中说明基线状态。
- 开工前必须创建并切换到自己的任务分支，推荐命名：
  - `feature/test-phase2-artifacts`
  - `feature/test-phase2-timeline`
  - `feature/test-phase2-facades`
  - `feature/test-phase2-camera-regression`
- 开发中只能修改自己任务声明中的文件；如发现必须跨边界修改，先回报总控，不得直接扩散范围。
- 完工前必须运行本任务指定的测试命令。
- 完工前必须再次执行 `git status --short`。
- 完工后必须创建一个新提交，不允许 amend 旧提交。
- 回复必须包含：
  - branch 名称
  - commit hash
  - 执行过的测试命令与结果
  - 修改文件列表
  - 是否存在后续风险/阻塞

## Agent 1 — Workflow artifacts
- Scope: 统一 snapshot / attachment / screenshot 产物元数据。
- Files:
  - `Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs`
  - `Assets/STGEngine/TestRuntime/Results/TestRunRecord.cs`
  - `Assets/STGEngine/TestRuntime/Results/TestResultCapture.cs`
  - `Assets/STGEngine/TestRuntime/Results/TestSnapshotExporter.cs`
  - `Assets/STGEngine/TestRuntime/Runtime/TestArtifactPaths.cs`
  - `Assets/STGEngine/Tests/EditMode/Editor/TestResultCaptureTests.cs`
- Deliverable:
  - `TestResultCapture` 可写入 snapshotPath / screenshotPath / attachments
  - EditMode 测试通过

## Agent 2 — Timeline workflow
- Scope: 补齐 Timeline workflow runner 与 PlayMode 测试主链。
- Files:
  - `Assets/STGEngine/TestRuntime/Harness/TimelineWorkflowRunner.cs`
  - `Assets/STGEngine/Tests/PlayMode/Preview/TimelineWorkflowRunnerTests.cs`
  - `Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs`
  - `Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs`
- Deliverable:
  - Timeline workflow request → runner → result/snapshot 主链可跑
  - Timeline PlayMode 测试通过

## Agent 3 — Preview/Scene + Facades
- Scope: 升级 Preview/Scene runner 的 workflow 产物，并补强 Pattern/Timeline facades 的默认输入。
- Files:
  - `Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs`
  - `Assets/STGEngine/TestRuntime/Harness/SceneIntegrationRunner.cs`
  - `Assets/STGEngine/Tests/PlayMode/Preview/PatternPreviewRunnerTests.cs`
  - `Assets/STGEngine/Tests/PlayMode/Scene/SceneIntegrationRunnerTests.cs`
  - `Assets/STGEngine/Tests/PlayMode/Scene/SceneWorkflowRunnerTests.cs`
  - `Assets/STGEngine/Editor/TestTools/PatternWorkflowTestFacade.cs`
  - `Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs`
  - `Assets/STGEngine/Tests/EditMode/Editor/PatternWorkflowFacadeTests.cs`
  - `Assets/STGEngine/Tests/EditMode/Editor/TimelineWorkflowFacadeTests.cs`
- Deliverable:
  - Preview / Scene runner 可输出 snapshot
  - Pattern / Timeline facade 默认输入稳定
  - 相关 EditMode / PlayMode 测试通过

## Agent 4 — CameraScript regression
- Scope: 接入首批 CameraScript PlayMode 回归用例，作为测试系统的真实消费者。
- Files:
  - `Assets/STGEngine/Tests/PlayMode/Camera/CameraScriptWorkflowTests.cs`
  - （如必须）只允许最小范围修改 `Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs` 用于共用辅助逻辑
- Deliverable:
  - 至少一个稳定的 CameraScript 基础播放回归用例
  - CameraScript PlayMode 测试通过

## Coordination notes
- Agent 1 应最先执行，因为它提供统一的产物接口。
- Agent 2 依赖 Agent 1 的 snapshot API；如果并行，需优先拉取 Agent 1 的结果或在自己的分支临时适配后再 rebase。
- Agent 3 可与 Agent 2 并行，但不要与 Agent 2 同时改同一 facade 字段；若 Timeline facade 发生冲突，以 Agent 2 的 workflow 字段为主。
- Agent 4 应避免改 `CameraScriptPlayer.cs`；本阶段只建立回归测试，不扩大功能范围。
