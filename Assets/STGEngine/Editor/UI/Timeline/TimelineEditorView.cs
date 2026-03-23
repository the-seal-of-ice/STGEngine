using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Core.Serialization;
using STGEngine.Editor.Commands;
using STGEngine.Editor.UI;
using STGEngine.Runtime;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline
{
    /// <summary>
    /// Main timeline editor view. Composes breadcrumb, toolbar, segment list,
    /// track area, and property panel into a unified editing experience.
    /// </summary>
    public class TimelineEditorView : IDisposable
    {
        public VisualElement Root { get; }
        public CommandStack Commands => _commandStack;

        /// <summary>Callback for MeshType changes (scene setup hooks this).</summary>
        public Action<MeshType> OnMeshTypeChanged;

        private readonly TimelinePlaybackController _playback;
        private readonly PatternLibrary _library;
        private readonly CommandStack _commandStack = new();

        private Stage _stage;
        private PatternPreviewer _singlePreviewer; // For property panel pattern editing

        // Sub-views
        private readonly SegmentListView _segmentList;
        private readonly TrackAreaView _trackArea;

        // UI elements
        private readonly VisualElement _breadcrumbBar;
        private readonly Label _breadcrumbStage;
        private readonly Label _breadcrumbSegment;
        private readonly VisualElement _toolbar;
        private readonly VisualElement _propertyPanel;
        private readonly ScrollView _propertyContent;
        private readonly Label _propertyHeaderLabel;

        // Playback UI
        private Button _playPauseBtn;
        private Label _timeLabel;
        private Slider _speedSlider;

        // Pattern editor for selected event
        private PatternEditorView _patternEditor;

        public TimelineEditorView(TimelinePlaybackController playback, PatternLibrary library,
            PatternPreviewer singlePreviewer)
        {
            _playback = playback;
            _library = library;
            _singlePreviewer = singlePreviewer;

            Root = new VisualElement();
            Root.style.flexGrow = 0;
            Root.style.flexDirection = FlexDirection.Column;

            // ── Breadcrumb Bar ──
            _breadcrumbBar = new VisualElement();
            _breadcrumbBar.style.flexDirection = FlexDirection.Row;
            _breadcrumbBar.style.alignItems = Align.Center;
            _breadcrumbBar.style.height = 28;
            _breadcrumbBar.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
            _breadcrumbBar.style.paddingLeft = 8;
            _breadcrumbBar.style.paddingRight = 8;
            _breadcrumbBar.style.borderBottomWidth = 1;
            _breadcrumbBar.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

            _breadcrumbStage = new Label("Stage");
            _breadcrumbStage.style.color = new Color(0.85f, 0.85f, 0.85f);
            _breadcrumbStage.style.marginRight = 4;
            _breadcrumbBar.Add(_breadcrumbStage);

            var sep = new Label(">");
            sep.style.color = new Color(0.5f, 0.5f, 0.5f);
            sep.style.marginLeft = 4;
            sep.style.marginRight = 4;
            _breadcrumbBar.Add(sep);

            _breadcrumbSegment = new Label("—");
            _breadcrumbSegment.style.color = new Color(0.5f, 0.8f, 1f);
            _breadcrumbSegment.style.unityFontStyleAndWeight = FontStyle.Bold;
            _breadcrumbBar.Add(_breadcrumbSegment);

            Root.Add(_breadcrumbBar);

            // ── Toolbar ──
            _toolbar = new VisualElement();
            _toolbar.style.flexDirection = FlexDirection.Row;
            _toolbar.style.alignItems = Align.Center;
            _toolbar.style.height = 30;
            _toolbar.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f, 0.95f);
            _toolbar.style.paddingLeft = 4;
            _toolbar.style.paddingRight = 4;
            _toolbar.style.borderBottomWidth = 1;
            _toolbar.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            BuildToolbar();
            Root.Add(_toolbar);

            // ── Main split: Segment List + Track Area ──
            var mainSplit = new VisualElement();
            mainSplit.style.flexDirection = FlexDirection.Row;
            mainSplit.style.flexGrow = 1;

            _segmentList = new SegmentListView(_commandStack);
            _segmentList.OnSegmentSelected += OnSegmentSelected;
            _segmentList.OnStageChanged += OnStageDataChanged;
            mainSplit.Add(_segmentList.Root);

            _trackArea = new TrackAreaView(_commandStack);
            _trackArea.OnEventSelected += OnEventSelected;
            _trackArea.OnEventsChanged += OnStageDataChanged;
            _trackArea.OnSeekRequested += OnSeekRequested;
            _trackArea.OnAddEventRequested += OnAddEventRequested;
            mainSplit.Add(_trackArea.Root);

            Root.Add(mainSplit);

            // ── Property Panel (Bottom) ──
            _propertyPanel = new VisualElement();
            _propertyPanel.style.height = 200;
            _propertyPanel.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            _propertyPanel.style.borderTopWidth = 1;
            _propertyPanel.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);

            var propHeader = new VisualElement();
            propHeader.style.flexDirection = FlexDirection.Row;
            propHeader.style.alignItems = Align.Center;
            propHeader.style.height = 24;
            propHeader.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            propHeader.style.paddingLeft = 8;
            propHeader.style.borderBottomWidth = 1;
            propHeader.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

            _propertyHeaderLabel = new Label("Properties");
            _propertyHeaderLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _propertyHeaderLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            propHeader.Add(_propertyHeaderLabel);
            _propertyPanel.Add(propHeader);

            _propertyContent = new ScrollView(ScrollViewMode.Vertical);
            _propertyContent.style.flexGrow = 1;
            _propertyPanel.Add(_propertyContent);

            Root.Add(_propertyPanel);

            // Subscribe to playback events
            _playback.OnTimeChanged += OnPlaybackTimeChanged;
            _playback.OnPlayStateChanged += OnPlayStateChanged;
            _commandStack.OnStateChanged += OnCommandStateChanged;

            // Register delayed theme override on Root — ensures inline styles survive
            // even if Unity Runtime Theme USS re-applies after attach.
            RegisterThemeOverride(Root);

            // Load default stage
            LoadDefaultStage();
        }

        public void Dispose()
        {
            _playback.OnTimeChanged -= OnPlaybackTimeChanged;
            _playback.OnPlayStateChanged -= OnPlayStateChanged;
            _commandStack.OnStateChanged -= OnCommandStateChanged;

            _patternEditor?.Dispose();
            _segmentList.Dispose();
            _trackArea.Dispose();
        }

        // ─── Stage Loading ───

        public void SetStage(Stage stage)
        {
            _stage = stage;
            _breadcrumbStage.text = stage?.Name ?? "Stage";

            // Resolve all pattern references
            if (_stage != null)
            {
                foreach (var seg in _stage.Segments)
                {
                    foreach (var evt in seg.Events)
                    {
                        if (evt is SpawnPatternEvent spawnEvt)
                            spawnEvt.ResolvedPattern = _library.Resolve(spawnEvt.PatternId);
                    }
                }
            }

            _segmentList.SetStage(stage);
        }

        private void LoadDefaultStage()
        {
            var asset = Resources.Load<TextAsset>("Stages/demo_stage");
            if (asset != null)
            {
                try
                {
                    var stage = YamlSerializer.DeserializeStage(asset.text);
                    SetStage(stage);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TimelineEditor] Failed to load demo stage: {e.Message}");
                    SetStage(CreateEmptyStage());
                }
            }
            else
            {
                SetStage(CreateEmptyStage());
            }
        }

        private Stage CreateEmptyStage()
        {
            var stage = new Stage
            {
                Id = "new_stage",
                Name = "New Stage",
                Segments = new()
                {
                    new TimelineSegment
                    {
                        Id = "segment_1",
                        Name = "Segment 1",
                        Type = SegmentType.MidStage,
                        Duration = 30f
                    }
                }
            };
            return stage;
        }

        // ─── Toolbar ───

        private void BuildToolbar()
        {
            _playPauseBtn = new Button(OnTogglePlay) { text = "▶" };
            _playPauseBtn.style.width = 32;
            _playPauseBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            _playPauseBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toolbar.Add(_playPauseBtn);

            var stopBtn = new Button(OnStop) { text = "■" };
            stopBtn.style.width = 32;
            stopBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            stopBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toolbar.Add(stopBtn);

            var stepBtn = new Button(OnStepFrame) { text = "▶|" };
            stepBtn.style.width = 32;
            stepBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            stepBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toolbar.Add(stepBtn);

            _timeLabel = new Label("0.00s");
            _timeLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _timeLabel.style.minWidth = 60;
            _timeLabel.style.marginLeft = 8;
            _toolbar.Add(_timeLabel);

            var speedLabel = new Label("Speed:");
            speedLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            speedLabel.style.marginLeft = 8;
            _toolbar.Add(speedLabel);

            _speedSlider = new Slider(0.1f, 3f) { value = 1f };
            _speedSlider.style.width = 80;
            _speedSlider.RegisterValueChangedCallback(e => _playback.PlaybackSpeed = e.newValue);
            _toolbar.Add(_speedSlider);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            _toolbar.Add(spacer);

            var fitBtn = new Button(OnZoomToFit) { text = "Fit" };
            fitBtn.style.width = 40;
            fitBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            fitBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toolbar.Add(fitBtn);

            var saveBtn = new Button(OnSaveStage) { text = "Save" };
            saveBtn.style.width = 44;
            saveBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            saveBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            saveBtn.style.marginLeft = 4;
            _toolbar.Add(saveBtn);

            var loadBtn = new Button(OnLoadStage) { text = "Load" };
            loadBtn.style.width = 44;
            loadBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            loadBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            loadBtn.style.marginLeft = 4;
            _toolbar.Add(loadBtn);
        }

        // ─── Toolbar Actions ───

        private void OnTogglePlay() => _playback.TogglePlay();
        private void OnStop() => _playback.Reset();
        private void OnStepFrame() => _playback.StepFrame();
        private void OnZoomToFit() => _trackArea.ZoomToFit();

        private void OnSaveStage()
        {
            if (_stage == null) return;
            string path = Path.Combine(Application.dataPath, "Resources", "Stages", $"{_stage.Id}.yaml");
            try
            {
                YamlSerializer.SerializeStageToFile(_stage, path);
                Debug.Log($"[TimelineEditor] Stage saved to {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Save failed: {e.Message}");
            }
        }

        private void OnLoadStage()
        {
            // For now, reload the demo stage
            LoadDefaultStage();
        }

        // ─── Event Handlers ───

        private void OnSegmentSelected(TimelineSegment segment)
        {
            _breadcrumbSegment.text = segment?.Name ?? "—";
            _trackArea.SetSegment(segment);
            _playback.LoadSegment(segment);
        }

        private void OnEventSelected(SpawnPatternEvent evt)
        {
            _propertyContent.Clear();
            _patternEditor?.Dispose();
            _patternEditor = null;

            if (evt == null)
            {
                _propertyHeaderLabel.text = "Properties";
                return;
            }

            _propertyHeaderLabel.text = $"Event: {evt.PatternId}";

            // Show event properties
            var eventProps = new VisualElement();
            eventProps.style.paddingTop = 4;
            eventProps.style.paddingLeft = 8;
            eventProps.style.paddingRight = 8;

            // Start Time
            var startField = new FloatField("Start Time") { value = evt.StartTime };
            startField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Start Time",
                    () => evt.StartTime,
                    v => evt.StartTime = v,
                    Mathf.Max(0f, e.newValue));
                _commandStack.Execute(cmd);
            });
            eventProps.Add(startField);

            // Duration
            var durField = new FloatField("Duration") { value = evt.Duration };
            durField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Duration",
                    () => evt.Duration,
                    v => evt.Duration = v,
                    Mathf.Max(0.1f, e.newValue));
                _commandStack.Execute(cmd);
            });
            eventProps.Add(durField);

            // Pattern ID
            var patternLabel = new Label($"Pattern: {evt.PatternId}");
            patternLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            patternLabel.style.marginTop = 4;
            eventProps.Add(patternLabel);

            // Spawn Position
            var posLabel = new Label("Spawn Position");
            posLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            posLabel.style.marginTop = 8;
            eventProps.Add(posLabel);

            var posX = new FloatField("X") { value = evt.SpawnPosition.x };
            var posY = new FloatField("Y") { value = evt.SpawnPosition.y };
            var posZ = new FloatField("Z") { value = evt.SpawnPosition.z };

            Action updatePos = () =>
            {
                var newPos = new Vector3(posX.value, posY.value, posZ.value);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change Spawn Position",
                    () => evt.SpawnPosition,
                    v => evt.SpawnPosition = v,
                    newPos);
                _commandStack.Execute(cmd);
            };

            posX.RegisterValueChangedCallback(e => updatePos());
            posY.RegisterValueChangedCallback(e => updatePos());
            posZ.RegisterValueChangedCallback(e => updatePos());

            eventProps.Add(posX);
            eventProps.Add(posY);
            eventProps.Add(posZ);

            // Inline pattern editor if resolved
            if (evt.ResolvedPattern != null && _singlePreviewer != null)
            {
                var separator = new VisualElement();
                separator.style.height = 1;
                separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                separator.style.marginTop = 8;
                separator.style.marginBottom = 4;
                eventProps.Add(separator);

                var patternHeader = new Label("Pattern Parameters");
                patternHeader.style.color = new Color(0.8f, 0.8f, 0.8f);
                patternHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                patternHeader.style.marginBottom = 4;
                eventProps.Add(patternHeader);

                _singlePreviewer.Pattern = evt.ResolvedPattern;
                _patternEditor = new PatternEditorView(_singlePreviewer);
                _patternEditor.OnMeshTypeChanged = OnMeshTypeChanged;
                _patternEditor.SetPattern(evt.ResolvedPattern);

                // Embed the pattern editor root (without its outer container positioning)
                var editorRoot = _patternEditor.Root;
                editorRoot.style.width = Length.Percent(100);
                editorRoot.style.minWidth = StyleKeyword.Auto;
                editorRoot.style.maxWidth = StyleKeyword.Auto;
                editorRoot.style.backgroundColor = Color.clear;
                eventProps.Add(editorRoot);
            }

            _propertyContent.Add(eventProps);
            ApplyLightTextTheme(eventProps);
        }

        private void OnPlaybackTimeChanged(float time)
        {
            _trackArea.SetPlayTime(time);
            _timeLabel.text = $"{time:F2}s";
        }

        private void OnPlayStateChanged(bool playing)
        {
            _playPauseBtn.text = playing ? "⏸" : "▶";
        }

        private void OnSeekRequested(float time)
        {
            _playback.Seek(time);
        }

        private void OnAddEventRequested(float atTime)
        {
            if (_stage == null) return;

            // Show pattern picker (simple: use first available pattern)
            var patternIds = new List<string>(_library.PatternIds);
            if (patternIds.Count == 0)
            {
                Debug.LogWarning("[TimelineEditor] No patterns available.");
                return;
            }

            ShowPatternPicker(patternIds, atTime);
        }

        private void ShowPatternPicker(List<string> patternIds, float atTime)
        {
            var picker = new VisualElement();
            picker.style.position = Position.Absolute;
            picker.style.left = Length.Percent(30);
            picker.style.top = Length.Percent(30);
            picker.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.98f);
            picker.style.borderTopWidth = picker.style.borderBottomWidth =
                picker.style.borderLeftWidth = picker.style.borderRightWidth = 1;
            picker.style.borderTopColor = picker.style.borderBottomColor =
                picker.style.borderLeftColor = picker.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            picker.style.paddingTop = picker.style.paddingBottom = 8;
            picker.style.paddingLeft = picker.style.paddingRight = 12;
            picker.style.minWidth = 200;

            var title = new Label("Select Pattern");
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            picker.Add(title);

            foreach (var pid in patternIds)
            {
                var btn = new Button(() =>
                {
                    picker.RemoveFromHierarchy();
                    CreateEventWithPattern(pid, atTime);
                })
                { text = pid };
                btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
                btn.style.color = new Color(0.9f, 0.9f, 0.9f);
                btn.style.marginBottom = 2;
                picker.Add(btn);
            }

            var cancelBtn = new Button(() => picker.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn.style.marginTop = 4;
            picker.Add(cancelBtn);

            Root.panel.visualTree.Add(picker);
        }

        private void CreateEventWithPattern(string patternId, float atTime)
        {
            var pattern = _library.Resolve(patternId);
            if (pattern == null) return;

            var evt = new SpawnPatternEvent
            {
                Id = $"evt_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                StartTime = atTime,
                Duration = pattern.Duration,
                PatternId = patternId,
                SpawnPosition = new Vector3(0, 5, 0),
                ResolvedPattern = pattern
            };

            _trackArea.AddEvent(evt);
        }

        private void OnStageDataChanged()
        {
            _trackArea.RebuildBlocks();
        }

        private void OnCommandStateChanged()
        {
            _trackArea.RebuildBlocks();
        }

        // ─── Theme ───

        /// <summary>
        /// Force-apply light text theme to the entire timeline UI tree.
        /// Called externally by PatternSandboxSetup via coroutine as a safety net.
        /// </summary>
        public void ForceApplyTheme()
        {
            ApplyThemeToTree(Root);
        }

        private static readonly Color Lt = new(0.85f, 0.85f, 0.85f);
        private static readonly Color DimText = new(0.7f, 0.7f, 0.7f);
        private static readonly Color InputBg = new(0.22f, 0.22f, 0.22f);
        private static readonly Color BtnBg = new(0.28f, 0.28f, 0.28f);
        private static readonly Color BtnBorder = new(0.35f, 0.35f, 0.35f);

        /// <summary>
        /// Walk the visual tree and force all text/button/input styles.
        /// Mirrors PatternEditorView.ApplyLightTextTheme logic.
        /// </summary>
        internal static void ApplyThemeToTree(VisualElement root)
        {
            // All text → light color
            root.Query<Label>().ForEach(l => l.style.color = Lt);
            root.Query<TextElement>().ForEach(t => t.style.color = Lt);
            root.Query(className: "unity-text-element").ForEach(e => e.style.color = Lt);

            // Input fields
            root.Query(className: "unity-base-field__input").ForEach(e =>
            {
                e.style.color = Lt;
                e.style.backgroundColor = InputBg;
            });

            // Buttons: light text, fixed size, no stretch
            root.Query<Button>().ForEach(b =>
            {
                b.style.color = Lt;
                b.style.backgroundColor = BtnBg;
                b.style.flexGrow = 0;
                b.style.flexShrink = 0;
                b.style.borderTopWidth = b.style.borderBottomWidth =
                    b.style.borderLeftWidth = b.style.borderRightWidth = 1;
                b.style.borderTopColor = b.style.borderBottomColor =
                    b.style.borderLeftColor = b.style.borderRightColor = BtnBorder;
            });

            // Sliders
            root.Query<Slider>().ForEach(f =>
            {
                f.labelElement.style.color = Lt;
                f.labelElement.style.minWidth = 50;
                f.labelElement.style.maxWidth = 50;
            });

            // FloatField label width (property panel)
            var labelWidth = new Length(38, LengthUnit.Percent);
            root.Query<FloatField>().ForEach(f =>
            {
                f.labelElement.style.color = Lt;
                if (!f.ClassListContains("compact-field"))
                {
                    f.labelElement.style.minWidth = labelWidth;
                    f.labelElement.style.maxWidth = labelWidth;
                }
            });

            // DropdownField
            root.Query<DropdownField>().ForEach(f =>
            {
                f.style.color = Lt;
                f.labelElement.style.color = Lt;
                f.Query(className: "unity-base-field__input").ForEach(e =>
                {
                    e.style.color = Lt;
                    e.style.backgroundColor = InputBg;
                });
                f.Query(className: "unity-text-element").ForEach(e => e.style.color = Lt);
            });
        }

        /// <summary>
        /// Register delayed theme override on a visual element.
        /// Uses AttachToPanelEvent + schedule.Execute (50ms/200ms) + GeometryChangedEvent
        /// to ensure styles are applied AFTER Unity Runtime Theme.
        /// </summary>
        internal static void RegisterThemeOverride(VisualElement root)
        {
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                root.schedule.Execute(() => ApplyThemeToTree(root)).ExecuteLater(50);
                root.schedule.Execute(() => ApplyThemeToTree(root)).ExecuteLater(200);
            });
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyThemeToTree(root));
        }

        private static void ApplyLightTextTheme(VisualElement root)
        {
            // Convenience alias for dynamically added sub-trees (property panel, picker, etc.)
            RegisterThemeOverride(root);
        }
    }
}
