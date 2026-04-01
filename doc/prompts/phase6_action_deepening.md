# Phase 6 提示词：功能块深化 + 游戏系统对接

## 角色

你是 STGEngine 项目的架构师兼实施者。Phase 5 已完成 ActionEvent 功能块系统的核心框架（数据模型、序列化、编辑器集成、预览、音频）。Phase 6 的目标是深化功能块的实际效果，并对接尚未实现的游戏系统。

---

## Phase 5 成果回顾

### 已完成
- 11 种 ActionType 的数据模型 + YAML 序列化
- 编辑器：ActionBlock 渲染、右键菜单、属性面板（反射式 UI）、Delete/Copy/Paste
- 预览：ShowTitle（5 种动画）、ScreenEffect Shake、BulletClear Gizmos
- 阻塞机制：时间轴冻结 + 绿色扫描线 + playhead 停在左边缘
- 音频：IAudioBackend + UnityAudioBackend + BGM/SE 触发 + clip 时长自动检测 + Loop snap

### 尚未实现的功能块效果

| ActionType | 当前状态 | 需要什么 |
|-----------|---------|---------|
| ScreenEffect FlashWhite/FlashRed | 只有 Shake | UI overlay 全屏半透明色块 + 淡出 |
| BackgroundSwitch | 仅数据 | 背景渲染系统（滚动层 + 切换过渡） |
| BulletClear | 仅 Gizmos 线框 | 实际消弹（遍历 SimulationEvaluator 标记 inactive） |
| ItemDrop | 仅数据 | 道具系统（生成 + 物理 + 拾取） |
| AutoCollect | 仅数据 | 道具系统 |
| ScoreTally | 仅阻塞 | 结算 overlay UI（分数/bonus 显示） |
| BranchJump | 仅数据 | Segment 切换逻辑 |

---

## Phase 6 建议任务（按优先级）

### P1：不需要新系统，可立即实现
1. **ScreenEffect FlashWhite/FlashRed** — 在 ActionEventPreviewController 中加全屏 UI overlay，Duration 内从指定颜色淡出到透明
2. **ScoreTally 结算 overlay** — 在 3D 视口叠加结算画面（"Chapter Clear!" / "Spell Card Bonus" + 分数占位），纯 UI
3. **BulletClear 实际消弹** — 在 TimelinePlaybackController 中，BulletClear 事件触发时遍历 active previewers 的 SimulationEvaluator，标记范围内 bullet 为 inactive
4. **BranchJump 可视化** — 在 timeline 上画从 BranchJump 块到目标 segment 的连线/箭头

### P2：需要新的轻量系统
5. **背景系统** — 滚动背景层（Quad + 材质 UV 偏移），BackgroundSwitch 切换材质 + 过渡效果
6. **道具系统基础** — ItemDrop 生成道具 prefab，AutoCollect 触发全屏回收动画

### P3：需要较大的游戏逻辑
7. **BranchJump 运行时** — Segment 切换逻辑（条件判断 + 跳转 + 状态重置）
8. **音频后端升级** — 如果延迟不满足需求，切换到 FMOD 或 WASAPI

---

## 关键文件参考

| 文件 | 说明 |
|------|------|
| `doc/prompts/phase5_action_block.md` | Phase 5 完整设计文档（含 Gotcha 列表） |
| `Runtime/Preview/ActionEventPreviewController.cs` | 预览效果统一控制器 |
| `Runtime/Preview/FreeCameraController.cs` | 相机控制（ShakeOffset） |
| `Runtime/Preview/TimelinePlaybackController.cs` | 时间轴播放（阻塞/Seek/BlockingProgress） |
| `Runtime/Audio/IAudioBackend.cs` | 音频接口 |
| `Runtime/Audio/UnityAudioBackend.cs` | Unity 音频实现 |
| `Runtime/Bullet/SimulationEvaluator.cs` | 弹幕模拟器（BulletClear 需要操作） |
| `Editor/UI/Timeline/Layers/ActionBlock.cs` | ActionBlock 渲染 |
| `Editor/UI/Timeline/TrackAreaView.cs` | 时间轴渲染（绿色扫描线、CanResizeDuration） |
| `Editor/UI/Timeline/TimelineEditorView.cs` | 编辑器主控（属性面板、事件创建） |
| `Editor/Scene/PatternSandboxSetup.cs` | 场景引导（音频/预览注入） |

---

## Memorix 查询入口

- `memorix_search: "Phase 5 ActionEvent"` → 完整实施总结
- `memorix_search: "blocking mechanism"` → 阻塞机制设计
- `memorix_search: "audio system"` → 音频架构
- `memorix_search: "FreeCameraController ShakeOffset"` → 相机震动 gotcha
- `memorix_search: "tempSegment ActionEvent"` → 预览 segment 复制 gotcha
- `memorix_session_context` → 上一次 session 摘要

---

## 约束（继承自 Phase 5）

- 三层程序集边界：Core 不依赖 MonoBehaviour
- Command Pattern：所有编辑操作可 Undo/Redo
- 确定性种子：新增随机行为必须通过 DeterministicRng
- UI 主题：新 UI 组件调用 RegisterThemeOverride
- 音频不影响确定性：音频触发时机一致但不影响游戏逻辑
