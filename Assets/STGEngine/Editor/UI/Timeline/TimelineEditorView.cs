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
using STGEngine.Editor.UI.FileManager;
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
        private readonly VisualElement _mainSplit;
        private readonly VisualElement _propertyPanel;
        private readonly ScrollView _propertyContent;
        private readonly Label _propertyHeaderLabel;
        private readonly Button _toggleBtn;
        private bool _propertyCollapsed;

        /// <summary>
        /// Standalone floating property panel. Add this to the UIDocument root
        /// (not inside Timeline Root) so it can float above the 3D viewport.
        /// </summary>
        public VisualElement PropertyPanel => _propertyPanel;

        // Playback UI
        private Button _playPauseBtn;
        private Label _timeLabel;
        private Slider _speedSlider;
        private Label _speedValueLabel;

        // Stage seed UI
        private IntegerField _stageSeedField;

        // Pattern editor for selected event
        private PatternEditorView _patternEditor;

        // Live-updated property fields for the selected event
        private FloatField _propStartField;
        private FloatField _propDurField;
        private SpawnPatternEvent _selectedEvent;

        public TimelineEditorView(TimelinePlaybackController playback, PatternLibrary library,
            PatternPreviewer singlePreviewer)
        {
            _playback = playback;
            _library = library;
            _singlePreviewer = singlePreviewer;

            Root = new VisualElement();
            Root.style.flexGrow = 1;
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

            _breadcrumbSegment = new Label("\u2014");
            _breadcrumbSegment.style.color = new Color(0.5f, 0.8f, 1f);
            _breadcrumbSegment.style.unityFontStyleAndWeight = FontStyle.Bold;
            _breadcrumbBar.Add(_breadcrumbSegment);

            // Spacer to push seed controls to the right
            var breadcrumbSpacer = new VisualElement();
            breadcrumbSpacer.style.flexGrow = 1;
            _breadcrumbBar.Add(breadcrumbSpacer);

            // Stage Seed controls
            var seedLabel = new Label("Seed:");
            seedLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            seedLabel.style.fontSize = 11;
            seedLabel.style.marginRight = 4;
            _breadcrumbBar.Add(seedLabel);

            _stageSeedField = new IntegerField();
            _stageSeedField.isDelayed = true;
            _stageSeedField.AddToClassList("seed-field");
            _stageSeedField.style.width = 80;
            _stageSeedField.style.height = 22;
            _stageSeedField.RegisterValueChangedCallback(e =>
            {
                if (_stage == null) return;
                _stage.Seed = e.newValue;
            });
            _breadcrumbBar.Add(_stageSeedField);

            var seedRandomBtn = new Button(() =>
            {
                if (_stage == null) return;
                int newSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                _stage.Seed = newSeed;
                _stageSeedField.SetValueWithoutNotify(newSeed);
            }) { text = "Rnd" };
            seedRandomBtn.style.width = 34;
            seedRandomBtn.style.height = 22;
            seedRandomBtn.style.fontSize = 11;
            seedRandomBtn.style.marginLeft = 4;
            _breadcrumbBar.Add(seedRandomBtn);

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
            _mainSplit = new VisualElement();
            _mainSplit.style.flexDirection = FlexDirection.Row;
            _mainSplit.style.flexGrow = 1;

            _segmentList = new SegmentListView(_commandStack);
            _segmentList.OnSegmentSelected += OnSegmentSelected;
            _segmentList.OnStageChanged += OnStageDataChanged;
            _mainSplit.Add(_segmentList.Root);

            _trackArea = new TrackAreaView(_commandStack);
            _trackArea.OnEventSelected += OnEventSelected;
            _trackArea.OnEventsChanged += OnStageDataChanged;
            _trackArea.OnEventValuesChanged += OnEventValuesChanged;
            _trackArea.OnSeekRequested += OnSeekRequested;
            _trackArea.OnAddEventRequested += OnAddEventRequested;
            _mainSplit.Add(_trackArea.Root);

            Root.Add(_mainSplit);

            // ── Property Panel (floating right-side, managed externally) ──
            _propertyPanel = new VisualElement();
            _propertyPanel.style.width = new Length(18, LengthUnit.Percent);
            _propertyPanel.style.minWidth = 280;
            _propertyPanel.style.maxWidth = 400;
            _propertyPanel.style.flexGrow = 1;
            _propertyPanel.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            _propertyPanel.style.borderLeftWidth = 1;
            _propertyPanel.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);

            // Header bar with toggle button + title
            var propHeader = new VisualElement();
            propHeader.style.flexDirection = FlexDirection.Row;
            propHeader.style.alignItems = Align.Center;
            propHeader.style.height = 26;
            propHeader.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            propHeader.style.paddingLeft = 4;
            propHeader.style.paddingRight = 8;
            propHeader.style.borderBottomWidth = 1;
            propHeader.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

            _toggleBtn = new Button(TogglePropertyPanel) { text = "\u25c0" }; // ◀
            _toggleBtn.style.width = 24;
            _toggleBtn.style.height = 20;
            _toggleBtn.style.fontSize = 10;
            _toggleBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            _toggleBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toggleBtn.style.marginRight = 4;
            _toggleBtn.style.borderTopWidth = _toggleBtn.style.borderBottomWidth =
                _toggleBtn.style.borderLeftWidth = _toggleBtn.style.borderRightWidth = 1;
            _toggleBtn.style.borderTopColor = _toggleBtn.style.borderBottomColor =
                _toggleBtn.style.borderLeftColor = _toggleBtn.style.borderRightColor =
                    new Color(0.35f, 0.35f, 0.35f);
            propHeader.Add(_toggleBtn);

            _propertyHeaderLabel = new Label("Properties");
            _propertyHeaderLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _propertyHeaderLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _propertyHeaderLabel.style.flexGrow = 1;
            propHeader.Add(_propertyHeaderLabel);
            _propertyPanel.Add(propHeader);

            _propertyContent = new ScrollView(ScrollViewMode.Vertical);
            _propertyContent.style.flexGrow = 1;
            _propertyPanel.Add(_propertyContent);

            // NOTE: _propertyPanel is NOT added to Root here.
            // PatternSandboxSetup will add it to the UIDocument root as a
            // floating overlay positioned above the timeline area.
            RegisterThemeOverride(_propertyPanel);

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

            if (_patternEditor != null)
                _patternEditor.Commands.OnStateChanged -= OnPatternEditorChanged;

            CloseSnapPopup();
            _patternEditor?.Dispose();
            _segmentList.Dispose();
            _trackArea.Dispose();
        }

        // ─── Stage Loading ───

        public void SetStage(Stage stage)
        {
            _stage = stage;
            _breadcrumbStage.text = stage?.Name ?? "Stage";
            _stageSeedField.SetValueWithoutNotify(stage?.Seed ?? 0);

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
            // Prefer STGData catalog path
            var catalog = STGCatalog.Load();
            string path = catalog.GetStagePath("demo_stage");
            if (File.Exists(path))
            {
                try
                {
                    var stage = YamlSerializer.DeserializeStageFromFile(path);
                    SetStage(stage);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TimelineEditor] Failed to load stage from disk: {e.Message}");
                }
            }

            // Fallback: try legacy Resources path (first launch before migration)
            var legacyPath = Path.Combine(Application.dataPath, "Resources", "Stages", "demo_stage.yaml");
            if (File.Exists(legacyPath))
            {
                try
                {
                    var stage = YamlSerializer.DeserializeStageFromFile(legacyPath);
                    SetStage(stage);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TimelineEditor] Failed to load legacy stage: {e.Message}");
                }
            }

            // Fallback: try Resources.Load (packaged builds)
            var asset = Resources.Load<TextAsset>("STGData/Stages/demo_stage");
            if (asset == null)
                asset = Resources.Load<TextAsset>("Stages/demo_stage");

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
            _playPauseBtn = new Button(OnTogglePlay) { text = "\u25b6" };
            _playPauseBtn.style.width = 32;
            _playPauseBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            _playPauseBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toolbar.Add(_playPauseBtn);

            var stopBtn = new Button(OnStop) { text = "\u25a0" };
            stopBtn.style.width = 32;
            stopBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            stopBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            _toolbar.Add(stopBtn);

            var stepBtn = new Button(OnStepFrame) { text = "\u25b6|" };
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
            _speedSlider.RegisterValueChangedCallback(e =>
            {
                _playback.PlaybackSpeed = e.newValue;
                _speedValueLabel.text = $"{e.newValue:F1}x";
            });
            _toolbar.Add(_speedSlider);

            _speedValueLabel = new Label("1.0x");
            _speedValueLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _speedValueLabel.style.minWidth = 32;
            _speedValueLabel.style.marginLeft = 2;
            _toolbar.Add(_speedValueLabel);

            var resetSpeedBtn = new Button(() =>
            {
                _speedSlider.value = 1f;
                _playback.PlaybackSpeed = 1f;
                _speedValueLabel.text = "1.0x";
            })
            { text = "1x" };
            resetSpeedBtn.style.width = 28;
            resetSpeedBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            resetSpeedBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            resetSpeedBtn.style.marginLeft = 2;
            _toolbar.Add(resetSpeedBtn);

            // ── Snap dropdown ──
            BuildSnapDropdown();

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

        private VisualElement _snapPopup;
        private VisualElement _snapDismiss;
        private FloatField _snapPhField;
        private FloatField _snapGridField;

        private void BuildSnapDropdown()
        {
            var snapBtn = new Button() { text = "Snap" };
            snapBtn.style.width = 42;
            snapBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            snapBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            snapBtn.style.marginLeft = 8;
            snapBtn.style.borderTopWidth = snapBtn.style.borderBottomWidth =
                snapBtn.style.borderLeftWidth = snapBtn.style.borderRightWidth = 0;
            _toolbar.Add(snapBtn);

            snapBtn.clicked += ToggleSnapPopup;
        }

        private void ToggleSnapPopup()
        {
            if (_snapPopup != null)
            {
                CloseSnapPopup();
                return;
            }

            // Build popup content
            _snapPopup = new VisualElement();
            _snapPopup.style.position = Position.Absolute;
            _snapPopup.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.98f);
            _snapPopup.style.borderTopWidth = _snapPopup.style.borderBottomWidth =
                _snapPopup.style.borderLeftWidth = _snapPopup.style.borderRightWidth = 1;
            _snapPopup.style.borderTopColor = _snapPopup.style.borderBottomColor =
                _snapPopup.style.borderLeftColor = _snapPopup.style.borderRightColor =
                    new Color(0.4f, 0.4f, 0.4f);
            _snapPopup.style.borderTopLeftRadius = _snapPopup.style.borderTopRightRadius =
                _snapPopup.style.borderBottomLeftRadius = _snapPopup.style.borderBottomRightRadius = 4;
            _snapPopup.style.paddingTop = 6;
            _snapPopup.style.paddingBottom = 6;
            _snapPopup.style.paddingLeft = 8;
            _snapPopup.style.paddingRight = 8;
            _snapPopup.style.width = 200;

            var title = new Label("Snap Settings");
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            _snapPopup.Add(title);

            // Playhead snap
            var phLabel = new Label("Playhead Snap (s)");
            phLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            phLabel.style.fontSize = 11;
            _snapPopup.Add(phLabel);

            var phField = new FloatField() { value = _trackArea.SnapPlayheadThreshold };
            phField.style.marginBottom = 6;
            phField.isDelayed = true;
            phField.RegisterValueChangedCallback(e =>
            {
                _trackArea.SnapPlayheadThreshold = Mathf.Max(0f, e.newValue);
            });
            _snapPopup.Add(phField);
            _snapPhField = phField;

            // Grid snap
            var gridLabel = new Label("Grid Snap (s)");
            gridLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            gridLabel.style.fontSize = 11;
            _snapPopup.Add(gridLabel);

            var gridField = new FloatField() { value = _trackArea.SnapGridSize };
            gridField.isDelayed = true;
            gridField.RegisterValueChangedCallback(e =>
            {
                _trackArea.SnapGridSize = Mathf.Max(0f, e.newValue);
            });
            _snapPopup.Add(gridField);
            _snapGridField = gridField;

            // Dismiss layer: full-screen transparent element behind the popup
            _snapDismiss = new VisualElement();
            _snapDismiss.style.position = Position.Absolute;
            _snapDismiss.style.left = _snapDismiss.style.top = 0;
            _snapDismiss.style.right = _snapDismiss.style.bottom = 0;
            _snapDismiss.RegisterCallback<MouseDownEvent>(evt =>
            {
                CloseSnapPopup();
                evt.StopPropagation();
            });

            // Position below the toolbar area
            var toolbarWorld = _toolbar.worldBound;
            var treeRoot = Root.panel.visualTree;
            _snapPopup.style.left = toolbarWorld.xMax - 200;
            _snapPopup.style.top = toolbarWorld.yMax + 2;

            treeRoot.Add(_snapDismiss);
            treeRoot.Add(_snapPopup);
            RegisterThemeOverride(_snapPopup);
        }

        private void CloseSnapPopup()
        {
            // Force-apply any in-progress edits before destroying the fields
            if (_snapPhField != null)
            {
                _snapPhField.Focus();
                _snapPhField.Blur();
                _trackArea.SnapPlayheadThreshold = Mathf.Max(0f, _snapPhField.value);
            }
            if (_snapGridField != null)
            {
                _snapGridField.Focus();
                _snapGridField.Blur();
                _trackArea.SnapGridSize = Mathf.Max(0f, _snapGridField.value);
            }

            _snapPopup?.RemoveFromHierarchy();
            _snapDismiss?.RemoveFromHierarchy();
            _snapPopup = null;
            _snapDismiss = null;
            _snapPhField = null;
            _snapGridField = null;
        }

        // ─── Toolbar Actions ───

        private void OnTogglePlay() => _playback.TogglePlay();
        private void OnStop() => _playback.Reset();
        private void OnStepFrame() => _playback.StepFrame();
        private void OnZoomToFit() => _trackArea.ZoomToFit();

        private void OnSaveStage()
        {
            if (_stage == null) return;

            var catalog = STGCatalog.Load();
            var picker = new FilePickerPopup(
                "Save Stage",
                FilePickerMode.Save,
                catalog.Stages,
                onSelect: entry =>
                {
                    _stage.Id = entry.Id;
                    _stage.Name = entry.Name;
                    var path = Path.Combine(STGCatalog.BasePath, entry.File);
                    try
                    {
                        YamlSerializer.SerializeStageToFile(_stage, path);
                        catalog.AddOrUpdateStage(entry.Id, entry.Name);
                        STGCatalog.Save(catalog);
                        _breadcrumbStage.text = _stage.Name;
                        Debug.Log($"[TimelineEditor] Stage saved (overwrite): {path}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TimelineEditor] Save failed: {e.Message}");
                    }
                },
                onCreateNew: name =>
                {
                    var id = STGCatalog.NameToId(name);
                    id = catalog.EnsureUniqueStageId(id);
                    _stage.Id = id;
                    _stage.Name = name;
                    catalog.AddOrUpdateStage(id, name);
                    var path = catalog.GetStagePath(id);
                    try
                    {
                        YamlSerializer.SerializeStageToFile(_stage, path);
                        STGCatalog.Save(catalog);
                        _breadcrumbStage.text = _stage.Name;
                        Debug.Log($"[TimelineEditor] Stage saved (new): {path}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TimelineEditor] Save failed: {e.Message}");
                    }
                },
                onDelete: entry =>
                {
                    catalog.RemoveStage(entry.Id);
                    STGCatalog.Save(catalog);
                    Debug.Log($"[TimelineEditor] Deleted: {entry.Id}");
                });
            picker.Show(Root);
        }

        private void OnLoadStage()
        {
            var catalog = STGCatalog.Load();
            if (catalog.Stages.Count == 0)
            {
                Debug.LogWarning("[TimelineEditor] No stage files found.");
                return;
            }

            var picker = new FilePickerPopup(
                "Load Stage",
                FilePickerMode.Load,
                catalog.Stages,
                onSelect: entry =>
                {
                    var path = Path.Combine(STGCatalog.BasePath, entry.File);
                    try
                    {
                        var stage = YamlSerializer.DeserializeStageFromFile(path);
                        SetStage(stage);
                        Debug.Log($"[TimelineEditor] Stage loaded: {path}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TimelineEditor] Load failed: {e.Message}");
                    }
                },
                onDelete: entry =>
                {
                    catalog.RemoveStage(entry.Id);
                    STGCatalog.Save(catalog);
                    Debug.Log($"[TimelineEditor] Deleted: {entry.Id}");
                });
            picker.Show(Root);
        }

        // ─── Event Handlers ───

        private void OnSegmentSelected(TimelineSegment segment)
        {
            _breadcrumbSegment.text = segment?.Name ?? "\u2014";
            _trackArea.SetSegment(segment);
            _playback.LoadSegment(segment);
        }

        private void OnEventSelected(SpawnPatternEvent evt)
        {
            _propertyContent.Clear();
            if (_patternEditor != null)
            {
                _patternEditor.Commands.OnStateChanged -= OnPatternEditorChanged;
                _patternEditor.Dispose();
            }
            _patternEditor = null;
            _propStartField = null;
            _propDurField = null;
            _selectedEvent = evt;

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
            startField.isDelayed = true;
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
            _propStartField = startField;

            // Duration
            var durField = new FloatField("Duration") { value = evt.Duration };
            durField.isDelayed = true;
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
            _propDurField = durField;

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
            posX.isDelayed = true;
            posY.isDelayed = true;
            posZ.isDelayed = true;

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

                // When pattern parameters change, refresh the timeline's active previewer
                _patternEditor.Commands.OnStateChanged += OnPatternEditorChanged;

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
            _playPauseBtn.text = playing ? "\u23f8" : "\u25b6";
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

        /// <summary>
        /// Called when the embedded PatternEditorView's CommandStack changes.
        /// Refreshes the timeline's active previewer so edits are visible immediately.
        /// </summary>
        private void OnPatternEditorChanged()
        {
            if (_selectedEvent != null)
                _playback.RefreshEvent(_selectedEvent);
        }

        /// <summary>
        /// Called during drag to update property fields in real-time without rebuilding.
        /// </summary>
        private void OnEventValuesChanged(SpawnPatternEvent evt)
        {
            if (evt == null) return;
            if (_propStartField != null)
                _propStartField.SetValueWithoutNotify(evt.StartTime);
            if (_propDurField != null)
                _propDurField.SetValueWithoutNotify(evt.Duration);
        }

        // ─── Timeline Minimize ───

        /// <summary>
        /// When minimized, hide breadcrumb and main content, keeping only the toolbar visible.
        /// </summary>
        public void SetMinimized(bool minimized)
        {
            _breadcrumbBar.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
            _mainSplit.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ─── Property Panel Toggle ───

        private void TogglePropertyPanel()
        {
            _propertyCollapsed = !_propertyCollapsed;
            if (_propertyCollapsed)
            {
                // Collapse: hide content, shrink to button-only width
                _propertyContent.style.display = DisplayStyle.None;
                _propertyHeaderLabel.style.display = DisplayStyle.None;
                _propertyPanel.style.width = 32;
                _propertyPanel.style.minWidth = 32;
                _propertyPanel.style.maxWidth = 32;
                _toggleBtn.text = "\u25b6"; // ▶
            }
            else
            {
                // Expand: restore full panel
                _propertyContent.style.display = DisplayStyle.Flex;
                _propertyHeaderLabel.style.display = DisplayStyle.Flex;
                _propertyPanel.style.width = new Length(18, LengthUnit.Percent);
                _propertyPanel.style.minWidth = 280;
                _propertyPanel.style.maxWidth = 400;
                _toggleBtn.text = "\u25c0"; // ◀
            }
        }

        // ─── Theme ───

        /// <summary>
        /// Force-apply light text theme to the entire timeline UI tree.
        /// Called externally by PatternSandboxSetup via coroutine as a safety net.
        /// </summary>
        public void ForceApplyTheme()
        {
            ApplyThemeToTree(Root);
            ApplyThemeToTree(_propertyPanel);
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

            // IntegerField
            root.Query<IntegerField>().ForEach(f =>
            {
                f.labelElement.style.color = Lt;
                if (f.ClassListContains("seed-field"))
                {
                    f.style.fontSize = 10;
                    f.style.paddingTop = 0;
                    f.style.paddingBottom = 0;
                    f.style.marginTop = 0;
                    f.style.marginBottom = 0;
                    f.labelElement.style.paddingTop = 0;
                    f.labelElement.style.paddingBottom = 0;
                    f.Query(className: "unity-base-field__input").ForEach(e =>
                    {
                        e.style.color = Lt;
                        e.style.backgroundColor = InputBg;
                        e.style.paddingTop = 0;
                        e.style.paddingBottom = 0;
                        e.style.paddingLeft = 2;
                        e.style.paddingRight = 2;
                        e.style.marginTop = 0;
                        e.style.marginBottom = 0;
                    });
                }
                else
                {
                    f.Query(className: "unity-base-field__input").ForEach(e =>
                    {
                        e.style.color = Lt;
                        e.style.backgroundColor = InputBg;
                    });
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
        /// Uses schedule.Execute (50ms/200ms) to ensure styles are applied
        /// AFTER Unity Runtime Theme.
        /// </summary>
        internal static void RegisterThemeOverride(VisualElement root)
        {
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                root.schedule.Execute(() => ApplyThemeToTree(root)).ExecuteLater(50);
                root.schedule.Execute(() => ApplyThemeToTree(root)).ExecuteLater(200);
            });
        }

        private static void ApplyLightTextTheme(VisualElement root)
        {
            // Immediate sync apply + delayed fallback
            ApplyThemeToTree(root);
            root.schedule.Execute(() => ApplyThemeToTree(root)).ExecuteLater(50);
            root.schedule.Execute(() => ApplyThemeToTree(root)).ExecuteLater(200);
        }
    }
}
