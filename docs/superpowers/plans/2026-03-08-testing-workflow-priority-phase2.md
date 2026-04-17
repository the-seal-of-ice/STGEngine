# Testing Workflow Priority Phase 2 Implementation Plan

> **状态：已完成** — 合并到 main `cc9edaa`，EditMode + PlayMode 全部通过（2026-04-13）
>
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有 Unity 测试基础设施从 smoke 级骨架推进到可驱动 Timeline / Pattern / Scene 真实工作流、可输出诊断产物、可为镜头脚本和后续系统提供回归保障的第二阶段测试系统。

**Architecture:** 继续沿用“Editor Facade → TestRuntime Runner → 结构化结果产物 → PlayMode / EditMode 测试”的分层架构，但把当前只负责等待与写 result.json 的 runner 升级为真正驱动业务对象并进行语义断言的工作流入口。诊断产物统一挂到 `TestRunRecord`，并优先补齐 Timeline 主链，再补强 Scene 集成与相机脚本回归用例。

**Tech Stack:** Unity Test Framework、NUnit、PlayMode `UnityTest`、现有 `STGEngine.Editor` / `STGEngine.Runtime` / `STGEngine.TestRuntime` 程序集、JSON 结果导出、git worktree / feature branch / frequent commits。

---

## File Structure

### Existing files to modify
- `Assets/STGEngine/Editor/TestTools/PatternWorkflowTestFacade.cs` — 从简单 request 构造器升级为带最小测试输入的 Pattern workflow 准备入口。
- `Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs` — 补齐 Timeline workflow request 的最小输入信息，支持 runtime runner 消费。
- `Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs` — 从“等待几秒后通过”升级为真实预览 workflow runner，记录快照/步骤/附件。
- `Assets/STGEngine/TestRuntime/Harness/SceneIntegrationRunner.cs` — 接入 `SceneTestSetup` 与真实场景稳定性断言。
- `Assets/STGEngine/TestRuntime/Results/TestRunRecord.cs` — 增加更明确的工作流步骤与诊断字段（如需保持兼容则只追加字段）。
- `Assets/STGEngine/TestRuntime/Results/TestResultCapture.cs` — 让记录对象能挂接 snapshot / screenshot / attachments。
- `Assets/STGEngine/TestRuntime/Results/TestSnapshotExporter.cs` — 支持写 workflow snapshot，不仅是最终 result。
- `Assets/STGEngine/TestRuntime/Runtime/TestArtifactPaths.cs` — 明确 result/snapshot/screenshot 命名约定。
- `Assets/STGEngine/Tests/PlayMode/Preview/PatternPreviewRunnerTests.cs` — 用真实 runner 行为替代当前 smoke 断言。
- `Assets/STGEngine/Tests/PlayMode/Scene/SceneIntegrationRunnerTests.cs` — 用真实场景集成断言替代纯等待断言。
- `Assets/STGEngine/Tests/PlayMode/Scene/SceneWorkflowRunnerTests.cs` — 扩展 Scene workflow 产物与稳定性断言。
- `Assets/STGEngine/Tests/EditMode/Editor/TimelineWorkflowFacadeTests.cs` — 补 request 结构与输入约束测试。

### New files to create
- `Assets/STGEngine/TestRuntime/Harness/TimelineWorkflowRunner.cs` — Timeline runtime workflow 入口，驱动 `TimelinePlaybackController` 并输出结构化结果。
- `Assets/STGEngine/Tests/PlayMode/Preview/TimelineWorkflowRunnerTests.cs` — Timeline workflow 骨架测试，覆盖 request → playback → result 这条主链。
- `Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs` — 统一的 workflow snapshot 模型，描述当前时间、活跃事件数、阻塞状态等。
- `Assets/STGEngine/Tests/PlayMode/Camera/CameraScriptWorkflowTests.cs` — 首批 CameraScript 回归用例，基于测试系统 PlayMode 主链运行。
- `doc/plans/2026-03-08-testing-workflow-priority-phase2-dispatch.md` — 任务分派说明，给多 subagent 执行时明确 git 规范与边界。

### Existing files to reference but usually not modify
- `Assets/STGEngine/Runtime/Preview/TimelinePlaybackController.cs` — Timeline workflow 的真实被测对象。
- `Assets/STGEngine/Runtime/Preview/PatternPreviewer.cs` — Pattern workflow 的真实被测对象。
- `Assets/STGEngine/Runtime/Scene/SceneTestSetup.cs` — Scene workflow 的真实被测对象。
- `Assets/STGEngine/Runtime/Scene/CameraScriptPlayer.cs` — 第一批镜头脚本回归用例的真实被测对象。
- `docs/superpowers/plans/2026-03-01-unity-ai-automation-testing-foundation.md` — 第一期计划，用于对齐已实现内容。

---

## Execution Strategy

本计划按可并行的 4 个子任务拆分，每个子任务都要求使用独立 git worktree 或独立分支，且每个 subagent 必须在开始与结束时完成自己的 git 检查。

### Git rules for every subagent
- 开工前：
  - 运行 `git status --short`
  - 确认当前基础分支与工作区干净程度
  - 新建自己的 worktree/分支，例如 `feature/test-phase2-timeline-runner`
- 开发中：
  - 只修改自己任务声明中的文件
  - 小步提交，不 amend 旧提交
- 完工前：
  - 运行针对本任务的测试命令
  - 再运行 `git status --short`
  - 创建新提交
  - 汇报分支名、提交 hash、验证命令结果

建议总控先创建 4 个独立任务：
1. Timeline workflow 主链
2. Preview/Scene runner 诊断产物
3. EditMode facade 补强
4. CameraScript 首批回归用例

---

## Tasks

### Task 1: 定义统一 workflow snapshot 与产物命名

**Files:**
- Create: `Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs`
- Modify: `Assets/STGEngine/TestRuntime/Results/TestRunRecord.cs`
- Modify: `Assets/STGEngine/TestRuntime/Results/TestResultCapture.cs`
- Modify: `Assets/STGEngine/TestRuntime/Results/TestSnapshotExporter.cs`
- Modify: `Assets/STGEngine/TestRuntime/Runtime/TestArtifactPaths.cs`
- Test: `Assets/STGEngine/Tests/EditMode/Editor/TestResultCaptureTests.cs`

- [ ] **Step 1: 写一个失败的 EditMode 测试，要求结果记录能保存 snapshot/attachment 路径**

```csharp
[Test]
public void Capture_CanStoreSnapshotAndAttachmentPaths()
{
    using var capture = new TestResultCapture("workflow-test", "PlayMode", "Scene_TimelineWorkflow");

    capture.AddStep("prepare");
    capture.SetSnapshotPath("Temp/STGEngineTestArtifacts/workflow-test/snapshot.json");
    capture.AddAttachment("Temp/STGEngineTestArtifacts/workflow-test/extra.txt");
    capture.MarkPassed();

    Assert.That(capture.Record.SnapshotPath, Is.EqualTo("Temp/STGEngineTestArtifacts/workflow-test/snapshot.json"));
    Assert.That(capture.Record.Attachments, Has.Member("Temp/STGEngineTestArtifacts/workflow-test/extra.txt"));
}
```

- [ ] **Step 2: 只运行该测试，确认当前失败**

Run: `dotnet test "STGEngine.Tests.EditMode.csproj" --filter "Capture_CanStoreSnapshotAndAttachmentPaths"`
Expected: FAIL，提示 `TestResultCapture` 缺少 `SetSnapshotPath` 或 `AddAttachment`。

- [ ] **Step 3: 新建统一 snapshot 模型，定义最小字段**

```csharp
using System;

namespace STGEngine.TestRuntime.Results
{
    [Serializable]
    public class TestWorkflowSnapshot
    {
        public string WorkflowName;
        public float CurrentTime;
        public int ActiveEventCount;
        public bool IsBlocked;
        public string BlockingEventId;
        public string Notes;
    }
}
```

- [ ] **Step 4: 给 `TestRunRecord` 增加可稳定承载诊断产物的字段**

```csharp
public List<string> Attachments = new();
public string SnapshotPath;
public string ScreenshotPath;
```

如果文件里已有这些字段，则保持字段名不变，不重复新增，并只补充注释说明用途。

- [ ] **Step 5: 给 `TestResultCapture` 增加最小辅助方法**

```csharp
public void SetSnapshotPath(string path)
{
    Record.SnapshotPath = path;
}

public void SetScreenshotPath(string path)
{
    Record.ScreenshotPath = path;
}

public void AddAttachment(string path)
{
    if (!string.IsNullOrEmpty(path) && !Record.Attachments.Contains(path))
        Record.Attachments.Add(path);
}
```

- [ ] **Step 6: 在 `TestArtifactPaths` 中统一产物路径生成方法**

```csharp
public static string GetSnapshotPath(string testName)
{
    return Path.Combine(GetArtifactDirectory(testName), "snapshot.json");
}

public static string GetScreenshotPath(string testName)
{
    return Path.Combine(GetArtifactDirectory(testName), "screenshot.png");
}
```

如果同名方法已存在，则只确保命名与目录规则一致。

- [ ] **Step 7: 让 `TestSnapshotExporter` 可直接导出 workflow snapshot**

```csharp
public static void WriteWorkflowSnapshot(string path, TestWorkflowSnapshot snapshot)
{
    WriteJson(path, snapshot);
}
```

- [ ] **Step 8: 运行 EditMode 测试，确认通过**

Run: `dotnet test "STGEngine.Tests.EditMode.csproj" --filter "TestResultCaptureTests"`
Expected: PASS。

- [ ] **Step 9: 提交这一小步**

```bash
git add Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs Assets/STGEngine/TestRuntime/Results/TestRunRecord.cs Assets/STGEngine/TestRuntime/Results/TestResultCapture.cs Assets/STGEngine/TestRuntime/Results/TestSnapshotExporter.cs Assets/STGEngine/TestRuntime/Runtime/TestArtifactPaths.cs Assets/STGEngine/Tests/EditMode/Editor/TestResultCaptureTests.cs
git commit -m "test: add workflow artifact metadata support"
```

### Task 2: 补齐 Timeline workflow 主链 runner 与 PlayMode 测试

**Files:**
- Create: `Assets/STGEngine/TestRuntime/Harness/TimelineWorkflowRunner.cs`
- Create: `Assets/STGEngine/Tests/PlayMode/Preview/TimelineWorkflowRunnerTests.cs`
- Modify: `Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs`
- Modify: `Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs`
- Test: `Assets/STGEngine/Tests/PlayMode/Preview/TimelineWorkflowRunnerTests.cs`

- [ ] **Step 1: 先写失败的 Timeline workflow PlayMode 测试，要求 runner 能输出 snapshot/result**

```csharp
[UnityTest]
public IEnumerator TimelineWorkflowRunner_ExportsResultAndSnapshot()
{
    var request = TimelineWorkflowTestFacade.CreateWorkflowRequest("demo-segment");
    var go = new GameObject("timeline-runner");
    var runner = go.AddComponent<TimelineWorkflowRunner>();

    yield return runner.RunWorkflow(request, 0.2f);

    Assert.That(runner.LastRecord, Is.Not.Null);
    Assert.That(runner.LastRecord.Status, Is.EqualTo("Passed"));
    Assert.That(runner.LastRecord.SnapshotPath, Is.Not.Null.And.Not.Empty);
    Assert.That(System.IO.File.Exists(runner.LastRecord.SnapshotPath), Is.True);
}
```

- [ ] **Step 2: 运行该单测，确认当前失败**

Run: `dotnet test "STGEngine.Tests.PlayMode.csproj" --filter "TimelineWorkflowRunner_ExportsResultAndSnapshot"`
Expected: FAIL，提示 `TimelineWorkflowRunner` 不存在或 `RunWorkflow` 未定义。

- [ ] **Step 3: 扩展 `TimelineWorkflowRequest`，补最小 workflow 输入字段**

```csharp
public class TimelineWorkflowRequest
{
    public string SegmentId;
    public string Status;
    public string RequestName;
    public float DurationSeconds;
    public bool ExpectBlocking;
}
```

并让工厂方法返回稳定默认值：

```csharp
public static TimelineWorkflowRequest CreateWorkflowRequest(string segmentId)
{
    return new TimelineWorkflowRequest
    {
        SegmentId = segmentId,
        Status = "Prepared",
        RequestName = $"timeline-workflow-{segmentId}",
        DurationSeconds = 0.2f,
        ExpectBlocking = false
    };
}
```

- [ ] **Step 4: 实现最小 `TimelineWorkflowRunner`，至少驱动时间推进并导出结果/快照**

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using STGEngine.Editor.TestTools;
using STGEngine.TestRuntime.Results;
using STGEngine.TestRuntime.Runtime;

namespace STGEngine.TestRuntime.Harness
{
    public class TimelineWorkflowRunner : MonoBehaviour
    {
        public TestRunRecord LastRecord { get; private set; }

        public IEnumerator RunWorkflow(TimelineWorkflowRequest request, float seconds)
        {
            using var capture = new TestResultCapture(request.RequestName, "PlayMode", SceneManager.GetActiveScene().name);
            capture.AddStep("timeline-workflow-start");

            yield return new WaitForSeconds(seconds);

            var snapshot = new TestWorkflowSnapshot
            {
                WorkflowName = request.RequestName,
                CurrentTime = seconds,
                ActiveEventCount = 0,
                IsBlocked = false,
                BlockingEventId = string.Empty,
                Notes = request.SegmentId
            };

            var snapshotPath = TestArtifactPaths.GetSnapshotPath(request.RequestName);
            TestSnapshotExporter.WriteWorkflowSnapshot(snapshotPath, snapshot);
            capture.SetSnapshotPath(snapshotPath);
            capture.AddStep("timeline-workflow-finish");
            capture.MarkPassed();

            LastRecord = capture.Record;
            TestSnapshotExporter.WriteJson(TestArtifactPaths.GetResultPath(request.RequestName), LastRecord);
        }
    }
}
```

- [ ] **Step 5: 把 runner 升级为真正接入 `TimelinePlaybackController` 的最小实现**

关键要求：
- 在 runner 内创建 `TimelinePlaybackController`
- 记录 `CurrentTime`
- 把 `IsBlocked` / `BlockingEvent?.Id` 写入 snapshot
- 至少调用一次 `Play()` 与多次 `Tick()`，而不是纯 `WaitForSeconds`

最小骨架代码应包含类似逻辑：

```csharp
var controller = new TimelinePlaybackController();
controller.Play();
float elapsed = 0f;
while (elapsed < seconds)
{
    controller.Tick(Time.deltaTime);
    elapsed += Time.deltaTime;
    yield return null;
}
```

- [ ] **Step 6: 运行 Timeline PlayMode 测试，确认通过**

Run: `dotnet test "STGEngine.Tests.PlayMode.csproj" --filter "TimelineWorkflowRunnerTests"`
Expected: PASS。

- [ ] **Step 7: 提交这一小步**

```bash
git add Assets/STGEngine/TestRuntime/Harness/TimelineWorkflowRunner.cs Assets/STGEngine/Tests/PlayMode/Preview/TimelineWorkflowRunnerTests.cs Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs Assets/STGEngine/TestRuntime/Results/TestWorkflowSnapshot.cs
git commit -m "test: add timeline workflow runner skeleton"
```

### Task 3: 升级 Preview/Scene runner 为真实 workflow 入口

**Files:**
- Modify: `Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs`
- Modify: `Assets/STGEngine/TestRuntime/Harness/SceneIntegrationRunner.cs`
- Modify: `Assets/STGEngine/Tests/PlayMode/Preview/PatternPreviewRunnerTests.cs`
- Modify: `Assets/STGEngine/Tests/PlayMode/Scene/SceneIntegrationRunnerTests.cs`
- Modify: `Assets/STGEngine/Tests/PlayMode/Scene/SceneWorkflowRunnerTests.cs`
- Test: `Assets/STGEngine/Tests/PlayMode/Preview/PatternPreviewRunnerTests.cs`
- Test: `Assets/STGEngine/Tests/PlayMode/Scene/SceneIntegrationRunnerTests.cs`

- [ ] **Step 1: 写失败的 Pattern runner 测试，要求不只检查 LastRecord，还检查 snapshot 输出与步骤记录**

```csharp
[UnityTest]
public IEnumerator PreviewRunner_WritesSnapshotAndWorkflowSteps()
{
    var go = new GameObject("preview-runner");
    var runner = go.AddComponent<PreviewTestRunner>();

    yield return runner.RunPreview(null, 0.1f, "pattern-preview-smoke");

    Assert.That(runner.LastRecord, Is.Not.Null);
    Assert.That(runner.LastRecord.Steps, Has.Member("preview-start"));
    Assert.That(runner.LastRecord.Steps, Has.Member("preview-finish"));
    Assert.That(runner.LastRecord.SnapshotPath, Is.Not.Null.And.Not.Empty);
}
```

- [ ] **Step 2: 写失败的 Scene runner 测试，要求记录场景稳定性信息**

```csharp
[UnityTest]
public IEnumerator SceneRunner_WritesSnapshotAndPassesWithoutConsoleErrors()
{
    var go = new GameObject("scene-runner");
    var runner = go.AddComponent<SceneIntegrationRunner>();

    yield return runner.RunForSeconds(0.1f);

    Assert.That(runner.LastRecord, Is.Not.Null);
    Assert.That(runner.LastRecord.Status, Is.EqualTo("Passed"));
    Assert.That(runner.LastRecord.SnapshotPath, Is.Not.Null.And.Not.Empty);
    Assert.That(runner.LastRecord.ConsoleErrors, Is.Empty);
}
```

- [ ] **Step 3: 运行两个单测，确认当前失败**

Run: `dotnet test "STGEngine.Tests.PlayMode.csproj" --filter "PreviewRunner_WritesSnapshotAndWorkflowSteps|SceneRunner_WritesSnapshotAndPassesWithoutConsoleErrors"`
Expected: FAIL，因为 runner 还未设置 `SnapshotPath`。

- [ ] **Step 4: 升级 `PreviewTestRunner`，让它导出 workflow snapshot**

最小结果应包含：

```csharp
var snapshot = new TestWorkflowSnapshot
{
    WorkflowName = testName,
    CurrentTime = seconds,
    ActiveEventCount = 0,
    IsBlocked = false,
    Notes = previewer == null ? "previewer-null" : "previewer-provided"
};

var snapshotPath = TestArtifactPaths.GetSnapshotPath(testName);
TestSnapshotExporter.WriteWorkflowSnapshot(snapshotPath, snapshot);
capture.SetSnapshotPath(snapshotPath);
```

- [ ] **Step 5: 升级 `SceneIntegrationRunner`，接入 `SceneTestSetup` 的最小状态检查**

要求至少做以下检查之一：
- 场景中存在 `SceneTestSetup`
- 或创建后能运行若干帧不报错
- snapshot 中写明 `SceneTestSetup` 是否存在

最小 snapshot 示例：

```csharp
var setup = FindAnyObjectByType<STGEngine.Runtime.Scene.SceneTestSetup>();
var snapshot = new TestWorkflowSnapshot
{
    WorkflowName = "scene-runtime-integration",
    CurrentTime = seconds,
    ActiveEventCount = setup == null ? 0 : 1,
    IsBlocked = false,
    Notes = setup == null ? "scene-setup-missing" : "scene-setup-found"
};
```

- [ ] **Step 6: 运行 Pattern/Scene PlayMode 测试，确认通过**

Run: `dotnet test "STGEngine.Tests.PlayMode.csproj" --filter "PatternPreviewRunnerTests|SceneIntegrationRunnerTests|SceneWorkflowRunnerTests"`
Expected: PASS。

- [ ] **Step 7: 提交这一小步**

```bash
git add Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs Assets/STGEngine/TestRuntime/Harness/SceneIntegrationRunner.cs Assets/STGEngine/Tests/PlayMode/Preview/PatternPreviewRunnerTests.cs Assets/STGEngine/Tests/PlayMode/Scene/SceneIntegrationRunnerTests.cs Assets/STGEngine/Tests/PlayMode/Scene/SceneWorkflowRunnerTests.cs
git commit -m "test: export workflow snapshots for preview and scene runners"
```

### Task 4: 补强 EditMode facade 套件，保证 workflow request 有稳定输入

**Files:**
- Modify: `Assets/STGEngine/Editor/TestTools/PatternWorkflowTestFacade.cs`
- Modify: `Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs`
- Modify: `Assets/STGEngine/Tests/EditMode/Editor/TimelineWorkflowFacadeTests.cs`
- Create: `Assets/STGEngine/Tests/EditMode/Editor/PatternWorkflowFacadeTests.cs`
- Test: `Assets/STGEngine/Tests/EditMode/Editor/PatternWorkflowFacadeTests.cs`
- Test: `Assets/STGEngine/Tests/EditMode/Editor/TimelineWorkflowFacadeTests.cs`

- [ ] **Step 1: 先写失败的 Pattern facade 测试，要求 request 带稳定默认时长与 Prepared 状态**

```csharp
[Test]
public void CreatePreviewRequest_ReturnsPreparedRequestWithStableDefaults()
{
    var request = PatternWorkflowTestFacade.CreatePreviewRequest("demo-pattern");

    Assert.That(request.PatternId, Is.EqualTo("demo-pattern"));
    Assert.That(request.Status, Is.EqualTo("Prepared"));
    Assert.That(request.RequestName, Is.EqualTo("pattern-preview-demo-pattern"));
}
```

- [ ] **Step 2: 补一个失败的 Timeline facade 测试，要求 request 带默认 duration**

```csharp
[Test]
public void CreateWorkflowRequest_ReturnsPreparedRequestWithDuration()
{
    var request = TimelineWorkflowTestFacade.CreateWorkflowRequest("demo-segment");

    Assert.That(request.SegmentId, Is.EqualTo("demo-segment"));
    Assert.That(request.Status, Is.EqualTo("Prepared"));
    Assert.That(request.DurationSeconds, Is.GreaterThan(0f));
}
```

- [ ] **Step 3: 运行 facade 测试，确认当前至少有一项失败**

Run: `dotnet test "STGEngine.Tests.EditMode.csproj" --filter "PatternWorkflowFacadeTests|TimelineWorkflowFacadeTests"`
Expected: FAIL，因为 Pattern facade 测试文件不存在，或 Timeline request 缺少 `DurationSeconds`。

- [ ] **Step 4: 让两个 facade 统一提供最小 workflow 默认输入**

Pattern request 至少保持：

```csharp
public class PatternPreviewRequest
{
    public string PatternId;
    public string Status;
    public string RequestName;
}
```

Timeline request 至少保持：

```csharp
public float DurationSeconds;
public bool ExpectBlocking;
```

- [ ] **Step 5: 补齐/更新 EditMode 测试并确保通过**

Run: `dotnet test "STGEngine.Tests.EditMode.csproj" --filter "PatternWorkflowFacadeTests|TimelineWorkflowFacadeTests"`
Expected: PASS。

- [ ] **Step 6: 提交这一小步**

```bash
git add Assets/STGEngine/Editor/TestTools/PatternWorkflowTestFacade.cs Assets/STGEngine/Editor/TestTools/TimelineWorkflowTestFacade.cs Assets/STGEngine/Tests/EditMode/Editor/PatternWorkflowFacadeTests.cs Assets/STGEngine/Tests/EditMode/Editor/TimelineWorkflowFacadeTests.cs
git commit -m "test: stabilize workflow facade requests"
```

### Task 5: 接入首批 CameraScript 回归用例，作为测试系统优先方案的第一批真实消费者

**Files:**
- Create: `Assets/STGEngine/Tests/PlayMode/Camera/CameraScriptWorkflowTests.cs`
- Modify: `Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs`
- Reference: `Assets/STGEngine/Runtime/Scene/CameraScriptPlayer.cs`
- Test: `Assets/STGEngine/Tests/PlayMode/Camera/CameraScriptWorkflowTests.cs`

- [ ] **Step 1: 写失败的 CameraScript PlayMode 用例，至少覆盖最基础的播放与状态变化**

```csharp
[UnityTest]
public IEnumerator CameraScriptPlayer_Play_ActivatesAndEventuallyStops()
{
    var cameraGo = new GameObject("Main Camera");
    cameraGo.tag = "MainCamera";
    cameraGo.AddComponent<Camera>();

    var runnerGo = new GameObject("camera-script-player");
    var player = runnerGo.AddComponent<STGEngine.Runtime.Scene.CameraScriptPlayer>();
    player.Initialize(new StubCameraFrameProvider());

    var script = new STGEngine.Core.Timeline.CameraScriptParams();
    script.Keyframes.Add(new STGEngine.Core.Scene.CameraKeyframe { Time = 0f, PositionOffset = Vector3.zero, Rotation = Quaternion.identity, FOV = 60f });
    script.Keyframes.Add(new STGEngine.Core.Scene.CameraKeyframe { Time = 0.05f, PositionOffset = new Vector3(0f, 1f, -3f), Rotation = Quaternion.identity, FOV = 55f });
    script.BlendIn = 0f;
    script.BlendOut = 0f;

    player.Play(script);
    Assert.That(player.IsActive, Is.True);

    yield return new WaitForSeconds(0.1f);

    Assert.That(player.IsActive, Is.False);
}
```

- [ ] **Step 2: 运行该单测，确认当前失败或编译失败**

Run: `dotnet test "STGEngine.Tests.PlayMode.csproj" --filter "CameraScriptPlayer_Play_ActivatesAndEventuallyStops"`
Expected: FAIL，原因可能是 stub provider 缺失或字段名与现实现不一致。

- [ ] **Step 3: 添加最小测试桩，仅为当前用例服务**

```csharp
private sealed class StubCameraFrameProvider : STGEngine.Core.Scene.ICameraFrameProvider
{
    public Vector3 PlayerWorldPosition => Vector3.zero;
    public Vector3 FrameRight => Vector3.right;
    public Vector3 FrameUp => Vector3.up;
    public Vector3 FrameForward => Vector3.forward;
}
```

- [ ] **Step 4: 根据现实现修正测试，使其验证真实可观察行为**

允许的最小断言包括：
- `IsActive` 先变为 true 后回到 false
- `Camera.main.fieldOfView` 发生变化
- 运行过程中无 `ConsoleErrors`

不要在这一步引入 Persist/Revert/AimMode 多分支；只保留最小高价值回归用例。

- [ ] **Step 5: 运行 CameraScript PlayMode 测试，确认通过**

Run: `dotnet test "STGEngine.Tests.PlayMode.csproj" --filter "CameraScriptWorkflowTests"`
Expected: PASS。

- [ ] **Step 6: 提交这一小步**

```bash
git add Assets/STGEngine/Tests/PlayMode/Camera/CameraScriptWorkflowTests.cs Assets/STGEngine/TestRuntime/Harness/PreviewTestRunner.cs
git commit -m "test: add camera script workflow regression"
```

### Task 6: 写任务分派文档，约束 subagent 的 git 工作方式

**Files:**
- Create: `doc/plans/2026-03-08-testing-workflow-priority-phase2-dispatch.md`

- [ ] **Step 1: 写明 4 个可并行子任务与文件边界**

文档应包含如下结构：

```md
# Testing Workflow Priority Phase 2 Dispatch

## Shared rules
- 每个子代理必须使用独立 git worktree 或独立 feature branch
- 开工前执行 git status --short
- 完工前执行针对本任务的测试命令
- 完工后执行 git status --short 并创建新提交
- 回复必须包含：branch、commit、tests、modified files

## Agent 1
- Scope: Timeline workflow runner
- Files: ...
- Deliverable: ...

## Agent 2
- Scope: Preview/Scene runner artifacts
- Files: ...
- Deliverable: ...

## Agent 3
- Scope: Facade tests
- Files: ...
- Deliverable: ...

## Agent 4
- Scope: CameraScript regression tests
- Files: ...
- Deliverable: ...
```

- [ ] **Step 2: 保存文档并自查是否与本计划任务边界一致**

检查项：
- Agent 1 只负责 Timeline 相关文件
- Agent 2 不修改 CameraScript 逻辑
- Agent 3 只做 EditMode facade
- Agent 4 只写首批 CameraScript regression tests

- [ ] **Step 3: 提交文档**

```bash
git add doc/plans/2026-03-08-testing-workflow-priority-phase2-dispatch.md docs/superpowers/plans/2026-03-08-testing-workflow-priority-phase2.md
git commit -m "docs: add testing workflow phase 2 dispatch plan"
```

---

## Self-Review

- 规格覆盖检查：本计划覆盖了 Timeline 主链、Preview/Scene runner 真实产物、Facade 默认输入稳定性，以及 CameraScript 作为第一批 workflow 消费者的回归用例。
- 占位符检查：没有使用 TBD/TODO/implement later 等占位词；每个任务都给出了明确文件与命令。
- 类型一致性检查：`TimelineWorkflowRequest`、`TestWorkflowSnapshot`、`SetSnapshotPath`、`AddAttachment`、`TimelineWorkflowRunner` 在任务中命名一致。

---

计划执行时，推荐总控先建立 worktree，再按 Task 2 / Task 3 / Task 4 / Task 5 并行分派，Task 1 作为所有子任务都可依赖的先行小任务执行。
