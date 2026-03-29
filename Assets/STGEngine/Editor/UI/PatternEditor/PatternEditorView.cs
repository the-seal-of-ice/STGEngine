using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Emitters;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Serialization;
using STGEngine.Editor.Commands;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Editor.UI.Timeline.Layers;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI
{
    /// <summary>
    /// Main pattern editor panel built with UI Toolkit.
    /// Provides emitter selection, parameter editing, modifier list management,
    /// playback controls, and YAML save/load. All edits go through CommandStack
    /// for full Undo/Redo support via generic Commands only.
    /// </summary>
    public class PatternEditorView : IDisposable
    {
        private readonly PatternPreviewer _previewer;
        private readonly CommandStack _commandStack = new();

        private BulletPattern _pattern;
        private DataBinder _patternBinder;
        private DataBinder _emitterBinder;
        private readonly List<DataBinder> _modifierBinders = new();

        /// <summary>Callback invoked when MeshType changes. Scene setup hooks this to update bullet mesh.</summary>
        public Action<MeshType> OnMeshTypeChanged;

        // Root
        private readonly VisualElement _outerContainer; // 最外层容器（定位 + 尺寸 + 背景色）
        private readonly ScrollView _scrollView;        // 滚动视图，内容超出时可垂直滚动
        private readonly VisualElement _root;            // 实际内容容器（所有 UI 组件挂在这里）

        // Emitter
        private DropdownField _emitterTypeDropdown;
        private VisualElement _emitterParamsContainer;

        // Modifiers
        private VisualElement _modifierListContainer;
        private DropdownField _addModifierDropdown;

        // Pattern fields
        private FloatField _bulletScaleField;
        private ColorField _bulletColorField;
        private FloatField _durationField;
        private IntegerField _seedField;
        private DropdownField _meshTypeDropdown;
        private VisualElement _collisionContainer;

        // Playback
        private Slider _timeSlider;
        private Label _timeLabel;
        private Button _playPauseBtn;
        private Slider _speedSlider;
        private FloatField _speedField;

        /// <summary>
        /// When set, this editor is in override mode (editing a pattern inside a SpellCard context).
        /// Save/Load buttons are hidden; changes are auto-saved by the parent TimelineEditorView.
        /// </summary>
        public string OverrideContextId { get; set; }

        // Trajectory thumbnails (live-updating)
        private const float ThumbnailSize = 100f;
        private VisualElement _emitterThumbnail;
        private VisualElement _allModsThumbnail;
        private readonly List<VisualElement> _perModifierThumbnails = new();

        // Factories
        private static readonly Dictionary<string, Func<IEmitter>> EmitterFactories = new()
        {
            { "point", () => new PointEmitter() },
            { "ring", () => new RingEmitter() },
            { "sphere", () => new SphereEmitter() },
            { "line", () => new LineEmitter() },
            { "cone", () => new ConeEmitter() },
        };

        private static readonly Dictionary<string, Func<IModifier>> ModifierFactories = new()
        {
            { "spawn_offset", () => new SpawnOffsetModifier() },
            { "speed_curve", () => new SpeedCurveModifier() },
            { "wave", () => new WaveModifier() },
            { "wave_independent", () => new IndependentWaveModifier() },
            { "homing", () => new HomingModifier() },
            { "bounce", () => new BounceModifier() },
            { "split", () => new SplitModifier() },
        };

        public VisualElement Root => _outerContainer;
        public CommandStack Commands => _commandStack;

        public PatternEditorView(PatternPreviewer previewer, string overrideContextId = null)
        {
            _previewer = previewer;
            OverrideContextId = overrideContextId;

            // ── 最外层容器（定位、尺寸、背景色） ──
            _outerContainer = new VisualElement();
            _outerContainer.style.width = new Length(18, LengthUnit.Percent);  // 面板宽度占屏幕 18%
            _outerContainer.style.minWidth = 280;   // 面板最小宽度 280px
            _outerContainer.style.maxWidth = 400;   // 面板最大宽度 400px
            _outerContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f); // 深灰半透明背景

            // ── 滚动视图（内容超出面板高度时可垂直滚动） ──
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;  // 填满外层容器
            _outerContainer.Add(_scrollView);

            // ── 实际内容容器 ──
            _root = new VisualElement();
            _root.style.paddingTop = 8;    // 面板内边距
            _root.style.paddingBottom = 8;
            _root.style.paddingLeft = 10;
            _root.style.paddingRight = 10;
            _scrollView.Add(_root);

            BuildUI();
            ApplyLightTextTheme(_root);
            SetPattern(CreateDefaultPattern());

            _commandStack.OnStateChanged += OnCommandStateChanged;
            _previewer.Playback.OnTimeChanged += OnPlaybackTimeChanged;
            _previewer.Playback.OnPlayStateChanged += OnPlayStateChanged;
        }

        public void Dispose()
        {
            _commandStack.OnStateChanged -= OnCommandStateChanged;
            _previewer.Playback.OnTimeChanged -= OnPlaybackTimeChanged;
            _previewer.Playback.OnPlayStateChanged -= OnPlayStateChanged;
            _patternBinder?.Dispose();
            _emitterBinder?.Dispose();
            DisposeModifierBinders();
        }

        // ─── UI Construction ───

        private void BuildUI()
        {
            // ── 面板标题 "Pattern Editor" ──
            var title = new Label("Pattern Editor");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;  // 标题与下方 Undo/Redo 栏的间距
            title.style.color = Color.white;
            _root.Add(title);

            BuildUndoRedoBar();      // Undo / Redo 按钮栏
            BuildPatternSection();   // "Pattern" 区段：Scale, Color, Duration
            BuildEmitterSection();   // "Emitter" 区段：Type 下拉 + 动态参数
            BuildModifierSection();  // "Modifiers" 区段：modifier 列表 + 添加栏
            BuildPlaybackSection();  // "Playback" 区段：Play/Step/Reset, Time/Speed slider, Loop
            BuildFileSection();      // "File" 区段：Save/Load YAML 按钮
        }

        // ── Undo / Redo 按钮栏（水平排列） ──
        private void BuildUndoRedoBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.marginBottom = 6;  // 按钮栏与下方 Pattern 区段标题的间距

            var undoBtn = new Button(() => _commandStack.Undo()) { text = "Undo" };  // [Undo] 按钮
            undoBtn.style.flexGrow = 1;
            bar.Add(undoBtn);

            var redoBtn = new Button(() => _commandStack.Redo()) { text = "Redo" };  // [Redo] 按钮
            redoBtn.style.flexGrow = 1;
            bar.Add(redoBtn);

            _root.Add(bar);
        }

        // ── "Pattern" 区段 ──
        private void BuildPatternSection()
        {
            _root.Add(MakeHeader("Pattern"));

            _bulletScaleField = new FloatField("Scale");
            _root.Add(_bulletScaleField);

            _bulletColorField = new ColorField("Color");
            _root.Add(_bulletColorField);

            _durationField = new FloatField("Duration");
            _root.Add(_durationField);

            // Seed field (same layout as Scale/Duration)
            _seedField = new IntegerField("Seed");
            _seedField.isDelayed = true;
            _seedField.AddToClassList("seed-field");
            _root.Add(_seedField);

            // Randomize seed button
            var randomizeBtn = new Button(() =>
            {
                if (_pattern == null) return;
                int newSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                var cmd = new PropertyChangeCommand<int>(
                    "Randomize Seed",
                    () => _pattern.Seed,
                    v => _pattern.Seed = v,
                    newSeed);
                _commandStack.Execute(cmd);
                _seedField.SetValueWithoutNotify(newSeed);
            }) { text = "Randomize Seed" };
            randomizeBtn.style.height = 22;
            randomizeBtn.style.marginTop = 2;
            randomizeBtn.style.marginBottom = 4;
            _root.Add(randomizeBtn);

            // MeshType dropdown
            var meshTypes = new List<string> { "Sphere", "Diamond", "Arrow", "Rice" };
            _meshTypeDropdown = new DropdownField("MeshType", meshTypes, 0);
            _meshTypeDropdown.RegisterValueChangedCallback(evt =>
            {
                if (_pattern == null) return;
                if (Enum.TryParse<MeshType>(evt.newValue, out var mt))
                {
                    var cmd = new PropertyChangeCommand<MeshType>(
                        $"Change MeshType to {evt.newValue}",
                        () => _pattern.MeshType,
                        v =>
                        {
                            _pattern.MeshType = v;
                            OnMeshTypeChanged?.Invoke(v);
                        },
                        mt);
                    _commandStack.Execute(cmd);
                }
            });
            _root.Add(_meshTypeDropdown);

            // Collision shape section
            _root.Add(MakeHeader("Collision"));
            _collisionContainer = new VisualElement();
            _root.Add(_collisionContainer);
        }

        // ── "Emitter" 区段 ──
        private void BuildEmitterSection()
        {
            _root.Add(MakeHeader("Emitter"));  // 区段标题 "Emitter"

            // 发射器类型下拉框，label="Type"，选项来自 EmitterFactories 的 key（point / ring）
            _emitterTypeDropdown = new DropdownField("Type",
                new List<string>(EmitterFactories.Keys), 0);
            _emitterTypeDropdown.RegisterValueChangedCallback(OnEmitterTypeChanged);
            _root.Add(_emitterTypeDropdown);

            // 发射器参数容器 —— 内容由 RebuildEmitterParams() 动态填充
            // 包含 Count(IntegerField) + 类型特有参数（如 Speed, Radius 等 FloatField）
            // ★ 如果 Emitter 区段最后一个字段和 Modifiers 标题重叠，可在此加 marginBottom
            _emitterParamsContainer = new VisualElement();
            _root.Add(_emitterParamsContainer);
        }

        // ── "Modifiers" 区段 ──
        private void BuildModifierSection()
        {
            _root.Add(MakeHeader("Modifiers"));  // 区段标题 "Modifiers"

            // modifier 列表容器 —— 内容由 RebuildModifierList() 动态填充
            // 每个 modifier 是一个 container（带底部分隔线），内含标题行 + 参数字段
            _modifierListContainer = new VisualElement();
            _root.Add(_modifierListContainer);

            // "添加 modifier" 栏（水平排列：下拉框 + "+" 按钮）
            var addBar = new VisualElement();
            addBar.style.flexDirection = FlexDirection.Row;
            addBar.style.marginTop = 4;  // 添加栏与上方 modifier 列表的间距

            // modifier 类型下拉框（无 label），选项来自 ModifierFactories 的 key
            _addModifierDropdown = new DropdownField(
                new List<string>(ModifierFactories.Keys), 0);
            _addModifierDropdown.style.flexGrow = 1;
            addBar.Add(_addModifierDropdown);

            var addBtn = new Button(OnAddModifier) { text = "+" };  // [+] 添加按钮
            addBtn.style.width = 30;
            addBar.Add(addBtn);

            _root.Add(addBar);
        }

        // ── "Playback" 区段 ──
        private void BuildPlaybackSection()
        {
            _root.Add(MakeHeader("Playback"));  // 区段标题 "Playback"

            // 播放控制栏（水平排列：Play/Pause + Step + Reset）
            var controlBar = new VisualElement();
            controlBar.style.flexDirection = FlexDirection.Row;
            controlBar.style.marginBottom = 4;  // 控制栏与下方 Time slider 的间距

            _playPauseBtn = new Button(() => _previewer.Playback.TogglePlay())
                { text = "Play" };  // [Play/Pause] 按钮，文字随播放状态切换
            _playPauseBtn.style.flexGrow = 1;
            controlBar.Add(_playPauseBtn);

            var stepBtn = new Button(() =>  // [Step] 单步按钮
            {
                _previewer.Playback.StepFrame();
                _previewer.ForceRefresh();
            }) { text = "Step" };
            stepBtn.style.width = 50;  // 固定宽度 50px
            controlBar.Add(stepBtn);

            var resetBtn = new Button(() =>  // [Reset] 重置按钮
            {
                _previewer.Playback.Seek(0f);
                _previewer.ForceRefresh();
                ResetTimeSliderRange();
            }) { text = "Reset" };
            resetBtn.style.width = 50;  // 固定宽度 50px
            controlBar.Add(resetBtn);

            _root.Add(controlBar);

            // 时间滑块 Slider，label="Time"，范围 0~5
            // ★ 如果 "Time" label 被截断，可在 ApplyLightTextTheme 中给 Slider 单独设置更大的 label 宽度
            _timeSlider = new Slider("Time", 0f, 5f) { value = 0f };
            _timeSlider.RegisterValueChangedCallback(evt =>
            {
                _previewer.Playback.Seek(evt.newValue);
                _previewer.ForceRefresh();
            });
            _root.Add(_timeSlider);

            // 时间显示标签 "0.00 / 5.00"（右对齐，灰色）
            _timeLabel = new Label("0.00 / 5.00");
            _timeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _timeLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            _root.Add(_timeLabel);

            // 速度滑块 Slider，label="Speed"，范围 0.1~3
            _speedSlider = new Slider("Speed", 0.1f, 3f) { value = 1f };
            _speedSlider.RegisterValueChangedCallback(evt =>
            {
                _previewer.Playback.PlaybackSpeed = evt.newValue;
                _speedField.SetValueWithoutNotify(Mathf.Round(evt.newValue * 100f) / 100f);
            });
            _root.Add(_speedSlider);

            // Speed 辅助行（输入框 + 恢复默认按钮）
            var speedSubRow = new VisualElement();
            speedSubRow.style.flexDirection = FlexDirection.Row;
            speedSubRow.style.alignItems = Align.Center;
            speedSubRow.style.marginTop = 2;

            // Speed 数值输入框
            _speedField = new FloatField() { value = 1f };
            _speedField.isDelayed = true;
            _speedField.style.flexGrow = 1;
            _speedField.style.flexShrink = 1;
            _speedField.AddToClassList("compact-field");
            _speedField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, 0.1f, 3f);
                _previewer.Playback.PlaybackSpeed = clamped;
                _speedSlider.SetValueWithoutNotify(clamped);
                if (Mathf.Abs(clamped - evt.newValue) > 0.001f)
                    _speedField.SetValueWithoutNotify(clamped);
            });
            speedSubRow.Add(_speedField);

            // [1x] 恢复默认速度按钮
            var speedResetBtn = new Button(() =>
            {
                _speedSlider.value = 1f;
                _speedField.SetValueWithoutNotify(1f);
                _previewer.Playback.PlaybackSpeed = 1f;
            }) { text = "1x" };
            speedResetBtn.style.width = 30;
            speedResetBtn.style.marginLeft = 4;
            speedSubRow.Add(speedResetBtn);

            _root.Add(speedSubRow);

            // 循环开关 Toggle，label="Loop"
            var loopToggle = new Toggle("Loop") { value = true };
            loopToggle.RegisterValueChangedCallback(evt =>
                _previewer.Playback.Loop = evt.newValue);
            _root.Add(loopToggle);
        }

        // ── "File" 区段 ──
        private void BuildFileSection()
        {
            _root.Add(MakeHeader("File"));  // 区段标题 "File"

            if (!string.IsNullOrEmpty(OverrideContextId))
            {
                var overrideLabel = new Label("Override mode — changes auto-saved");
                overrideLabel.style.color = new Color(1f, 0.7f, 0.3f);
                overrideLabel.style.fontSize = 11;
                overrideLabel.style.marginLeft = 4;
                overrideLabel.style.marginBottom = 4;
                _root.Add(overrideLabel);
                return;
            }

            // 文件操作栏（水平排列：Save + Load）
            var fileBar = new VisualElement();
            fileBar.style.flexDirection = FlexDirection.Row;

            var saveBtn = new Button(OnSave) { text = "Save YAML" };  // [Save YAML] 按钮
            saveBtn.style.flexGrow = 1;
            fileBar.Add(saveBtn);

            var loadBtn = new Button(OnLoad) { text = "Load YAML" };  // [Load YAML] 按钮
            loadBtn.style.flexGrow = 1;
            fileBar.Add(loadBtn);

            _root.Add(fileBar);
        }

        // ─── Pattern Binding ───

        public void SetPattern(BulletPattern pattern)
        {
            _pattern = pattern;
            _commandStack.Clear();

            BindPatternFields();

            var emitterTag = _pattern.Emitter?.TypeName ?? "point";
            _emitterTypeDropdown.SetValueWithoutNotify(emitterTag);
            _meshTypeDropdown.SetValueWithoutNotify(_pattern.MeshType.ToString());
            RebuildEmitterParams();
            RebuildModifierList();
            RebuildCollisionEditor();

            // Force theme refresh after rebuilding all dynamic UI
            ForceApplyTheme(_root);

            // Notify mesh type change so scene setup can update bullet visuals
            OnMeshTypeChanged?.Invoke(_pattern.MeshType);

            _previewer.SetDefaultPattern(_pattern);
            UpdateTimeSliderRange();
            RefreshThumbnails();
        }

        private void BindPatternFields()
        {
            // Dispose previous binder to remove old callbacks
            _patternBinder?.Dispose();
            _patternBinder = new DataBinder();

            _patternBinder.Bind(_bulletScaleField, _pattern,
                nameof(BulletPattern.BulletScale), _commandStack);

            _patternBinder.Bind(_durationField, _pattern,
                nameof(BulletPattern.Duration), _commandStack);

            _patternBinder.Bind(_seedField, _pattern,
                nameof(BulletPattern.Seed), _commandStack);

            // Color uses custom field, bind manually
            _bulletColorField.SetColor(_pattern.BulletColor);
            _bulletColorField.OnColorChanged = color =>
            {
                var cmd = new PropertyChangeCommand<Color>(
                    "Change Color",
                    () => _pattern.BulletColor,
                    v => _pattern.BulletColor = v,
                    color);
                _commandStack.Execute(cmd);
            };
        }

        // ─── Emitter ───

        private void OnEmitterTypeChanged(ChangeEvent<string> evt)
        {
            if (!EmitterFactories.TryGetValue(evt.newValue, out var factory)) return;
            if (_pattern.Emitter?.TypeName == evt.newValue) return;

            var newEmitter = factory();
            var cmd = new PropertyChangeCommand<IEmitter>(
                $"Change Emitter to {evt.newValue}",
                () => _pattern.Emitter,
                v =>
                {
                    _pattern.Emitter = v;
                    RebuildEmitterParams();
                    _previewer.ForceRefresh();
                },
                newEmitter);
            _commandStack.Execute(cmd);
        }

        // 动态重建发射器参数字段（切换发射器类型时调用）
        private void RebuildEmitterParams()
        {
            _emitterBinder?.Dispose();
            _emitterBinder = null;
            _emitterParamsContainer.Clear();

            if (_pattern.Emitter == null) return;

            _emitterBinder = new DataBinder();
            var emitter = _pattern.Emitter;

            // 弹幕数量 IntegerField，label="Count"（所有发射器共有）
            var countField = new IntegerField("Count");
            _emitterBinder.Bind(countField, emitter, nameof(IEmitter.Count), _commandStack);
            _emitterParamsContainer.Add(countField);

            // 按发射器类型添加特有参数
            switch (emitter)
            {
                case PointEmitter point:
                    var speedField = new FloatField("Speed");  // PointEmitter 的速度 FloatField
                    _emitterBinder.Bind(speedField, point,
                        nameof(PointEmitter.Speed), _commandStack);
                    _emitterParamsContainer.Add(speedField);
                    break;

                case RingEmitter ring:
                    var radiusField = new FloatField("Radius");
                    _emitterBinder.Bind(radiusField, ring,
                        nameof(RingEmitter.Radius), _commandStack);
                    _emitterParamsContainer.Add(radiusField);

                    var ringSpeedField = new FloatField("Speed");
                    _emitterBinder.Bind(ringSpeedField, ring,
                        nameof(RingEmitter.Speed), _commandStack);
                    _emitterParamsContainer.Add(ringSpeedField);
                    break;

                case SphereEmitter sphere:
                    var sphRadiusField = new FloatField("Radius");
                    _emitterBinder.Bind(sphRadiusField, sphere,
                        nameof(SphereEmitter.Radius), _commandStack);
                    _emitterParamsContainer.Add(sphRadiusField);

                    var sphSpeedField = new FloatField("Speed");
                    _emitterBinder.Bind(sphSpeedField, sphere,
                        nameof(SphereEmitter.Speed), _commandStack);
                    _emitterParamsContainer.Add(sphSpeedField);
                    break;

                case LineEmitter line:
                    var lineSpeedField = new FloatField("Speed");
                    _emitterBinder.Bind(lineSpeedField, line,
                        nameof(LineEmitter.Speed), _commandStack);
                    _emitterParamsContainer.Add(lineSpeedField);

                    // StartPoint / EndPoint as 3 float fields each
                    _emitterParamsContainer.Add(MakeVector3Editor("Start", line,
                        nameof(LineEmitter.StartPoint), _emitterBinder));
                    _emitterParamsContainer.Add(MakeVector3Editor("End", line,
                        nameof(LineEmitter.EndPoint), _emitterBinder));
                    break;

                case ConeEmitter cone:
                    var coneSpeedField = new FloatField("Speed");
                    _emitterBinder.Bind(coneSpeedField, cone,
                        nameof(ConeEmitter.Speed), _commandStack);
                    _emitterParamsContainer.Add(coneSpeedField);

                    var angleField = new FloatField("Angle");
                    _emitterBinder.Bind(angleField, cone,
                        nameof(ConeEmitter.Angle), _commandStack);
                    _emitterParamsContainer.Add(angleField);

                    var coneRadiusField = new FloatField("Radius");
                    _emitterBinder.Bind(coneRadiusField, cone,
                        nameof(ConeEmitter.Radius), _commandStack);
                    _emitterParamsContainer.Add(coneRadiusField);
                    break;
            }

            ApplyLightTextTheme(_emitterParamsContainer);

            // Emitter trajectory thumbnail (below emitter params)
            _emitterThumbnail = CreateTrajectoryThumbnail(ThumbnailSize);
            _emitterParamsContainer.Add(_emitterThumbnail);
        }

        // ─── Modifiers ───

        private void OnAddModifier()
        {
            var typeName = _addModifierDropdown.value;
            if (!ModifierFactories.TryGetValue(typeName, out var factory)) return;

            var modifier = factory();
            var cmd = ListCommand<IModifier>.Add(
                _pattern.Modifiers, modifier, -1, $"Add {typeName} modifier");
            _commandStack.Execute(cmd);
        }

        /// <summary>
        /// Notify that a modifier property was changed directly (without CommandStack).
        /// Triggers preview refresh via OnStateChanged.
        /// </summary>
        private void OnModifierChanged()
        {
            _commandStack.NotifyChanged();
        }

        // 动态重建 modifier 列表（添加/删除/Undo 时调用）
        private void RebuildModifierList()
        {
            DisposeModifierBinders();
            _modifierListContainer.Clear();
            _perModifierThumbnails.Clear();
            _allModsThumbnail = null;

            for (int i = 0; i < _pattern.Modifiers.Count; i++)
            {
                int idx = i;
                var mod = _pattern.Modifiers[i];
                var binder = new DataBinder();
                _modifierBinders.Add(binder);

                // ── 单个 modifier 的外层容器 ──
                var container = new VisualElement();
                container.style.borderBottomWidth = 1;  // 底部分隔线
                container.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
                container.style.paddingBottom = 6;   // ★ modifier 块内底部内边距，影响最后一个字段到分隔线的距离
                container.style.marginBottom = 6;    // ★ modifier 块之间的外间距

                // modifier 标题行（水平排列：类型名 + 标记 + [×] 删除按钮）
                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.marginBottom = 4;  // 标题行与下方参数字段的间距

                var labelText = mod.TypeName;
                var labelColor = new Color(0.9f, 0.7f, 0.3f); // 默认橙色

                // ISpawnModifier 特殊标记
                if (mod is ISpawnModifier)
                {
                    labelText = "\u2726 " + labelText; // ✦ 前缀
                    labelColor = new Color(0.5f, 0.9f, 0.7f); // 绿色调，区分于飞行修饰器
                }

                var label = new Label(labelText);
                label.style.flexGrow = 1;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = labelColor;
                if (mod is ISpawnModifier)
                    label.tooltip = "Spawn modifier \u2014 modifies bullet data at emission time";
                header.Add(label);

                var removeBtn = new Button(() =>  // [×] 删除按钮
                {
                    int currentIdx = _pattern.Modifiers.IndexOf(mod);
                    if (currentIdx < 0) return;
                    var removeCmd = ListCommand<IModifier>.Remove(
                        _pattern.Modifiers, currentIdx, $"Remove {mod.TypeName}");
                    _commandStack.Execute(removeCmd);
                }) { text = "×" };
                removeBtn.style.width = 24;
                header.Add(removeBtn);

                container.Add(header);

                // 按 modifier 类型添加参数字段
                switch (mod)
                {
                    case SpeedCurveModifier scm:
                        BuildSpeedCurveEditor(container, scm, binder);  // SpeedCurve 关键帧编辑器
                        break;

                    case WaveModifier wm:
                        var ampField = new FloatField("Amplitude");   // 振幅 FloatField
                        binder.Bind(ampField, wm,
                            nameof(WaveModifier.Amplitude), _commandStack);
                        container.Add(ampField);

                        var freqField = new FloatField("Frequency");  // 频率 FloatField
                        binder.Bind(freqField, wm,
                            nameof(WaveModifier.Frequency), _commandStack);
                        container.Add(freqField);

                        var axisDropdown = new DropdownField("Axis",  // 轴向下拉框
                            new List<string> { "perpendicular", "vertical" },
                            wm.Axis == "vertical" ? 1 : 0);
                        binder.Bind(axisDropdown, wm,
                            nameof(WaveModifier.Axis), _commandStack);
                        container.Add(axisDropdown);
                        break;

                    case IndependentWaveModifier iwm:
                        var iwAmpField = new FloatField("Amplitude");
                        binder.Bind(iwAmpField, iwm,
                            nameof(IndependentWaveModifier.Amplitude), _commandStack);
                        container.Add(iwAmpField);

                        var wlField = new FloatField("Wavelength");
                        binder.Bind(wlField, iwm,
                            nameof(IndependentWaveModifier.Wavelength), _commandStack);
                        container.Add(wlField);

                        var iwAxisDropdown = new DropdownField("Axis",
                            new List<string> { "perpendicular", "vertical" },
                            iwm.Axis == "vertical" ? 1 : 0);
                        binder.Bind(iwAxisDropdown, iwm,
                            nameof(IndependentWaveModifier.Axis), _commandStack);
                        container.Add(iwAxisDropdown);
                        break;

                    case HomingModifier hm:
                        container.Add(MakeVector3Editor("Target", hm,
                            nameof(HomingModifier.TargetPosition), binder));

                        var turnSpeedField = new FloatField("TurnSpeed");
                        binder.Bind(turnSpeedField, hm,
                            nameof(HomingModifier.TurnSpeed), _commandStack);
                        container.Add(turnSpeedField);

                        var delayField = new FloatField("Delay");
                        binder.Bind(delayField, hm,
                            nameof(HomingModifier.Delay), _commandStack);
                        container.Add(delayField);

                        var apModes = new List<string> { "Random", "Fixed", "None" };
                        var apDropdown = new DropdownField("AntiParallel", apModes,
                            apModes.IndexOf(hm.AntiParallel.ToString()));
                        apDropdown.RegisterValueChangedCallback(apEvt =>
                        {
                            if (Enum.TryParse<AntiParallelMode>(apEvt.newValue, out var mode))
                            {
                                var apCmd = new PropertyChangeCommand<AntiParallelMode>(
                                    $"Change AntiParallel to {apEvt.newValue}",
                                    () => hm.AntiParallel,
                                    v => hm.AntiParallel = v,
                                    mode);
                                _commandStack.Execute(apCmd);
                            }
                        });
                        container.Add(apDropdown);
                        break;

                    case BounceModifier bm:
                        container.Add(MakeVector3Editor("BoundaryHalfExtents", bm,
                            nameof(BounceModifier.BoundaryHalfExtents), binder));

                        var maxBouncesField = new IntegerField("MaxBounces");
                        binder.Bind(maxBouncesField, bm,
                            nameof(BounceModifier.MaxBounces), _commandStack);
                        container.Add(maxBouncesField);
                        break;

                    case SplitModifier sm:
                        var splitTimeField = new FloatField("SplitTime");
                        binder.Bind(splitTimeField, sm,
                            nameof(SplitModifier.SplitTime), _commandStack);
                        container.Add(splitTimeField);

                        var splitCountField = new IntegerField("SplitCount");
                        binder.Bind(splitCountField, sm,
                            nameof(SplitModifier.SplitCount), _commandStack);
                        container.Add(splitCountField);

                        var spreadAngleField = new FloatField("SpreadAngle");
                        binder.Bind(spreadAngleField, sm,
                            nameof(SplitModifier.SpreadAngle), _commandStack);
                        container.Add(spreadAngleField);

                        var destroyParentToggle = new Toggle("DestroyParent") { value = sm.DestroyParent };
                        binder.Bind(destroyParentToggle, sm,
                            nameof(SplitModifier.DestroyParent), _commandStack);
                        container.Add(destroyParentToggle);
                        break;

                    case SpawnOffsetModifier so:
                        // Distribution mode
                        var modeNames = new List<string> { "Uniform", "Normal" };
                        var modeDropdown = new DropdownField("Distribution", modeNames,
                            so.Mode == SpawnOffsetModifier.DistributionMode.Normal ? 1 : 0);
                        modeDropdown.RegisterValueChangedCallback(e =>
                        {
                            so.Mode = e.newValue == "Normal"
                                ? SpawnOffsetModifier.DistributionMode.Normal
                                : SpawnOffsetModifier.DistributionMode.Uniform;
                            OnModifierChanged();
                        });
                        container.Add(modeDropdown);

                        // Position range (Vector3)
                        var posLabel = new Label("Position Range (XYZ)");
                        posLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                        posLabel.style.marginTop = 4;
                        container.Add(posLabel);
                        var posXField = new FloatField("X") { value = so.PositionRange.x };
                        posXField.isDelayed = true;
                        posXField.RegisterValueChangedCallback(e =>
                        {
                            so.PositionRange = new Vector3(Mathf.Max(0f, e.newValue), so.PositionRange.y, so.PositionRange.z);
                            OnModifierChanged();
                        });
                        container.Add(posXField);
                        var posYField = new FloatField("Y") { value = so.PositionRange.y };
                        posYField.isDelayed = true;
                        posYField.RegisterValueChangedCallback(e =>
                        {
                            so.PositionRange = new Vector3(so.PositionRange.x, Mathf.Max(0f, e.newValue), so.PositionRange.z);
                            OnModifierChanged();
                        });
                        container.Add(posYField);
                        var posZField = new FloatField("Z") { value = so.PositionRange.z };
                        posZField.isDelayed = true;
                        posZField.RegisterValueChangedCallback(e =>
                        {
                            so.PositionRange = new Vector3(so.PositionRange.x, so.PositionRange.y, Mathf.Max(0f, e.newValue));
                            OnModifierChanged();
                        });
                        container.Add(posZField);

                        // Direction jitter
                        var dirJitterField = new FloatField("Direction Jitter (\u00b0)") { value = so.DirectionJitter };
                        dirJitterField.isDelayed = true;
                        dirJitterField.RegisterValueChangedCallback(e =>
                        {
                            so.DirectionJitter = Mathf.Max(0f, e.newValue);
                            OnModifierChanged();
                        });
                        container.Add(dirJitterField);

                        // Link direction to offset
                        var linkToggle = new Toggle("Link Direction to Offset") { value = so.LinkDirectionToOffset };
                        linkToggle.RegisterValueChangedCallback(e =>
                        {
                            so.LinkDirectionToOffset = e.newValue;
                            OnModifierChanged();
                        });
                        container.Add(linkToggle);

                        // Speed jitter
                        var speedJitterField = new FloatField("Speed Jitter") { value = so.SpeedJitter };
                        speedJitterField.isDelayed = true;
                        speedJitterField.RegisterValueChangedCallback(e =>
                        {
                            so.SpeedJitter = Mathf.Max(0f, e.newValue);
                            OnModifierChanged();
                        });
                        container.Add(speedJitterField);
                        break;
                }

                _modifierListContainer.Add(container);

                // Per-modifier trajectory thumbnail
                var modThumb = CreateTrajectoryThumbnail(ThumbnailSize);
                container.Add(modThumb);
                _perModifierThumbnails.Add(modThumb);
            }

            // All-bullets-all-modifiers thumbnail (after all modifier blocks)
            if (_pattern.Modifiers.Count > 0)
            {
                var allLabel = new Label("All Bullets + All Modifiers");
                allLabel.style.fontSize = 10;
                allLabel.style.color = new Color(0.6f, 0.7f, 0.8f);
                allLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                allLabel.style.marginTop = 4;
                _modifierListContainer.Add(allLabel);

                _allModsThumbnail = CreateTrajectoryThumbnail(ThumbnailSize);
                _modifierListContainer.Add(_allModsThumbnail);
            }

            ApplyLightTextTheme(_modifierListContainer);
        }

        // ─── File ───

        private void OnSave()
        {
            var catalog = STGCatalog.Load();
            var picker = new FilePickerPopup(
                "Save Pattern",
                FilePickerMode.Save,
                catalog.Patterns,
                onSelect: entry =>
                {
                    // Overwrite existing
                    _pattern.Id = entry.Id;
                    _pattern.Name = entry.Name;
                    var path = Path.Combine(STGCatalog.BasePath, entry.File);
                    YamlSerializer.SerializeToFile(_pattern, path);
                    catalog.AddOrUpdatePattern(entry.Id, entry.Name);
                    STGCatalog.Save(catalog);
                    Debug.Log($"[PatternEditor] Saved (overwrite): {path}");
                },
                onCreateNew: name =>
                {
                    var id = STGCatalog.NameToId(name);
                    id = catalog.EnsureUniquePatternId(id);
                    _pattern.Id = id;
                    _pattern.Name = name;
                    catalog.AddOrUpdatePattern(id, name);
                    var path = catalog.GetPatternPath(id);
                    YamlSerializer.SerializeToFile(_pattern, path);
                    STGCatalog.Save(catalog);
                    Debug.Log($"[PatternEditor] Saved (new): {path}");
                },
                onDelete: entry =>
                {
                    catalog.RemovePattern(entry.Id);
                    STGCatalog.Save(catalog);
                    Debug.Log($"[PatternEditor] Deleted: {entry.Id}");
                });
            picker.Show(Root);
        }

        private void OnLoad()
        {
            var catalog = STGCatalog.Load();
            if (catalog.Patterns.Count == 0)
            {
                Debug.Log("[PatternEditor] No pattern files found.");
                return;
            }

            var picker = new FilePickerPopup(
                "Load Pattern",
                FilePickerMode.Load,
                catalog.Patterns,
                onSelect: entry =>
                {
                    var path = Path.Combine(STGCatalog.BasePath, entry.File);
                    try
                    {
                        var pattern = YamlSerializer.DeserializeFromFile(path);
                        SetPattern(pattern);
                        Debug.Log($"[PatternEditor] Loaded: {path}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[PatternEditor] Load failed: {e.Message}");
                    }
                },
                onDelete: entry =>
                {
                    catalog.RemovePattern(entry.Id);
                    STGCatalog.Save(catalog);
                    Debug.Log($"[PatternEditor] Deleted: {entry.Id}");
                });
            picker.Show(Root);
        }

        // ─── Events ───

        private void OnPlaybackTimeChanged(float t)
        {
            // Grow slider upper bound to follow playback time
            if (t > _timeSlider.highValue)
                _timeSlider.highValue = t;

            _timeSlider.SetValueWithoutNotify(t);
            _timeLabel.text = $"{t:F2}";
        }

        private void OnPlayStateChanged(bool playing)
        {
            _playPauseBtn.text = playing ? "Pause" : "Play";
        }

        private void OnCommandStateChanged()
        {
            // Refresh all binders
            _patternBinder?.RefreshUI();
            _emitterBinder?.RefreshUI();
            foreach (var b in _modifierBinders) b.RefreshUI();

            // Refresh color field (not bound via DataBinder)
            _bulletColorField.SetColor(_pattern.BulletColor);

            // Sync duration to playback
            _previewer.Playback.Duration = _pattern.Duration;
            UpdateTimeSliderRange();

            // Check emitter type change (undo swap)
            if (_pattern.Emitter != null &&
                _emitterTypeDropdown.value != _pattern.Emitter.TypeName)
            {
                _emitterTypeDropdown.SetValueWithoutNotify(_pattern.Emitter.TypeName);
                RebuildEmitterParams();
            }

            // Sync MeshType dropdown
            _meshTypeDropdown.SetValueWithoutNotify(_pattern.MeshType.ToString());

            // Rebuild modifier list (undo add/remove)
            RebuildModifierList();
            RebuildCollisionEditor();

            // Force theme refresh on entire root to catch any newly created elements
            ForceApplyTheme(_root);

            _previewer.ForceRefresh();
            RefreshThumbnails();
        }

        // ─── Trajectory Thumbnails ───

        /// <summary>
        /// Recompute and redraw all trajectory thumbnails.
        /// Called on every data change (CommandStack state change).
        /// </summary>
        private void RefreshThumbnails()
        {
            if (_pattern?.Emitter == null) return;

            float sampleDuration = Mathf.Max(10f, (_pattern.Duration > 0f ? _pattern.Duration : 5f) * 3f);
            // Shorter duration for modifier thumbnails so the effect is more visible
            float modSampleDuration = Mathf.Max(2f, _pattern.Duration > 0f ? _pattern.Duration : 3f);

            // Emitter-only thumbnail
            var emitterTrajs = TrajectoryThumbnailRenderer.ComputeEmitterOnly(_pattern, sampleDuration);
            if (_emitterThumbnail != null && emitterTrajs != null && emitterTrajs.Count > 0)
            {
                _emitterThumbnail.userData = emitterTrajs;
                _emitterThumbnail.MarkDirtyRepaint();
            }

            // Per-modifier thumbnails
            if (_pattern.Modifiers != null)
            {
                for (int i = 0; i < _pattern.Modifiers.Count && i < _perModifierThumbnails.Count; i++)
                {
                    var mod = _pattern.Modifiers[i];
                    var modTrajs = TrajectoryThumbnailRenderer.ComputeSingleBulletWithModifier(
                        _pattern, mod, modSampleDuration);
                    var thumb = _perModifierThumbnails[i];
                    if (thumb != null && modTrajs != null && modTrajs.Count > 0)
                    {
                        thumb.userData = modTrajs;
                        thumb.MarkDirtyRepaint();
                    }
                }
            }

            // All-bullets-all-modifiers thumbnail
            if (_allModsThumbnail != null && _pattern.Modifiers != null && _pattern.Modifiers.Count > 0)
            {
                var allTrajs = TrajectoryThumbnailRenderer.ComputeAllBulletsAllModifiers(
                    _pattern, modSampleDuration);
                if (allTrajs != null && allTrajs.Count > 0)
                {
                    _allModsThumbnail.userData = allTrajs;
                    _allModsThumbnail.MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// Create a trajectory thumbnail VisualElement with dark background.
        /// Uses userData to store the trajectory data; generateVisualContent reads it.
        /// </summary>
        private static VisualElement CreateTrajectoryThumbnail(float size)
        {
            var thumb = new VisualElement();
            thumb.style.width = size;
            thumb.style.height = size;
            thumb.style.marginTop = 4;
            thumb.style.marginBottom = 4;
            thumb.style.alignSelf = Align.Center;
            thumb.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 0.8f);
            thumb.style.borderTopLeftRadius = thumb.style.borderTopRightRadius =
                thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 4;
            thumb.style.borderTopWidth = thumb.style.borderBottomWidth =
                thumb.style.borderLeftWidth = thumb.style.borderRightWidth = 1;
            thumb.style.borderTopColor = thumb.style.borderBottomColor =
                thumb.style.borderLeftColor = thumb.style.borderRightColor =
                    new Color(0.3f, 0.3f, 0.4f, 0.6f);

            thumb.generateVisualContent += ctx =>
            {
                if (thumb.userData is List<TrajectoryThumbnailRenderer.TrajPoint[]> trajs &&
                    trajs.Count > 0)
                {
                    float w = thumb.resolvedStyle.width;
                    float h = thumb.resolvedStyle.height;
                    if (w > 0f && h > 0f)
                        TrajectoryThumbnailRenderer.Draw(ctx.painter2D, w, h, trajs);
                }
            };

            return thumb;
        }

        // ─── Helpers ───

        private void UpdateTimeSliderRange()
        {
            _timeSlider.highValue = Mathf.Max(_pattern?.Duration ?? 5f, 0.1f);
        }

        /// <summary>Reset slider range back to a small initial value (called on Reset).</summary>
        private void ResetTimeSliderRange()
        {
            _timeSlider.highValue = Mathf.Max(_pattern?.Duration ?? 5f, 0.1f);
            _timeSlider.SetValueWithoutNotify(0f);
            _timeLabel.text = "0.00";
        }

        private void DisposeModifierBinders()
        {
            foreach (var b in _modifierBinders) b.Dispose();
            _modifierBinders.Clear();
        }

        /// <summary>
        /// Rebuild collision shape editor UI.
        /// </summary>
        private void RebuildCollisionEditor()
        {
            _collisionContainer.Clear();

            var collision = _pattern.Collision;
            bool hasCollision = collision != null;

            var enableToggle = new Toggle("Enable") { value = hasCollision };
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue && _pattern.Collision == null)
                    _pattern.Collision = new CollisionShape();
                else if (!evt.newValue)
                    _pattern.Collision = null;
                RebuildCollisionEditor();
                _previewer.ForceRefresh();
            });
            _collisionContainer.Add(enableToggle);

            if (!hasCollision) return;

            var shapeTypes = new List<string> { "Sphere", "Capsule", "Box" };
            var shapeDropdown = new DropdownField("Shape", shapeTypes,
                (int)collision.ShapeType);
            shapeDropdown.RegisterValueChangedCallback(evt =>
            {
                if (Enum.TryParse<CollisionShapeType>(evt.newValue, out var st))
                {
                    collision.ShapeType = st;
                    RebuildCollisionEditor();
                    _previewer.ForceRefresh();
                }
            });
            _collisionContainer.Add(shapeDropdown);

            switch (collision.ShapeType)
            {
                case CollisionShapeType.Sphere:
                    var radiusField = new FloatField("Radius") { value = collision.Radius };
                    radiusField.isDelayed = true;
                    radiusField.RegisterValueChangedCallback(evt =>
                    {
                        collision.Radius = evt.newValue;
                        _previewer.ForceRefresh();
                    });
                    _collisionContainer.Add(radiusField);
                    break;

                case CollisionShapeType.Capsule:
                    var capRadiusField = new FloatField("Radius") { value = collision.Radius };
                    capRadiusField.isDelayed = true;
                    capRadiusField.RegisterValueChangedCallback(evt =>
                    {
                        collision.Radius = evt.newValue;
                        _previewer.ForceRefresh();
                    });
                    _collisionContainer.Add(capRadiusField);

                    var heightField = new FloatField("Height") { value = collision.Height };
                    heightField.isDelayed = true;
                    heightField.RegisterValueChangedCallback(evt =>
                    {
                        collision.Height = evt.newValue;
                        _previewer.ForceRefresh();
                    });
                    _collisionContainer.Add(heightField);
                    break;

                case CollisionShapeType.Box:
                    // Simple X/Y/Z half-extent fields
                    var hx = new FloatField("HalfX") { value = collision.HalfExtents.x };
                    var hy = new FloatField("HalfY") { value = collision.HalfExtents.y };
                    var hz = new FloatField("HalfZ") { value = collision.HalfExtents.z };
                    hx.isDelayed = true;
                    hy.isDelayed = true;
                    hz.isDelayed = true;
                    hx.RegisterValueChangedCallback(evt =>
                    {
                        var he = collision.HalfExtents;
                        he.x = evt.newValue;
                        collision.HalfExtents = he;
                        _previewer.ForceRefresh();
                    });
                    hy.RegisterValueChangedCallback(evt =>
                    {
                        var he = collision.HalfExtents;
                        he.y = evt.newValue;
                        collision.HalfExtents = he;
                        _previewer.ForceRefresh();
                    });
                    hz.RegisterValueChangedCallback(evt =>
                    {
                        var he = collision.HalfExtents;
                        he.z = evt.newValue;
                        collision.HalfExtents = he;
                        _previewer.ForceRefresh();
                    });
                    _collisionContainer.Add(hx);
                    _collisionContainer.Add(hy);
                    _collisionContainer.Add(hz);
                    break;
            }

            ApplyLightTextTheme(_collisionContainer);
        }

        /// <summary>
        /// Create a Vector3 editor (3 float fields in a row) bound to a property.
        /// </summary>
        private VisualElement MakeVector3Editor(string label, object target,
            string propertyName, DataBinder binder)
        {
            var prop = target.GetType().GetProperty(propertyName);
            var vec = (Vector3)prop.GetValue(target);

            var container = new VisualElement();
            container.style.marginBottom = 2;

            var headerLabel = new Label(label);
            headerLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            headerLabel.style.fontSize = 11;
            container.Add(headerLabel);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;

            var xField = new FloatField("X") { value = vec.x };
            var yField = new FloatField("Y") { value = vec.y };
            var zField = new FloatField("Z") { value = vec.z };

            foreach (var f in new[] { xField, yField, zField })
            {
                f.isDelayed = true;
                f.style.flexGrow = 1;
                f.style.minWidth = 60;
                f.AddToClassList("compact-field");
                f.labelElement.style.minWidth = 14;
                f.labelElement.style.maxWidth = 14;
            }

            void OnChanged(int axis, float val)
            {
                var current = (Vector3)prop.GetValue(target);
                var newVec = current;
                switch (axis)
                {
                    case 0: newVec.x = val; break;
                    case 1: newVec.y = val; break;
                    case 2: newVec.z = val; break;
                }
                var cmd = new PropertyChangeCommand<Vector3>(
                    $"Change {propertyName}",
                    () => (Vector3)prop.GetValue(target),
                    v => prop.SetValue(target, v),
                    newVec);
                _commandStack.Execute(cmd);
            }

            xField.RegisterValueChangedCallback(evt => OnChanged(0, evt.newValue));
            yField.RegisterValueChangedCallback(evt => OnChanged(1, evt.newValue));
            zField.RegisterValueChangedCallback(evt => OnChanged(2, evt.newValue));

            row.Add(xField); row.Add(yField); row.Add(zField);
            container.Add(row);
            return container;
        }

        /// <summary>
        /// SpeedCurve 关键帧列表编辑器（内嵌在 speed_curve modifier 容器内）。
        /// 每个关键帧一行：[索引] T(FloatField) V(FloatField) [×删除]
        /// 底部有 [+ Keyframe] 添加按钮。
        /// </summary>
        private void BuildSpeedCurveEditor(VisualElement container,
            SpeedCurveModifier scm, DataBinder binder)
        {
            // 关键帧列表容器
            var kfContainer = new VisualElement();
            kfContainer.style.marginLeft = 4;  // 左缩进，视觉上表示从属关系

            void RebuildKeyframes()
            {
                kfContainer.Clear();
                var keyframes = scm.SpeedCurve.Keyframes;

                for (int k = 0; k < keyframes.Count; k++)
                {
                    int ki = k;
                    var kf = keyframes[k];

                    // ── 单个关键帧行（水平排列，允许换行） ──
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.flexWrap = Wrap.Wrap;  // ★ 窄面板时 T/V 字段自动换行
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 4;      // 行间距

                    // 关键帧索引标签 "[0]" "[1]" ...（灰色小字）
                    var indexLabel = new Label($"[{ki}]");
                    indexLabel.style.width = 20;
                    indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                    indexLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    indexLabel.style.fontSize = 10;
                    row.Add(indexLabel);

                    // T 值 FloatField（紧凑型，label 固定 14px）
                    // ★ minWidth=70 控制换行阈值，如果窄面板下数值被截断可适当减小
                    var timeField = new FloatField("T") { value = kf.Time };
                    timeField.isDelayed = true;
                    timeField.style.flexGrow = 1;
                    timeField.style.minWidth = 70;
                    timeField.AddToClassList("compact-field");  // 标记为紧凑型，跳过全局 label 宽度规则
                    timeField.labelElement.style.minWidth = 14; // label "T" 固定 14px
                    timeField.labelElement.style.maxWidth = 14;
                    timeField.RegisterValueChangedCallback(evt =>
                    {
                        float oldVal = kf.Time;
                        var cmd = new PropertyChangeCommand<float>(
                            $"Change keyframe {ki} time",
                            () => kf.Time,
                            v => kf.Time = v,
                            evt.newValue);
                        _commandStack.Execute(cmd);
                    });
                    row.Add(timeField);

                    // V 值 FloatField（紧凑型，label 固定 14px）
                    // ★ minWidth=70 控制换行阈值
                    var valField = new FloatField("V") { value = kf.Value };
                    valField.isDelayed = true;
                    valField.style.flexGrow = 1;
                    valField.style.minWidth = 70;
                    valField.AddToClassList("compact-field");  // 紧凑型
                    valField.labelElement.style.minWidth = 14;
                    valField.labelElement.style.maxWidth = 14;
                    valField.RegisterValueChangedCallback(evt =>
                    {
                        var cmd = new PropertyChangeCommand<float>(
                            $"Change keyframe {ki} value",
                            () => kf.Value,
                            v => kf.Value = v,
                            evt.newValue);
                        _commandStack.Execute(cmd);
                    });
                    row.Add(valField);

                    // [×] 删除关键帧按钮
                    var delBtn = new Button(() =>
                    {
                        int ci = keyframes.IndexOf(kf);
                        if (ci < 0) return;
                        var cmd = ListCommand<CurveKeyframe>.Remove(
                            keyframes, ci, "Remove keyframe");
                        _commandStack.Execute(cmd);
                    }) { text = "×" };
                    delBtn.style.width = 22;
                    row.Add(delBtn);

                    kfContainer.Add(row);
                }

                // [+ Keyframe] 添加关键帧按钮行
                var addRow = new VisualElement();
                addRow.style.flexDirection = FlexDirection.Row;
                addRow.style.marginTop = 2;

                var addBtn = new Button(() =>
                {
                    float lastTime = keyframes.Count > 0
                        ? keyframes[keyframes.Count - 1].Time + 0.5f : 0f;
                    float lastVal = keyframes.Count > 0
                        ? keyframes[keyframes.Count - 1].Value : 4f;
                    var newKf = new CurveKeyframe { Time = lastTime, Value = lastVal };
                    var cmd = ListCommand<CurveKeyframe>.Add(
                        keyframes, newKf, -1, "Add keyframe");
                    _commandStack.Execute(cmd);
                }) { text = "+ Keyframe" };
                addBtn.style.flexGrow = 1;
                addRow.Add(addBtn);

                kfContainer.Add(addRow);
                ApplyLightTextTheme(kfContainer);
            }

            kfContainer.userData = (Action)RebuildKeyframes;
            RebuildKeyframes();
            container.Add(kfContainer);
        }

        /// <summary>
        /// 强制将子树中所有文字设为浅色（适配深色背景），并统一 BaseField 的 label 宽度。
        ///
        /// ★ 调参指南：
        ///   - lightText: 所有文字颜色（Label, TextElement, Button 等）
        ///   - inputBg:   输入框背景色（.unity-base-field__input）
        ///   - labelWidth: 全局 label 宽度（Percent(38)），影响所有非 compact-field 的 BaseField
        ///     如果某些 label（如 "Speed"）被截断，可以：
        ///     1. 增大这个百分比
        ///     2. 或在对应的 Query 中给特定控件类型（如 Slider）单独设置固定像素值
        ///   - compact-field: 带此 CSS class 的 FloatField 跳过全局 label 宽度规则，
        ///     使用各自内联设置的固定像素 label 宽度（如 14px, 16px）
        /// </summary>
        private static void ApplyLightTextTheme(VisualElement root)
        {
            // 立即同步应用一次（覆盖新创建的元素）
            ForceApplyTheme(root);

            // 延迟应用作为兜底（覆盖 Unity Runtime Theme USS 的默认样式）
            root.schedule.Execute(() => ForceApplyTheme(root)).ExecuteLater(50);
            root.schedule.Execute(() => ForceApplyTheme(root)).ExecuteLater(200);
        }

        /// <summary>
        /// 实际的主题应用逻辑，可被直接调用。
        /// </summary>
        private static void ForceApplyTheme(VisualElement root)
        {
            var lightText = new Color(0.85f, 0.85f, 0.85f);
            var inputBg = new Color(0.22f, 0.22f, 0.22f);
            var labelWidth = new Length(38, LengthUnit.Percent);

            // 所有 Label 和 TextElement 设为浅色
            root.Query<Label>().ForEach(l => l.style.color = lightText);
            root.Query<TextElement>().ForEach(t => t.style.color = lightText);
            // 输入框：浅色文字 + 深灰背景
            root.Query(className: "unity-base-field__input").ForEach(e =>
            {
                e.style.color = lightText;
                e.style.backgroundColor = inputBg;
            });
            root.Query(className: "unity-text-element").ForEach(e =>
            {
                e.style.color = lightText;
            });
            // 按钮：浅色文字 + 稍亮灰色背景
            root.Query<Button>().ForEach(b =>
            {
                b.style.color = lightText;
                b.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            });

            // ── 统一 label 宽度（跳过 compact-field） ──
            root.Query<FloatField>().ForEach(f =>
            {
                if (f.ClassListContains("compact-field"))
                {
                    f.style.height = 24;
                    f.style.alignItems = Align.Center;
                    f.labelElement.style.height = 20;
                    f.labelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                    f.labelElement.style.paddingTop = 0;
                    f.labelElement.style.paddingBottom = 0;
                    f.labelElement.style.marginTop = 0;
                    f.labelElement.style.marginBottom = 0;
                    f.Query(className: "unity-base-field__input").ForEach(inp =>
                    {
                        inp.style.height = 20;
                        inp.style.paddingTop = 2;
                        inp.style.paddingBottom = 2;
                        inp.style.marginTop = 0;
                        inp.style.marginBottom = 0;
                    });
                    return;
                }
                f.labelElement.style.minWidth = labelWidth;
                f.labelElement.style.maxWidth = labelWidth;
            });
            // IntegerField
            root.Query<IntegerField>().ForEach(f =>
            {
                f.labelElement.style.color = lightText;
                if (f.ClassListContains("seed-field"))
                {
                    // Seed field: compact style to fit in tight spaces
                    f.style.fontSize = 10;
                    f.style.paddingTop = 0;
                    f.style.paddingBottom = 0;
                    f.style.marginTop = 0;
                    f.style.marginBottom = 0;
                    f.labelElement.style.paddingTop = 0;
                    f.labelElement.style.paddingBottom = 0;
                    f.Query(className: "unity-base-field__input").ForEach(inp =>
                    {
                        inp.style.color = lightText;
                        inp.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
                        inp.style.paddingTop = 0;
                        inp.style.paddingBottom = 0;
                        inp.style.paddingLeft = 2;
                        inp.style.paddingRight = 2;
                        inp.style.marginTop = 0;
                        inp.style.marginBottom = 0;
                    });
                }
                else
                {
                    // Normal IntegerField: same layout as FloatField
                    f.labelElement.style.minWidth = labelWidth;
                    f.labelElement.style.maxWidth = labelWidth;
                    f.Query(className: "unity-base-field__input").ForEach(inp =>
                    {
                        inp.style.color = lightText;
                        inp.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
                    });
                }
            });
            // DropdownField
            root.Query<DropdownField>().ForEach(f =>
            {
                f.labelElement.style.minWidth = labelWidth;
                f.labelElement.style.maxWidth = labelWidth;
            });
            // Slider
            root.Query<Slider>().ForEach(f =>
            {
                f.labelElement.style.minWidth = 50;
                f.labelElement.style.maxWidth = 50;
            });
            // Toggle
            root.Query<Toggle>().ForEach(f =>
            {
                f.labelElement.style.minWidth = labelWidth;
                f.labelElement.style.maxWidth = labelWidth;
            });
            // Vector3Field
            root.Query<Vector3Field>().ForEach(f =>
            {
                f.labelElement.style.minWidth = labelWidth;
                f.labelElement.style.maxWidth = labelWidth;
            });
        }

        /// <summary>
        /// 创建区段标题（蓝色粗体 + 底部分隔线）。
        /// 用于 "Pattern" / "Emitter" / "Modifiers" / "Playback" / "File" 五个区段。
        /// ★ marginTop 控制区段标题与上方最后一个字段的间距，如果重叠就增大这个值。
        /// </summary>
        private static Label MakeHeader(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 13;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 16;   // ★ 区段标题上方间距（防止与上一区段最后一个字段重叠）
            label.style.marginBottom = 6; // 区段标题下方间距（标题到第一个字段）
            label.style.color = new Color(0.5f, 0.8f, 1f);          // 浅蓝色
            label.style.borderBottomWidth = 1;                        // 底部分隔线
            label.style.borderBottomColor = new Color(0.3f, 0.5f, 0.7f);
            return label;
        }

        private static BulletPattern CreateDefaultPattern()
        {
            return new BulletPattern
            {
                Id = "default",
                Name = "default",
                Emitter = new RingEmitter { Count = 12, Radius = 0.5f, Speed = 4f },
                Modifiers = new List<IModifier>(),
                BulletScale = 0.15f,
                BulletColor = new Color(1f, 0.3f, 0.3f, 1f),
                Duration = 5f,
            };
        }
    }

    /// <summary>
    /// 自定义 RGBA 颜色编辑器（Runtime UI Toolkit 没有内置 ColorField）。
    /// 布局：上方一行 "Color" 标签，下方一行 R/G/B/A 四个紧凑型 FloatField（允许换行）。
    /// </summary>
    public class ColorField : VisualElement
    {
        private readonly FloatField _r, _g, _b, _a;  // R/G/B/A 四个通道的 FloatField
        private Color _color;
        private bool _suppressEvents;

        public Action<Color> OnColorChanged;

        public ColorField(string label)
        {
            style.flexDirection = FlexDirection.Column;
            style.marginBottom = 4;  // ColorField 整体与下方字段的间距

            // "Color" 标签
            var header = new Label(label);
            header.style.color = new Color(0.8f, 0.8f, 0.8f);
            header.style.marginBottom = 2;  // 标签与下方 RGBA 行的间距
            Add(header);

            // RGBA 通道行（水平排列，允许换行）
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;  // ★ 窄面板时 RGBA 自动换行
            row.style.alignItems = Align.Center;

            _r = MakeChannel("R", 0);
            _g = MakeChannel("G", 1);
            _b = MakeChannel("B", 2);
            _a = MakeChannel("A", 3);

            row.Add(_r); row.Add(_g); row.Add(_b); row.Add(_a);
            Add(row);
        }

        public void SetColor(Color c)
        {
            _color = c;
            _suppressEvents = true;
            _r.SetValueWithoutNotify(c.r);
            _g.SetValueWithoutNotify(c.g);
            _b.SetValueWithoutNotify(c.b);
            _a.SetValueWithoutNotify(c.a);
            _suppressEvents = false;
        }

        // 创建单个颜色通道的紧凑型 FloatField（R/G/B/A）
        private FloatField MakeChannel(string name, int index)
        {
            var field = new FloatField(name);
            field.isDelayed = true;
            field.style.flexGrow = 1;
            field.style.minWidth = 40;   // ★ 单个通道最小宽度，影响换行阈值
            field.AddToClassList("compact-field");  // 紧凑型，跳过全局 label 宽度规则
            field.labelElement.style.minWidth = 16;  // label "R"/"G"/"B"/"A" 固定 16px
            field.labelElement.style.maxWidth = 16;
            field.labelElement.style.marginRight = 2; // label 与输入框的间距
            field.style.marginRight = 4;  // 通道之间的间距
            field.RegisterValueChangedCallback(evt =>
            {
                if (_suppressEvents) return;
                switch (index)
                {
                    case 0: _color.r = evt.newValue; break;
                    case 1: _color.g = evt.newValue; break;
                    case 2: _color.b = evt.newValue; break;
                    case 3: _color.a = evt.newValue; break;
                }
                OnColorChanged?.Invoke(_color);
            });
            return field;
        }
    }
}
