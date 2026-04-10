# Unity AI 自动化测试方案设计（STGEngine）

## 目标

为当前 STGEngine 项目建立一套适合 AI 自动执行、适合长期回归、适合后续接入 CI 的 Unity 自动化测试体系。该体系应同时覆盖编辑器工具、运行时场景链路与少量高价值端到端流程，并输出结构化结果，便于 AI 自动分析失败原因。

## 当前项目现状

通过现有项目结构可确认：

- 已安装 `com.unity.test-framework`，可直接使用 Unity Test Framework 作为测试底座。
- 已安装 `com.coplaydev.unity-mcp`，具备 AI 与 Unity 建立控制桥梁的基础。
- 项目包含明显的 Editor + Runtime 双侧代码结构，并大量使用 UI Toolkit。
- 当前已有 `SceneTestSetup.cs` 这类手工集成验证入口，但尚未形成正式测试目录与系统化测试框架。
- 最近提交主要聚焦相机系统与玩家/瞄准能力，说明运行时与演出链路变化频繁，存在持续回归需求。

## 设计目标

这套方案的主要目标如下：

1. 让 AI 能稳定触发测试，而不是依赖人工打开编辑器和手工点击。
2. 让自动化测试覆盖编辑器工作流、运行时玩法链路、关键端到端路径。
3. 让失败结果可结构化读取，便于 AI 自动分析与生成诊断结论。
4. 为后续 Unity BatchMode、CI 回归、产物归档留出扩展空间。

## 设计原则

1. **官方框架做底座**：以 Unity Test Framework 作为核心测试执行与断言机制。
2. **AI 不直接乱点 UI**：优先通过稳定的测试入口、Facade 与测试场景驱动流程。
3. **端到端测试少而精**：只覆盖高价值主流程，不追求全量 UI 自动化。
4. **结果必须可读**：除 pass/fail 外，还应输出 JSON、日志、截图、快照等诊断信息。
5. **测试必须分层**：逻辑、集成、E2E、视觉回归分层组织，避免混杂。

## 总体架构

建议采用四层测试架构。

### 第一层：基础逻辑测试

使用 EditMode / PlayMode 测试核心逻辑，覆盖：

- 数据模型
- 序列化 / 反序列化
- 命令栈
- Timeline 数据变换
- 资源 / 配置解析
- 核心控制器的非视觉逻辑

### 第二层：场景集成测试

建立测试专用场景或测试引导入口，用于验证：

- `SceneTestSetup` 一类系统链路
- 场景滚动 / chunk / hazard / camera 协作
- Pattern / Timeline 运行结果
- 关键运行时系统联动

### 第三层：AI 驱动端到端测试

通过 `unity-mcp` 驱动固定工作流，例如：

- 打开指定场景或测试资产
- 调用测试入口方法
- 执行播放、预览、模拟工作流
- 收集截图、日志、状态导出
- 返回结构化结果给 AI

### 第四层：视觉快照 / 截图回归

只对少量高价值链路做截图或快照验证，例如：

- Pattern 预览
- Timeline 关键帧表现
- Camera Script 演出关键节点
- Scene 运行固定时间后的关键画面

## 推荐目录结构

```text
Assets/STGEngine/Tests/
  EditMode/
  PlayMode/
  Shared/

Assets/STGEngine/TestRuntime/
  Harness/
  Results/
  Runtime/

Assets/STGEngine/TestScenes/
  Scene_RuntimeIntegration.unity
  Scene_PatternPreview.unity
  Scene_TimelineWorkflow.unity

Assets/STGEngine/Editor/TestTools/
  EditorTestFacade.cs
  PatternWorkflowTestFacade.cs
  TimelineWorkflowTestFacade.cs
```

## AI 在体系中的职责边界

### 不推荐的 AI 使用方式

不建议让 AI：

- 自己猜 Unity 菜单路径
- 自己找按钮并点击 UI Toolkit 控件
- 自己依赖层级树名称判断状态
- 自己在不受控的编辑器状态中探索式操作

### 推荐的 AI 使用方式

AI 应主要负责：

- 选择测试用例或场景
- 调用固定测试命令
- 运行测试流程
- 收集结果产物
- 读取日志与 JSON 结果
- 总结失败原因并输出诊断建议

而测试基础设施负责：

- 打开正确场景
- 初始化对象和测试数据
- 执行操作序列
- 断言期望结果
- 导出结构化结果

## 已完成的第一阶段实施（2026-03-01）

本次实施已完成第一阶段基础设施搭建，并完成最小 smoke 验证。

### 已落地内容

#### 1. 测试程序集与目录结构
- 新建 `Assets/STGEngine/Tests/EditMode/`
- 新建 `Assets/STGEngine/Tests/PlayMode/`
- 新建 `Assets/STGEngine/Tests/Shared/`
- 新建 `Assets/STGEngine/TestRuntime/`
- 建立对应 asmdef，使 EditMode / PlayMode / TestRuntime 拥有清晰边界

#### 2. 结构化结果输出基础设施
- `TestRunRecord`
- `TestResultCapture`
- `ConsoleLogCollector`
- `TestSnapshotExporter`
- `TestArtifactPaths`

这些类型已移动到正式的 `STGEngine.TestRuntime` 程序集，避免正式代码反向依赖 Tests 程序集。

#### 3. 编辑器测试入口
- `EditorTestFacade`
- `PatternWorkflowTestFacade`
- `TimelineWorkflowTestFacade`

并修正为返回轻量请求对象，避免 `STGEngine.Editor` 依赖测试程序集。

#### 4. 首批 EditMode 测试
- `TestResultCaptureTests`
- `EditorTestFacadeTests`
- `CommandStackTests`
- `OverrideManagerTests`
- `STGCatalogTests`
- `TimelineWorkflowFacadeTests`

#### 5. 首批 PlayMode / E2E smoke 测试
- `SceneIntegrationRunnerTests`
- `PatternPreviewRunnerTests`
- `SceneWorkflowRunnerTests`

#### 6. 运行时测试入口
- `SceneIntegrationRunner`
- `PreviewTestRunner`

#### 7. 测试场景
- `Assets/STGEngine/TestScenes/Scene_RuntimeIntegration.unity`
- `Assets/STGEngine/TestScenes/Scene_PatternPreview.unity`
- `Assets/STGEngine/TestScenes/Scene_TimelineWorkflow.unity`

### 本次实施中的关键修正

实施过程中发现两个重要结构问题，并已修复：

1. **正式 Editor 代码不应依赖 Tests 程序集**
   - 已将 `PatternWorkflowTestFacade` / `TimelineWorkflowTestFacade` 改为返回独立请求对象，而非 `TestRunRecord`

2. **TestRuntime 不应依赖 Tests.Shared**
   - 已新建 `STGEngine.TestRuntime` 程序集
   - 将结果捕获相关类型迁入 `TestRuntime`
   - 修正 PlayMode / EditMode asmdef 引用关系

3. **PlayMode 测试程序集边界修正**
   - 将错误放在 PlayMode 中的 Editor facade 测试迁回 EditMode
   - 修正 `STGEngine.Tests.PlayMode.asmdef`，去掉不合理的 `STGEngine.Editor` 依赖

### 验证结果

已完成最小验证：

- Unity 编译错误已清除
- PlayMode tests 已能被 Test Runner 识别
- `TestResultCaptureTests`：通过
- `SceneIntegrationRunnerTests`：通过

这表明第一阶段的测试基础设施、程序集结构、结果输出链路与最小运行时测试链路已经打通。

## 后续建议

下一阶段可继续建设：

1. 扩展更多真实 Pattern / Timeline 资产驱动测试
2. 在 PlayMode 中加入快照导出与截图采集
3. 将测试入口通过 `unity-mcp` 暴露给 AI 调度
4. 接入 BatchMode / CI 自动运行
5. 增加更完整的失败诊断与回归报告

## 结论

当前 STGEngine 已具备一套可工作的 Unity AI 自动化测试基础设施雏形：

- 有测试程序集
- 有运行时测试入口
- 有结构化结果输出
- 有测试场景
- 有 EditMode / PlayMode smoke tests
- 有经过实际编译与最小测试验证的执行路径

下一阶段的重点将不再是“从零搭框架”，而是将这套基础设施扩展为真正可被 AI 和 CI 高频调用的自动回归体系。
