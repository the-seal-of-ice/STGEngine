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
using STGEngine.Editor.UI.Timeline.Layers;
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

        /// <summary>
        /// Fired when entering/exiting spell card editing.
        /// Non-null = entered editing (provides SpellCard data for Boss placeholder).
        /// Null = exited editing.
        /// </summary>
        public Action<SpellCard> OnSpellCardEditingChanged;

        private readonly TimelinePlaybackController _playback;
        private readonly PatternLibrary _library;
        private readonly CommandStack _commandStack = new();
        private STGCatalog _catalog;

        private Stage _stage;
        private PatternPreviewer _singlePreviewer; // For property panel pattern editing

        // Sub-views
        private SegmentListView _segmentList; // Kept but hidden — will be fully removed later
        private readonly TrackAreaView _trackArea;
        private StageLayer _stageLayer;

        // UI elements
        private readonly VisualElement _breadcrumbBar;
        private readonly Label _breadcrumbStage;
        private readonly Label _breadcrumbSegment;
        private readonly Label _breadcrumbSep2;
        private readonly Label _breadcrumbSpellCard;
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
        private TimelineEvent _selectedTimelineEvent;

        // Spell card editing state
        private SpellCard _editingSpellCard;
        private string _editingSpellCardId;
        private TimelineSegment _editingBossFightSegment;

        // ── Recursive navigation stack ──
        private readonly Stack<BreadcrumbEntry> _navigationStack = new();
        private ITimelineLayer _currentLayer;

        public struct BreadcrumbEntry
        {
            public ITimelineLayer Layer;
            public string DisplayName;
        }

        // Stage overview: boss placeholder time ranges for dynamic show/hide
        private struct BossSegmentRange
        {
            public float StartTime;
            public float EndTime;
            public List<PathKeyframe> BossPath;
        }
        private List<BossSegmentRange> _stageOverviewBossRanges;
        private bool _stageOverviewBossVisible;

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
            // Click Stage breadcrumb to return to Stage layer
            _breadcrumbStage.RegisterCallback<ClickEvent>(_ =>
            {
                if (_navigationStack.Count > 0)
                {
                    // Exit spell card editing if active
                    if (_editingSpellCard != null)
                    {
                        _editingSpellCard = null;
                        _editingSpellCardId = null;
                        _editingBossFightSegment = null;
                    }
                    NavigateToDepth(0);
                    // Restore StageLayer in TrackArea
                    if (_stageLayer != null)
                    {
                        _currentLayer = _stageLayer;
                        _trackArea.SetLayer(_stageLayer);
                        // LoadStageOverviewPreview handles OnSpellCardEditingChanged internally
                        LoadStageOverviewPreview();
                        RebuildBreadcrumb();
                    }
                }
            });
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

            // Third-level breadcrumb (SpellCard) — hidden by default
            _breadcrumbSep2 = new Label(">");
            _breadcrumbSep2.style.color = new Color(0.5f, 0.5f, 0.5f);
            _breadcrumbSep2.style.marginLeft = 4;
            _breadcrumbSep2.style.marginRight = 4;
            _breadcrumbSep2.style.display = DisplayStyle.None;
            _breadcrumbBar.Add(_breadcrumbSep2);

            _breadcrumbSpellCard = new Label("");
            _breadcrumbSpellCard.style.color = new Color(0.9f, 0.3f, 0.9f);
            _breadcrumbSpellCard.style.unityFontStyleAndWeight = FontStyle.Bold;
            _breadcrumbSpellCard.style.display = DisplayStyle.None;
            _breadcrumbBar.Add(_breadcrumbSpellCard);

            // Make segment label clickable to navigate back
            _breadcrumbSegment.RegisterCallback<ClickEvent>(_ =>
            {
                if (_editingSpellCard != null)
                    ExitSpellCardEditing();
                else if (_navigationStack.Count > 1)
                    NavigateToDepth(1);
            });

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
            // SegmentListView is now hidden — StageLayer replaces it in TrackArea
            _segmentList.Root.style.display = DisplayStyle.None;
            _mainSplit.Add(_segmentList.Root);

            _trackArea = new TrackAreaView(_commandStack);
            _trackArea.OnEventSelected += OnEventSelected;
            _trackArea.OnEventsChanged += OnStageDataChanged;
            _trackArea.OnEventValuesChanged += OnEventValuesChanged;
            _trackArea.OnSeekRequested += OnSeekRequested;
            _trackArea.OnAddEventRequested += OnAddEventRequested;
            _trackArea.OnAddWaveEventRequested += OnAddWaveEventRequested;
            _trackArea.OnBlockDoubleClicked += OnBlockDoubleClicked;
            _trackArea.OnBlockSelected += OnBlockSelectedGeneric;
            _trackArea.OnBlockReorderRequested += OnBlockReorderRequested;
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

        /// <summary>Set the catalog reference for wave/spell card lookups.</summary>
        public void SetCatalog(STGCatalog catalog)
        {
            _catalog = catalog;
            _stageLayer?.SetCatalog(catalog);
        }

        /// <summary>
        /// Add a SpawnPatternEvent to the current MidStage segment at the playback time.
        /// Called from AssetLibraryPanel via PatternSandboxSetup.
        /// </summary>
        public void AddPatternEventFromLibrary(string patternId)
        {
            if (_stage == null) return;
            // Only works when viewing a MidStage segment
            if (_currentLayer is not MidStageLayer) return;

            float atTime = _playback.CurrentTime;
            CreateEventWithPattern(patternId, atTime);
        }

        /// <summary>
        /// Add a SpawnWaveEvent to the current MidStage segment at the playback time.
        /// Called from AssetLibraryPanel via PatternSandboxSetup.
        /// </summary>
        public void AddWaveEventFromLibrary(string waveId)
        {
            if (_stage == null) return;
            if (_currentLayer is not MidStageLayer) return;

            float atTime = _playback.CurrentTime;
            CreateEventWithWave(waveId, atTime);
        }

        /// <summary>
        /// Add a spell card ID to the current BossFight segment.
        /// Called from AssetLibraryPanel via PatternSandboxSetup.
        /// </summary>
        public void AddSpellCardToCurrentBossFight(string spellCardId)
        {
            // Find the BossFight segment from current layer
            TimelineSegment seg = null;
            if (_currentLayer is BossFightLayer bfl)
                seg = bfl.Segment;
            else if (_editingBossFightSegment != null)
                seg = _editingBossFightSegment;

            if (seg == null || seg.Type != SegmentType.BossFight)
            {
                Debug.LogWarning("[TimelineEditor] Select a BossFight segment first.");
                return;
            }

            seg.SpellCardIds.Add(spellCardId);
            ShowBossFightSpellCards(seg);
            LoadBossFightPreview(seg);
            OnStageDataChanged();
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

            // Create StageLayer and show it in TrackArea
            _navigationStack.Clear();
            _stageLayer = new StageLayer(stage, _catalog, _library, _commandStack);
            _stageLayer.OnStageStructureChanged = () =>
            {
                _trackArea.SetLayer(_stageLayer);
                LoadStageOverviewPreview();
                OnStageDataChanged();
            };
            _stageLayer.OnDeleteSegmentRequested = blk =>
            {
                _stageLayer.DeleteSegment(blk);
            };
            _currentLayer = _stageLayer;
            _trackArea.SetLayer(_stageLayer);
            // Build a combined segment covering all segments so playback shows bullets at Stage level
            LoadStageOverviewPreview();
        }

        /// <summary>
        /// Build a combined temporary segment from all segments in the stage.
        /// MidStage events are offset by segment start time.
        /// BossFight spell card patterns are offset similarly.
        /// This allows bullet preview at the Stage overview level.
        /// </summary>
        private void LoadStageOverviewPreview()
        {
            var tempSegment = new TimelineSegment
            {
                Id = "_stage_overview",
                Name = _stage.Name,
                Type = SegmentType.MidStage,
                Duration = _stageLayer?.TotalDuration ?? 30f
            };

            var bossRanges = new List<BossSegmentRange>();
            float segmentOffset = 0f;

            foreach (var seg in _stage.Segments)
            {
                if (seg.Type == SegmentType.MidStage)
                {
                    float segEnd = segmentOffset + seg.Duration;
                    // Copy events with time offset, clamped to segment boundary
                    foreach (var evt in seg.Events)
                    {
                        if (evt is SpawnPatternEvent sp)
                        {
                            if (sp.StartTime >= seg.Duration) continue; // starts after segment ends
                            var pattern = _library?.Resolve(sp.PatternId);
                            if (pattern == null) continue;

                            float clampedDur = Mathf.Min(sp.Duration, seg.Duration - sp.StartTime);
                            tempSegment.Events.Add(new SpawnPatternEvent
                            {
                                Id = $"_so_{seg.Id}_{sp.Id}",
                                StartTime = segmentOffset + sp.StartTime,
                                Duration = clampedDur,
                                PatternId = sp.PatternId,
                                SpawnPosition = sp.SpawnPosition,
                                ResolvedPattern = pattern
                            });
                        }
                        else if (evt is SpawnWaveEvent sw)
                        {
                            if (sw.StartTime >= seg.Duration) continue;
                            float clampedDur = Mathf.Min(sw.Duration, seg.Duration - sw.StartTime);
                            tempSegment.Events.Add(new SpawnWaveEvent
                            {
                                Id = $"_so_{seg.Id}_{sw.Id}",
                                StartTime = segmentOffset + sw.StartTime,
                                Duration = clampedDur,
                                WaveId = sw.WaveId,
                                SpawnOffset = sw.SpawnOffset
                            });
                        }
                    }
                }
                else if (seg.Type == SegmentType.BossFight && _catalog != null)
                {
                    // Flatten spell card patterns into the overview
                    float scOffset = segmentOffset;
                    var localBossPath = new List<PathKeyframe>();
                    var segContext = OverrideManager.SegmentContext(seg.Id);
                    foreach (var scId in seg.SpellCardIds)
                    {
                        var path = OverrideManager.ResolveSpellCardPath(_catalog, segContext, scId);
                        if (!System.IO.File.Exists(path)) continue;

                        SpellCard sc;
                        try
                        {
                            sc = YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path));
                        }
                        catch { continue; }

                        foreach (var scp in sc.Patterns)
                        {
                            var pattern = _library?.Resolve(scp.PatternId);
                            if (pattern == null) continue;

                            var bossPos = EvaluateBossPath(sc.BossPath, scp.Delay);
                            tempSegment.Events.Add(new SpawnPatternEvent
                            {
                                Id = $"_so_{scId}_{scp.PatternId}_{Guid.NewGuid().ToString("N").Substring(0, 4)}",
                                StartTime = scOffset + scp.Delay,
                                Duration = scp.Duration,
                                PatternId = scp.PatternId,
                                SpawnPosition = bossPos + scp.Offset,
                                ResolvedPattern = pattern
                            });
                        }

                        // Collect boss path keyframes with local time offset
                        float localTime = scOffset - segmentOffset;
                        foreach (var kf in sc.BossPath)
                        {
                            localBossPath.Add(new PathKeyframe
                            {
                                Time = localTime + kf.Time,
                                Position = kf.Position
                            });
                        }

                        scOffset += sc.TimeLimit;
                    }

                    // Record this BossFight segment's time range
                    if (localBossPath.Count > 0)
                    {
                        bossRanges.Add(new BossSegmentRange
                        {
                            StartTime = segmentOffset,
                            EndTime = segmentOffset + seg.Duration,
                            BossPath = localBossPath
                        });
                    }
                }

                segmentOffset += seg.Duration;
            }

            tempSegment.Duration = segmentOffset > 0f ? segmentOffset : 30f;
            _playback.LoadSegment(tempSegment);

            // Store boss ranges for dynamic show/hide in OnPlaybackTimeChanged
            _stageOverviewBossRanges = bossRanges;
            _stageOverviewBossVisible = false;
            // Initially hide boss placeholder at Stage level — it will show dynamically
            OnSpellCardEditingChanged?.Invoke(null);
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

            // Exit spell card editing if active
            if (_editingSpellCard != null)
            {
                _editingSpellCard = null;
                _editingSpellCardId = null;
                _breadcrumbSep2.style.display = DisplayStyle.None;
                _breadcrumbSpellCard.style.display = DisplayStyle.None;
                _editingBossFightSegment = null;
                OnSpellCardEditingChanged?.Invoke(null);
            }

            // Reset navigation stack to root (Stage level)
            _navigationStack.Clear();
            _currentLayer = null;

            if (segment != null && segment.Type == SegmentType.BossFight)
            {
                // Create BossFightLayer for navigation tracking
                var bfLayer = new BossFightLayer(segment, _catalog, _library);
                _currentLayer = bfLayer;
                WireLayerToTrackArea(bfLayer);
                ShowBossFightSpellCards(segment);
                LoadBossFightPreview(segment);
            }
            else if (segment != null)
            {
                // Create MidStageLayer and navigate to it
                var midLayer = new MidStageLayer(segment);
                _currentLayer = midLayer;
                WireLayerToTrackArea(midLayer);
                _trackArea.SetSegment(segment);
                _playback.LoadSegment(segment);
                // Hide boss placeholder when switching to MidStage
                OnSpellCardEditingChanged?.Invoke(null);
            }
            else
            {
                _trackArea.SetSegment(null);
                _playback.LoadSegment(null);
                OnSpellCardEditingChanged?.Invoke(null);
            }
        }

        /// <summary>
        /// Build a combined temporary segment from all spell cards in a BossFight segment.
        /// Spell cards are laid out sequentially: SC1 at t=0, SC2 at t=SC1.TimeLimit, etc.
        /// Also builds a combined BossPath and notifies the placeholder.
        /// </summary>
        private void LoadBossFightPreview(TimelineSegment segment)
        {
            if (_catalog == null) return;

            var tempSegment = new TimelineSegment
            {
                Id = "_bossfight_preview",
                Name = segment.Name,
                Type = SegmentType.MidStage,
                Duration = 0f
            };

            var combinedBossPath = new List<PathKeyframe>();
            float timeOffset = 0f;
            var bfContext = OverrideManager.SegmentContext(segment.Id);

            foreach (var scId in segment.SpellCardIds)
            {
                var path = OverrideManager.ResolveSpellCardPath(_catalog, bfContext, scId);
                if (!System.IO.File.Exists(path)) continue;

                SpellCard sc;
                try
                {
                    sc = YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BossFightPreview] Failed to load '{scId}': {e.Message}");
                    continue;
                }

                // Add patterns with time offset
                foreach (var scp in sc.Patterns)
                {
                    var pattern = _library?.Resolve(scp.PatternId);
                    if (pattern == null) continue;

                    var bossPos = EvaluateBossPath(sc.BossPath, scp.Delay);

                    var evt = new SpawnPatternEvent
                    {
                        Id = $"_bf_{scId}_{Guid.NewGuid().ToString("N").Substring(0, 4)}",
                        StartTime = timeOffset + scp.Delay,
                        Duration = scp.Duration,
                        PatternId = scp.PatternId,
                        SpawnPosition = bossPos + scp.Offset,
                        ResolvedPattern = pattern
                    };
                    tempSegment.Events.Add(evt);
                }

                // Append boss path keyframes with time offset
                foreach (var kf in sc.BossPath)
                {
                    combinedBossPath.Add(new PathKeyframe
                    {
                        Time = timeOffset + kf.Time,
                        Position = kf.Position
                    });
                }

                timeOffset += sc.TimeLimit;
            }

            tempSegment.Duration = timeOffset > 0f ? timeOffset : segment.Duration;

            // Show spell card blocks in TrackArea (not pattern preview blocks)
            if (_currentLayer is BossFightLayer)
                _trackArea.SetLayer(_currentLayer);
            else
                _trackArea.SetSegment(tempSegment);

            // Load combined preview for playback (弹幕 rendering in scene)
            _playback.LoadSegment(tempSegment);

            if (combinedBossPath.Count > 0)
            {
                var combinedSc = new SpellCard
                {
                    BossPath = combinedBossPath,
                    TimeLimit = tempSegment.Duration
                };
                OnSpellCardEditingChanged?.Invoke(combinedSc);
            }
            else
            {
                OnSpellCardEditingChanged?.Invoke(null);
            }
        }

        private void ShowBossFightSpellCards(TimelineSegment segment)
        {
            _propertyContent.Clear();
            _propertyHeaderLabel.text = "Spell Cards";

            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            var infoLabel = new Label($"BossFight: {segment.Name}");
            infoLabel.style.color = new Color(0.9f, 0.3f, 0.9f);
            infoLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            infoLabel.style.marginBottom = 8;
            container.Add(infoLabel);

            // Duration
            var durField = new FloatField("Duration") { value = segment.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Duration",
                    () => segment.Duration,
                    v => segment.Duration = v,
                    Mathf.Max(1f, e.newValue));
                _commandStack.Execute(cmd);
            });
            container.Add(durField);

            // Spell card list
            var scHeader = new Label("Spell Cards:");
            scHeader.style.color = new Color(0.85f, 0.85f, 0.85f);
            scHeader.style.marginTop = 8;
            scHeader.style.marginBottom = 4;
            scHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(scHeader);

            for (int i = 0; i < segment.SpellCardIds.Count; i++)
            {
                int idx = i;
                var scId = segment.SpellCardIds[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 24;
                row.style.marginBottom = 2;

                var indicator = new VisualElement();
                indicator.style.width = 6;
                indicator.style.height = 6;
                indicator.style.borderTopLeftRadius = indicator.style.borderTopRightRadius =
                    indicator.style.borderBottomLeftRadius = indicator.style.borderBottomRightRadius = 3;
                indicator.style.marginRight = 6;
                indicator.style.backgroundColor = new Color(0.9f, 0.3f, 0.9f);
                row.Add(indicator);

                var scLabel = new Label($"{i + 1}. {scId}");
                scLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
                scLabel.style.flexGrow = 1;
                row.Add(scLabel);

                // Edit button
                var editScId = scId;
                var editBtn = new Button(() => EnterSpellCardEditing(segment, editScId))
                { text = "\u270e" };
                editBtn.style.width = 20;
                editBtn.style.height = 18;
                editBtn.style.fontSize = 11;
                editBtn.style.backgroundColor = new Color(0.2f, 0.25f, 0.35f);
                editBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
                editBtn.style.borderTopWidth = editBtn.style.borderBottomWidth =
                    editBtn.style.borderLeftWidth = editBtn.style.borderRightWidth = 0;
                editBtn.style.marginRight = 2;
                row.Add(editBtn);

                var removeBtn = new Button(() =>
                {
                    if (idx < segment.SpellCardIds.Count)
                    {
                        segment.SpellCardIds.RemoveAt(idx);
                        ShowBossFightSpellCards(segment);
                        LoadBossFightPreview(segment);
                        OnStageDataChanged();
                    }
                })
                { text = "\u2715" };
                removeBtn.style.width = 20;
                removeBtn.style.height = 18;
                removeBtn.style.fontSize = 10;
                removeBtn.style.backgroundColor = new Color(0.35f, 0.2f, 0.2f);
                removeBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
                removeBtn.style.borderTopWidth = removeBtn.style.borderBottomWidth =
                    removeBtn.style.borderLeftWidth = removeBtn.style.borderRightWidth = 0;
                row.Add(removeBtn);

                container.Add(row);
            }

            // Add spell card button — opens picker
            var addBtn = new Button(() =>
            {
                ShowSpellCardPicker(segment);
            })
            { text = "+ Add Spell Card" };
            addBtn.style.height = 24;
            addBtn.style.marginTop = 4;
            addBtn.style.backgroundColor = new Color(0.25f, 0.2f, 0.35f);
            addBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            addBtn.style.borderTopWidth = addBtn.style.borderBottomWidth =
                addBtn.style.borderLeftWidth = addBtn.style.borderRightWidth = 0;
            container.Add(addBtn);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void ShowSpellCardPicker(TimelineSegment segment)
        {
            if (_catalog == null)
            {
                Debug.LogWarning("[TimelineEditor] No catalog available for spell card selection.");
                return;
            }

            var picker = new VisualElement();
            picker.style.position = Position.Absolute;
            picker.style.left = Length.Percent(30);
            picker.style.top = Length.Percent(20);
            picker.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.98f);
            picker.style.borderTopWidth = picker.style.borderBottomWidth =
                picker.style.borderLeftWidth = picker.style.borderRightWidth = 1;
            picker.style.borderTopColor = picker.style.borderBottomColor =
                picker.style.borderLeftColor = picker.style.borderRightColor = new Color(0.5f, 0.3f, 0.6f);
            picker.style.paddingTop = picker.style.paddingBottom = 8;
            picker.style.paddingLeft = picker.style.paddingRight = 12;
            picker.style.minWidth = 220;

            var title = new Label("Select Spell Card");
            title.style.color = new Color(0.9f, 0.3f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            picker.Add(title);

            if (_catalog.SpellCards.Count == 0)
            {
                var emptyLabel = new Label("No spell cards in catalog.\nCreate one from the Assets panel first.");
                emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                emptyLabel.style.marginBottom = 8;
                picker.Add(emptyLabel);
            }
            else
            {
                foreach (var entry in _catalog.SpellCards)
                {
                    string label = !string.IsNullOrEmpty(entry.Name)
                        ? $"{entry.Name}  ({entry.Id})"
                        : entry.Id;

                    var scId = entry.Id;
                    var btn = new Button(() =>
                    {
                        picker.RemoveFromHierarchy();
                        segment.SpellCardIds.Add(scId);
                        ShowBossFightSpellCards(segment);
                        LoadBossFightPreview(segment);
                        OnStageDataChanged();
                    })
                    { text = label };
                    btn.style.backgroundColor = new Color(0.25f, 0.2f, 0.3f);
                    btn.style.color = new Color(0.9f, 0.9f, 0.9f);
                    btn.style.marginBottom = 2;
                    picker.Add(btn);
                }
            }

            var cancelBtn = new Button(() => picker.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn.style.marginTop = 4;
            picker.Add(cancelBtn);

            Root.panel.visualTree.Add(picker);
        }

        // ─── Spell Card Editing (Breadcrumb Layer 3) ───

        private void EnterSpellCardEditing(TimelineSegment segment, string spellCardId)
        {
            if (_catalog == null) return;

            // Load spell card from YAML (override-aware)
            var contextId = OverrideManager.SegmentContext(segment.Id);
            var path = OverrideManager.ResolveSpellCardPath(_catalog, contextId, spellCardId);
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[TimelineEditor] SpellCard file not found: {path}");
                return;
            }

            SpellCard sc;
            try
            {
                sc = YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Failed to load spell card '{spellCardId}': {e.Message}");
                return;
            }

            _editingSpellCard = sc;
            _editingSpellCardId = spellCardId;
            _editingBossFightSegment = segment;

            // Push current layer onto navigation stack
            if (_currentLayer != null)
            {
                _navigationStack.Push(new BreadcrumbEntry
                {
                    Layer = _currentLayer,
                    DisplayName = _currentLayer.DisplayName
                });
            }
            _currentLayer = new SpellCardDetailLayer(sc, spellCardId, _library,
                OverrideManager.SpellCardContext(segment.Id, spellCardId));

            // Update breadcrumb to layer 3
            _breadcrumbSep2.style.display = DisplayStyle.Flex;
            _breadcrumbSpellCard.style.display = DisplayStyle.Flex;
            _breadcrumbSpellCard.text = !string.IsNullOrEmpty(sc.Name) ? sc.Name : spellCardId;

            ShowSpellCardEditor(sc, spellCardId);
            OnSpellCardEditingChanged?.Invoke(sc);

            // Build temporary segment from spell card patterns for preview
            LoadSpellCardPreview(sc);
        }

        /// <summary>
        /// Construct a temporary TimelineSegment from SpellCard.Patterns
        /// and load it into the playback controller + track area for preview.
        /// </summary>
        private void LoadSpellCardPreview(SpellCard sc)
        {
            var tempSegment = new TimelineSegment
            {
                Id = $"_spellcard_{sc.Id}",
                Name = sc.Name,
                Type = SegmentType.MidStage, // Treat as MidStage for playback
                Duration = sc.TimeLimit
            };

            foreach (var scp in sc.Patterns)
            {
                var pattern = _library?.Resolve(scp.PatternId);
                if (pattern == null)
                {
                    Debug.LogWarning($"[SpellCardPreview] Pattern '{scp.PatternId}' not found, skipping.");
                    continue;
                }

                // Boss path base position at the pattern's delay time
                var bossPos = EvaluateBossPath(sc.BossPath, scp.Delay);

                var evt = new SpawnPatternEvent
                {
                    Id = $"_sc_evt_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                    StartTime = scp.Delay,
                    Duration = scp.Duration,
                    PatternId = scp.PatternId,
                    SpawnPosition = bossPos + scp.Offset,
                    ResolvedPattern = pattern
                };
                tempSegment.Events.Add(evt);
            }

            _trackArea.SetSegment(tempSegment);
            _playback.LoadSegment(tempSegment);
        }

        /// <summary>
        /// <summary>
        /// Clamp events in a temporary segment so none exceed the segment's Duration.
        /// Events that start after Duration are removed.
        /// Events that overlap the boundary have their Duration truncated.
        /// </summary>
        private static void ClampEventsToSegmentDuration(TimelineSegment segment)
        {
            if (segment == null || segment.Events == null) return;
            float limit = segment.Duration;

            for (int i = segment.Events.Count - 1; i >= 0; i--)
            {
                var evt = segment.Events[i];
                if (evt.StartTime >= limit)
                {
                    segment.Events.RemoveAt(i);
                    continue;
                }
                float endTime = evt.StartTime + evt.Duration;
                if (endTime > limit)
                {
                    evt.Duration = limit - evt.StartTime;
                }
            }
        }

        /// <summary>
        /// Linear interpolation along BossPath keyframes (same logic as BossPlaceholder).
        /// </summary>
        internal static Vector3 EvaluateBossPath(List<PathKeyframe> path, float t)
        {
            if (path == null || path.Count == 0) return new Vector3(0, 6, 0);
            if (path.Count == 1) return path[0].Position;
            if (t <= path[0].Time) return path[0].Position;
            if (t >= path[path.Count - 1].Time) return path[path.Count - 1].Position;

            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                if (t >= a.Time && t <= b.Time)
                {
                    float segLen = b.Time - a.Time;
                    float frac = segLen > 0f ? (t - a.Time) / segLen : 0f;
                    return Vector3.Lerp(a.Position, b.Position, frac);
                }
            }

            return path[path.Count - 1].Position;
        }

        private void ExitSpellCardEditing()
        {
            _editingSpellCard = null;
            _editingSpellCardId = null;

            // Hide breadcrumb layer 3
            _breadcrumbSep2.style.display = DisplayStyle.None;
            _breadcrumbSpellCard.style.display = DisplayStyle.None;

            // Pop navigation stack to restore parent layer
            if (_navigationStack.Count > 0)
            {
                var entry = _navigationStack.Pop();
                _currentLayer = entry.Layer;
            }

            // Return to spell card list and reload BossFight preview
            if (_editingBossFightSegment != null)
            {
                ShowBossFightSpellCards(_editingBossFightSegment);
                LoadBossFightPreview(_editingBossFightSegment);
                // LoadBossFightPreview already fires OnSpellCardEditingChanged with combined SC
            }
            else
            {
                _trackArea.SetSegment(null);
                _playback.LoadSegment(null);
                OnSpellCardEditingChanged?.Invoke(null);
            }

            _editingBossFightSegment = null;
        }

        // ─── Recursive Navigation ───

        /// <summary>
        /// Navigate into a child layer. Pushes current layer onto the stack,
        /// sets the new layer as current, and rebuilds the breadcrumb + track area.
        /// </summary>
        public void NavigateTo(ITimelineLayer layer)
        {
            if (layer == null) return;

            // Clear stage overview boss tracking when leaving Stage level
            _stageOverviewBossRanges = null;
            _stageOverviewBossVisible = false;

            if (_currentLayer != null)
            {
                _navigationStack.Push(new BreadcrumbEntry
                {
                    Layer = _currentLayer,
                    DisplayName = _currentLayer.DisplayName
                });
            }

            _currentLayer = layer;
            WireLayerToTrackArea(layer);
            _trackArea.SetLayer(layer);
            layer.LoadPreview(_playback);
            RebuildBreadcrumb();
        }

        /// <summary>
        /// Navigate back one level. Pops the stack and restores the parent layer.
        /// </summary>
        public void NavigateBack()
        {
            if (_navigationStack.Count == 0) return;

            var entry = _navigationStack.Pop();
            _currentLayer = entry.Layer;
            WireLayerToTrackArea(_currentLayer);
            _trackArea.SetLayer(_currentLayer);
            _currentLayer.LoadPreview(_playback);
            RebuildBreadcrumb();
        }

        /// <summary>
        /// Navigate back to a specific depth in the stack.
        /// depth=0 means go back to the root layer.
        /// </summary>
        public void NavigateToDepth(int depth)
        {
            while (_navigationStack.Count > depth && _navigationStack.Count > 0)
            {
                var entry = _navigationStack.Pop();
                _currentLayer = entry.Layer;
            }

            if (_currentLayer != null)
            {
                WireLayerToTrackArea(_currentLayer);
                _trackArea.SetLayer(_currentLayer);
                _currentLayer.LoadPreview(_playback);
            }
            RebuildBreadcrumb();
        }

        /// <summary>
        /// Wire a MidStageLayer's callbacks to the legacy event handlers.
        /// For other layer types, this is a no-op for now (will be extended in 1e/1f).
        /// </summary>
        private void WireLayerToTrackArea(ITimelineLayer layer)
        {
            if (layer is MidStageLayer midLayer)
            {
                midLayer.Library = _library;
                midLayer.Catalog = _catalog;
                midLayer.OnAddPatternRequested = time => OnAddEventRequested(time);
                midLayer.OnAddWaveRequested = time => OnAddWaveEventRequested(time);
                midLayer.OnDeleteRequested = blk =>
                {
                    _trackArea.SelectBlock(blk);
                    _trackArea.DeleteSelectedEvent();
                };
            }
            else if (layer is BossFightLayer bfLayer)
            {
                bfLayer.OnAddSpellCardRequested = () =>
                {
                    ShowSpellCardPicker(bfLayer.Segment);
                };
                bfLayer.OnDeleteSpellCardRequested = blk =>
                {
                    if (blk is SpellCardBlock scBlk)
                    {
                        var seg = bfLayer.Segment;
                        int idx = seg.SpellCardIds.IndexOf(scBlk.SpellCardId);
                        if (idx >= 0)
                        {
                            var cmd = ListCommand<string>.Remove(
                                seg.SpellCardIds, idx, "Delete Spell Card");
                            _commandStack.Execute(cmd);
                            ShowBossFightSpellCards(seg);
                            LoadBossFightPreview(seg);
                            OnStageDataChanged();
                        }
                    }
                };
                bfLayer.OnOverrideChanged = () =>
                {
                    // Refresh the BossFight view after override revert
                    var seg = bfLayer.Segment;
                    var newBfLayer = new BossFightLayer(seg, _catalog, _library);
                    _currentLayer = newBfLayer;
                    WireLayerToTrackArea(newBfLayer);
                    _trackArea.SetLayer(newBfLayer);
                    LoadBossFightPreview(seg);
                    RebuildBreadcrumb();
                };
                bfLayer.OnSaveAsNewTemplateRequested = (resourceId, resourceType) =>
                {
                    ShowSaveAsNewTemplateDialog(bfLayer.ContextId, resourceId, resourceType);
                };
            }
        }

        /// <summary>
        /// Rebuild the breadcrumb bar to reflect the current navigation stack.
        /// Generates N clickable labels + separators, with the last one being non-clickable.
        /// </summary>
        private void RebuildBreadcrumb()
        {
            // Remove all children except the seed controls (which are after the spacer)
            // Strategy: clear everything, then re-add seed controls
            // But seed controls are complex — safer to just update the breadcrumb labels.

            // For now, update the existing hardcoded labels to match the stack.
            // Full dynamic breadcrumb will replace this once SegmentListView is removed (1f).

            var layers = new List<BreadcrumbEntry>();
            // Stack is LIFO, so we need to reverse to get root-first order
            var stackArray = _navigationStack.ToArray();
            for (int i = stackArray.Length - 1; i >= 0; i--)
                layers.Add(stackArray[i]);

            // Current layer is the last one
            if (_currentLayer != null)
                layers.Add(new BreadcrumbEntry { Layer = _currentLayer, DisplayName = _currentLayer.DisplayName });

            // Update existing breadcrumb labels based on depth
            if (layers.Count >= 1)
                _breadcrumbStage.text = layers[0].DisplayName;

            if (layers.Count >= 2)
            {
                _breadcrumbSegment.text = layers[1].DisplayName;
                _breadcrumbSegment.style.color = layers.Count > 2
                    ? new Color(0.5f, 0.8f, 1f) // clickable color
                    : new Color(0.5f, 0.8f, 1f); // current level
            }
            else
            {
                _breadcrumbSegment.text = "\u2014";
            }

            if (layers.Count >= 3)
            {
                _breadcrumbSep2.style.display = DisplayStyle.Flex;
                _breadcrumbSpellCard.style.display = DisplayStyle.Flex;

                // Show [M] marker if the current SpellCard is an override
                var displayName = layers[2].DisplayName;
                if (layers[2].Layer is SpellCardDetailLayer scLayer &&
                    !string.IsNullOrEmpty(scLayer.ContextId) &&
                    _editingBossFightSegment != null &&
                    OverrideManager.HasOverride(
                        OverrideManager.SegmentContext(_editingBossFightSegment.Id),
                        scLayer.SpellCardId))
                {
                    displayName = $"[M] {displayName}";
                    _breadcrumbSpellCard.style.color = new Color(1f, 0.7f, 0.3f); // Orange for modified
                }
                else
                {
                    _breadcrumbSpellCard.style.color = new Color(0.5f, 0.8f, 1f);
                }
                _breadcrumbSpellCard.text = displayName;
            }
            else
            {
                _breadcrumbSep2.style.display = DisplayStyle.None;
                _breadcrumbSpellCard.style.display = DisplayStyle.None;
            }
        }

        private void SaveCurrentSpellCard()
        {
            if (_editingSpellCard == null || _editingSpellCardId == null || _catalog == null) return;

            try
            {
                var yaml = YamlSerializer.SerializeSpellCard(_editingSpellCard);

                // If editing within a BossFight segment context, save as override
                if (_editingBossFightSegment != null)
                {
                    var contextId = OverrideManager.SegmentContext(_editingBossFightSegment.Id);
                    OverrideManager.SaveOverride(contextId, _editingSpellCardId, yaml);
                }
                else
                {
                    // Direct editing (e.g. from PatternEdit mode) — save to original
                    var path = _catalog.GetSpellCardPath(_editingSpellCardId);
                    System.IO.File.WriteAllText(path, yaml);
                    _catalog.AddOrUpdateSpellCard(_editingSpellCardId, _editingSpellCard.Name);
                    STGCatalog.Save(_catalog);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Failed to save spell card: {e.Message}");
            }

            // Notify Boss placeholder of path changes
            OnSpellCardEditingChanged?.Invoke(_editingSpellCard);

            // Refresh spell card preview (patterns/timing may have changed)
            LoadSpellCardPreview(_editingSpellCard);
        }

        /// <summary>
        /// Show a dialog to save an override as a new template with a user-specified ID.
        /// </summary>
        private void ShowSaveAsNewTemplateDialog(string contextId, string resourceId, string resourceType)
        {
            if (_catalog == null) return;

            var dialog = new VisualElement();
            dialog.style.position = Position.Absolute;
            dialog.style.left = dialog.style.right = dialog.style.top = dialog.style.bottom = 0;
            dialog.style.backgroundColor = new Color(0, 0, 0, 0.5f);
            dialog.style.alignItems = Align.Center;
            dialog.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            panel.style.paddingTop = panel.style.paddingBottom = 12;
            panel.style.paddingLeft = panel.style.paddingRight = 16;
            panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
                panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 6;
            panel.style.width = 300;

            var title = new Label("Save as New Template");
            title.style.fontSize = 14;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            var desc = new Label($"Override: {resourceId} ({resourceType})");
            desc.style.color = new Color(0.7f, 0.7f, 0.7f);
            desc.style.marginBottom = 8;
            panel.Add(desc);

            var idField = new TextField("New ID:");
            idField.value = $"{resourceId}_copy";
            idField.style.marginBottom = 8;
            panel.Add(idField);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var saveBtn = new Button(() =>
            {
                var newId = idField.value?.Trim();
                if (string.IsNullOrEmpty(newId))
                {
                    Debug.LogWarning("[TimelineEditor] New ID cannot be empty.");
                    return;
                }

                var result = OverrideManager.SaveAsNewTemplate(_catalog, contextId, resourceId, newId, resourceType);
                if (result != null)
                {
                    Debug.Log($"[TimelineEditor] Saved as new template: {result}");
                }
                dialog.RemoveFromHierarchy();
            }) { text = "Save" };
            saveBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.3f);
            saveBtn.style.color = new Color(0.9f, 0.9f, 0.9f);

            var cancelBtn = new Button(() => dialog.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn.style.marginLeft = 8;

            btnRow.Add(saveBtn);
            btnRow.Add(cancelBtn);
            panel.Add(btnRow);
            dialog.Add(panel);

            Root.panel.visualTree.Add(dialog);
        }

        private void ShowSpellCardEditor(SpellCard sc, string scId)
        {
            _propertyContent.Clear();
            _propertyHeaderLabel.text = $"SpellCard: {scId}";

            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // ── Back button ──
            var backBtn = new Button(() => ExitSpellCardEditing())
            { text = "\u25c0 Back to Spell Card List" };
            backBtn.style.height = 22;
            backBtn.style.marginBottom = 8;
            backBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
            backBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            backBtn.style.borderTopWidth = backBtn.style.borderBottomWidth =
                backBtn.style.borderLeftWidth = backBtn.style.borderRightWidth = 0;
            container.Add(backBtn);

            // ── Name ──
            var nameField = new TextField("Name") { value = sc.Name };
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(e =>
            {
                sc.Name = e.newValue;
                _breadcrumbSpellCard.text = e.newValue;
                SaveCurrentSpellCard();
            });
            container.Add(nameField);

            // ── Health ──
            var healthField = new FloatField("Health") { value = sc.Health };
            healthField.isDelayed = true;
            healthField.RegisterValueChangedCallback(e =>
            {
                sc.Health = Mathf.Max(1f, e.newValue);
                SaveCurrentSpellCard();
            });
            container.Add(healthField);

            // ── Time Limit ──
            var timeLimitField = new FloatField("Time Limit") { value = sc.TimeLimit };
            timeLimitField.isDelayed = true;
            timeLimitField.RegisterValueChangedCallback(e =>
            {
                sc.TimeLimit = Mathf.Max(1f, e.newValue);
                SaveCurrentSpellCard();
            });
            container.Add(timeLimitField);

            // ── Boss Path ──
            var pathHeader = new Label("Boss Path");
            pathHeader.style.color = new Color(0.85f, 0.85f, 0.85f);
            pathHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            pathHeader.style.marginTop = 10;
            pathHeader.style.marginBottom = 4;
            container.Add(pathHeader);

            BuildPathKeyframeList(container, sc.BossPath, "boss-path");

            // ── Patterns ──
            var patternsHeader = new Label("Patterns");
            patternsHeader.style.color = new Color(0.85f, 0.85f, 0.85f);
            patternsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            patternsHeader.style.marginTop = 10;
            patternsHeader.style.marginBottom = 4;
            container.Add(patternsHeader);

            BuildSpellCardPatternList(container, sc);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildPathKeyframeList(VisualElement parent, List<PathKeyframe> keyframes, string context)
        {
            for (int i = 0; i < keyframes.Count; i++)
            {
                int idx = i;
                var kf = keyframes[i];

                var wrapper = new VisualElement();
                wrapper.style.marginBottom = 3;
                wrapper.style.backgroundColor = new Color(0.16f, 0.16f, 0.2f);
                wrapper.style.borderTopLeftRadius = wrapper.style.borderTopRightRadius =
                    wrapper.style.borderBottomLeftRadius = wrapper.style.borderBottomRightRadius = 3;
                wrapper.style.paddingLeft = 6;
                wrapper.style.paddingRight = 4;
                wrapper.style.paddingTop = 2;
                wrapper.style.paddingBottom = 2;

                // Detail panel (hidden by default)
                var detail = new VisualElement();
                detail.style.display = DisplayStyle.None;
                detail.style.paddingTop = 4;
                detail.style.paddingBottom = 2;

                // Summary row: clickable button showing T + position
                var summaryRow = new VisualElement();
                summaryRow.style.flexDirection = FlexDirection.Row;
                summaryRow.style.alignItems = Align.Center;
                summaryRow.style.height = 22;

                var expandLabel = new Label("\u25b6");
                expandLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                expandLabel.style.fontSize = 10;
                expandLabel.style.width = 14;
                summaryRow.Add(expandLabel);

                var summaryText = new Label($"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})");
                summaryText.style.color = new Color(0.85f, 0.85f, 0.85f);
                summaryText.style.fontSize = 11;
                summaryText.style.flexGrow = 1;
                summaryRow.Add(summaryText);

                var delBtn = new Button(() =>
                {
                    keyframes.RemoveAt(idx);
                    SaveCurrentSpellCard();
                    ShowSpellCardEditor(_editingSpellCard, _editingSpellCardId);
                })
                { text = "\u2715" };
                delBtn.style.width = 18;
                delBtn.style.height = 16;
                delBtn.style.fontSize = 9;
                delBtn.style.backgroundColor = new Color(0.35f, 0.2f, 0.2f);
                delBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
                delBtn.style.borderTopWidth = delBtn.style.borderBottomWidth =
                    delBtn.style.borderLeftWidth = delBtn.style.borderRightWidth = 0;
                summaryRow.Add(delBtn);

                // Toggle expand/collapse on click
                bool expanded = false;
                summaryRow.RegisterCallback<ClickEvent>(evt =>
                {
                    // Ignore if click was on the delete button
                    if (evt.target is Button) return;

                    expanded = !expanded;
                    detail.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    expandLabel.text = expanded ? "\u25bc" : "\u25b6";
                });

                wrapper.Add(summaryRow);

                // ── Detail fields (each on its own line) ──
                var timeField = new FloatField("Time") { value = kf.Time };
                timeField.isDelayed = true;
                timeField.RegisterValueChangedCallback(e =>
                {
                    kf.Time = Mathf.Max(0f, e.newValue);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveCurrentSpellCard();
                });
                detail.Add(timeField);

                var xField = new FloatField("X") { value = kf.Position.x };
                xField.isDelayed = true;
                xField.RegisterValueChangedCallback(e =>
                {
                    kf.Position = new Vector3(e.newValue, kf.Position.y, kf.Position.z);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveCurrentSpellCard();
                });
                detail.Add(xField);

                var yField = new FloatField("Y") { value = kf.Position.y };
                yField.isDelayed = true;
                yField.RegisterValueChangedCallback(e =>
                {
                    kf.Position = new Vector3(kf.Position.x, e.newValue, kf.Position.z);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveCurrentSpellCard();
                });
                detail.Add(yField);

                var zField = new FloatField("Z") { value = kf.Position.z };
                zField.isDelayed = true;
                zField.RegisterValueChangedCallback(e =>
                {
                    kf.Position = new Vector3(kf.Position.x, kf.Position.y, e.newValue);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveCurrentSpellCard();
                });
                detail.Add(zField);

                wrapper.Add(detail);
                parent.Add(wrapper);
            }

            var addKfBtn = new Button(() =>
            {
                float lastTime = keyframes.Count > 0 ? keyframes[keyframes.Count - 1].Time + 5f : 0f;
                keyframes.Add(new PathKeyframe { Time = lastTime, Position = new Vector3(0, 6, 0) });
                SaveCurrentSpellCard();
                ShowSpellCardEditor(_editingSpellCard, _editingSpellCardId);
            })
            { text = "+ Add Keyframe" };
            addKfBtn.style.height = 20;
            addKfBtn.style.marginTop = 2;
            addKfBtn.style.backgroundColor = new Color(0.2f, 0.25f, 0.35f);
            addKfBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            addKfBtn.style.borderTopWidth = addKfBtn.style.borderBottomWidth =
                addKfBtn.style.borderLeftWidth = addKfBtn.style.borderRightWidth = 0;
            parent.Add(addKfBtn);
        }

        private void BuildSpellCardPatternList(VisualElement parent, SpellCard sc)
        {
            for (int i = 0; i < sc.Patterns.Count; i++)
            {
                int idx = i;
                var scp = sc.Patterns[i];

                var block = new VisualElement();
                block.style.backgroundColor = new Color(0.18f, 0.18f, 0.22f);
                block.style.borderTopLeftRadius = block.style.borderTopRightRadius =
                    block.style.borderBottomLeftRadius = block.style.borderBottomRightRadius = 3;
                block.style.paddingTop = 4;
                block.style.paddingBottom = 4;
                block.style.paddingLeft = 6;
                block.style.paddingRight = 6;
                block.style.marginBottom = 4;

                // Header row: pattern ID + delete
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.Center;
                headerRow.style.marginBottom = 2;

                var patLabel = new Label($"Pattern: {scp.PatternId}");
                patLabel.style.color = new Color(0.5f, 0.8f, 1f);
                patLabel.style.flexGrow = 1;
                patLabel.style.fontSize = 11;
                headerRow.Add(patLabel);

                var delBtn = new Button(() =>
                {
                    sc.Patterns.RemoveAt(idx);
                    SaveCurrentSpellCard();
                    ShowSpellCardEditor(_editingSpellCard, _editingSpellCardId);
                })
                { text = "\u2715" };
                delBtn.style.width = 18;
                delBtn.style.height = 16;
                delBtn.style.fontSize = 9;
                delBtn.style.backgroundColor = new Color(0.35f, 0.2f, 0.2f);
                delBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
                delBtn.style.borderTopWidth = delBtn.style.borderBottomWidth =
                    delBtn.style.borderLeftWidth = delBtn.style.borderRightWidth = 0;
                headerRow.Add(delBtn);
                block.Add(headerRow);

                // Delay
                var delayField = new FloatField("Delay") { value = scp.Delay };
                delayField.isDelayed = true;
                delayField.RegisterValueChangedCallback(e =>
                {
                    scp.Delay = Mathf.Max(0f, e.newValue);
                    SaveCurrentSpellCard();
                });
                block.Add(delayField);

                // Duration
                var durField = new FloatField("Duration") { value = scp.Duration };
                durField.isDelayed = true;
                durField.RegisterValueChangedCallback(e =>
                {
                    scp.Duration = Mathf.Max(0.1f, e.newValue);
                    SaveCurrentSpellCard();
                });
                block.Add(durField);

                // Offset
                var offRow = new VisualElement();
                offRow.style.flexDirection = FlexDirection.Row;
                offRow.style.alignItems = Align.Center;

                var offLabel = new Label("Offset");
                offLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                offLabel.style.width = 45;
                offLabel.style.fontSize = 11;
                offRow.Add(offLabel);

                var ox = new FloatField() { value = scp.Offset.x };
                ox.isDelayed = true;
                ox.style.width = 50;
                var oy = new FloatField() { value = scp.Offset.y };
                oy.isDelayed = true;
                oy.style.width = 50;
                var oz = new FloatField() { value = scp.Offset.z };
                oz.isDelayed = true;
                oz.style.width = 50;

                Action updateOff = () =>
                {
                    scp.Offset = new Vector3(ox.value, oy.value, oz.value);
                    SaveCurrentSpellCard();
                };
                ox.RegisterValueChangedCallback(_ => updateOff());
                oy.RegisterValueChangedCallback(_ => updateOff());
                oz.RegisterValueChangedCallback(_ => updateOff());

                offRow.Add(ox);
                offRow.Add(oy);
                offRow.Add(oz);
                block.Add(offRow);

                parent.Add(block);
            }

            // Add pattern button — pick from catalog
            var addPatBtn = new Button(() => ShowPatternPickerForSpellCard(sc))
            { text = "+ Add Pattern" };
            addPatBtn.style.height = 20;
            addPatBtn.style.marginTop = 2;
            addPatBtn.style.backgroundColor = new Color(0.2f, 0.3f, 0.2f);
            addPatBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            addPatBtn.style.borderTopWidth = addPatBtn.style.borderBottomWidth =
                addPatBtn.style.borderLeftWidth = addPatBtn.style.borderRightWidth = 0;
            parent.Add(addPatBtn);
        }

        private void ShowPatternPickerForSpellCard(SpellCard sc)
        {
            if (_catalog == null) return;

            var picker = new VisualElement();
            picker.style.position = Position.Absolute;
            picker.style.left = Length.Percent(30);
            picker.style.top = Length.Percent(20);
            picker.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.98f);
            picker.style.borderTopWidth = picker.style.borderBottomWidth =
                picker.style.borderLeftWidth = picker.style.borderRightWidth = 1;
            picker.style.borderTopColor = picker.style.borderBottomColor =
                picker.style.borderLeftColor = picker.style.borderRightColor = new Color(0.3f, 0.5f, 0.8f);
            picker.style.paddingTop = picker.style.paddingBottom = 8;
            picker.style.paddingLeft = picker.style.paddingRight = 12;
            picker.style.minWidth = 220;

            var title = new Label("Add Pattern to Spell Card");
            title.style.color = new Color(0.5f, 0.8f, 1f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            picker.Add(title);

            foreach (var entry in _catalog.Patterns)
            {
                string label = !string.IsNullOrEmpty(entry.Name)
                    ? $"{entry.Name}  ({entry.Id})"
                    : entry.Id;

                var pid = entry.Id;
                var btn = new Button(() =>
                {
                    picker.RemoveFromHierarchy();
                    sc.Patterns.Add(new SpellCardPattern
                    {
                        PatternId = pid,
                        Delay = 0f,
                        Duration = 5f,
                        Offset = Vector3.zero
                    });
                    SaveCurrentSpellCard();
                    ShowSpellCardEditor(_editingSpellCard, _editingSpellCardId);
                })
                { text = label };
                btn.style.backgroundColor = new Color(0.2f, 0.25f, 0.3f);
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

        /// <summary>
        /// Generic block selection handler. Routes to the appropriate existing
        /// property editor based on the current layer type and selected block.
        /// MidStage is handled by the legacy OnEventSelected path.
        /// </summary>
        private void OnBlockSelectedGeneric(ITimelineBlock block)
        {
            // MidStage uses legacy OnEventSelected path
            if (_currentLayer is MidStageLayer) return;
            if (_currentLayer == null) return;

            // Clean up pattern editor if active
            if (_patternEditor != null)
            {
                _patternEditor.Commands.OnStateChanged -= OnPatternEditorChanged;
                _patternEditor.Dispose();
            }
            _patternEditor = null;
            _propStartField = null;
            _propDurField = null;
            _selectedEvent = null;
            _selectedTimelineEvent = null;

            if (_currentLayer is StageLayer)
            {
                // Stage level: show segment properties
                if (block?.DataSource is TimelineSegment segment)
                {
                    _propertyContent.Clear();
                    BuildSegmentProperties(segment);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                }
            }
            else if (_currentLayer is BossFightLayer bfLayer)
            {
                // BossFight level: selecting a SpellCard block shows its editable properties
                if (block is SpellCardBlock scBlock)
                {
                    var sc = scBlock.DataSource as SpellCard;
                    if (sc != null)
                    {
                        _propertyContent.Clear();
                        BuildSpellCardBlockProperties(sc, scBlock.SpellCardId, bfLayer);
                    }
                }
                else
                {
                    // TransitionBlock or null — show layer summary
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, block);
                }
            }
            else if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                // SpellCard detail level: selecting a pattern block shows its editable properties
                if (block?.DataSource is SpellCardPattern scp)
                {
                    _propertyContent.Clear();
                    BuildSpellCardPatternProperties(scp, scLayer);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                }
            }
            else if (_currentLayer is WaveLayer waveLayer)
            {
                // Wave level: selecting an enemy block shows its editable properties
                if (block?.DataSource is EnemyInstance ei)
                {
                    _propertyContent.Clear();
                    BuildEnemyInstanceProperties(ei, waveLayer);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                }
            }
            else
            {
                _propertyContent.Clear();
                _currentLayer.BuildPropertiesPanel(_propertyContent, block);
            }
        }

        // ─── Per-Layer Editable Properties ───

        private void BuildSegmentProperties(TimelineSegment segment)
        {
            _propertyHeaderLabel.text = $"Segment: {segment.Name}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Name
            var nameField = new TextField("Name") { value = segment.Name };
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<string>(
                    "Change Segment Name",
                    () => segment.Name, v => segment.Name = v, e.newValue);
                _commandStack.Execute(cmd);
            });
            container.Add(nameField);

            // Duration
            var durField = new FloatField("Duration") { value = segment.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Segment Duration",
                    () => segment.Duration, v => segment.Duration = v,
                    Mathf.Max(1f, e.newValue));
                _commandStack.Execute(cmd);
            });
            container.Add(durField);

            // Type (read-only)
            var typeLabel = new Label($"Type: {(segment.Type == SegmentType.BossFight ? "BossFight" : "MidStage")}");
            typeLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            typeLabel.style.marginTop = 4;
            container.Add(typeLabel);

            // Content summary
            string content = segment.Type == SegmentType.BossFight
                ? $"Spell Cards: {segment.SpellCardIds.Count}"
                : $"Events: {segment.Events.Count}";
            var contentLabel = new Label(content);
            contentLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            container.Add(contentLabel);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildSpellCardBlockProperties(SpellCard sc, string scId, BossFightLayer bfLayer)
        {
            _propertyHeaderLabel.text = $"SpellCard: {scId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Name
            var nameField = new TextField("Name") { value = sc.Name ?? "" };
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(e => { sc.Name = e.newValue; });
            container.Add(nameField);

            // TimeLimit
            var tlField = new FloatField("Time Limit") { value = sc.TimeLimit };
            tlField.isDelayed = true;
            tlField.RegisterValueChangedCallback(e =>
            {
                sc.TimeLimit = Mathf.Max(1f, e.newValue);
                _trackArea.RefreshBlockPositions();
            });
            container.Add(tlField);

            // Health
            var hpField = new FloatField("Health") { value = sc.Health };
            hpField.isDelayed = true;
            hpField.RegisterValueChangedCallback(e => { sc.Health = Mathf.Max(1f, e.newValue); });
            container.Add(hpField);

            // TransitionDuration
            var transField = new FloatField("Transition Duration") { value = sc.TransitionDuration };
            transField.isDelayed = true;
            transField.RegisterValueChangedCallback(e =>
            {
                sc.TransitionDuration = Mathf.Max(0.1f, e.newValue);
                _trackArea.RefreshBlockPositions();
            });
            container.Add(transField);

            // DesignEstimate
            var deField = new FloatField("Design Estimate") { value = sc.DesignEstimate };
            deField.isDelayed = true;
            deField.RegisterValueChangedCallback(e =>
            {
                sc.DesignEstimate = e.newValue;
                _trackArea.RefreshBlockPositions();
            });
            container.Add(deField);

            // Patterns count (read-only)
            var patLabel = new Label($"Patterns: {sc.Patterns.Count}");
            patLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            patLabel.style.marginTop = 4;
            container.Add(patLabel);

            // Edit button → enter SpellCard detail
            var editBtn = new Button(() =>
            {
                EnterSpellCardEditing(bfLayer.Segment, scId);
            }) { text = "Edit SpellCard Details ▶" };
            editBtn.style.marginTop = 8;
            editBtn.style.backgroundColor = new Color(0.25f, 0.3f, 0.45f);
            editBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            container.Add(editBtn);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildSpellCardPatternProperties(SpellCardPattern scp, SpellCardDetailLayer scLayer)
        {
            _propertyHeaderLabel.text = $"Pattern: {scp.PatternId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Pattern ID (read-only)
            var idLabel = new Label($"Pattern: {scp.PatternId}");
            idLabel.style.color = new Color(0.7f, 0.85f, 1f);
            idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLabel.style.marginBottom = 4;
            container.Add(idLabel);

            // Delay
            var delayField = new FloatField("Delay") { value = scp.Delay };
            delayField.isDelayed = true;
            delayField.RegisterValueChangedCallback(e =>
            {
                scp.Delay = Mathf.Max(0f, e.newValue);
                _trackArea.RefreshBlockPositions();
            });
            container.Add(delayField);

            // Duration
            var durField = new FloatField("Duration") { value = scp.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                scp.Duration = Mathf.Max(0.1f, e.newValue);
                _trackArea.RefreshBlockPositions();
            });
            container.Add(durField);

            // Offset X/Y/Z
            var ox = new FloatField("Offset X") { value = scp.Offset.x };
            ox.isDelayed = true;
            ox.RegisterValueChangedCallback(e => { scp.Offset = new Vector3(e.newValue, scp.Offset.y, scp.Offset.z); });
            container.Add(ox);

            var oy = new FloatField("Offset Y") { value = scp.Offset.y };
            oy.isDelayed = true;
            oy.RegisterValueChangedCallback(e => { scp.Offset = new Vector3(scp.Offset.x, e.newValue, scp.Offset.z); });
            container.Add(oy);

            var oz = new FloatField("Offset Z") { value = scp.Offset.z };
            oz.isDelayed = true;
            oz.RegisterValueChangedCallback(e => { scp.Offset = new Vector3(scp.Offset.x, scp.Offset.y, e.newValue); });
            container.Add(oz);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildEnemyInstanceProperties(EnemyInstance ei, WaveLayer waveLayer)
        {
            _propertyHeaderLabel.text = $"Enemy: {ei.EnemyTypeId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // EnemyType ID (read-only)
            var idLabel = new Label($"Type: {ei.EnemyTypeId}");
            idLabel.style.color = new Color(1f, 0.7f, 0.5f);
            idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLabel.style.marginBottom = 4;
            container.Add(idLabel);

            // SpawnDelay
            var delayField = new FloatField("Spawn Delay") { value = ei.SpawnDelay };
            delayField.isDelayed = true;
            delayField.RegisterValueChangedCallback(e =>
            {
                ei.SpawnDelay = Mathf.Max(0f, e.newValue);
                _trackArea.RefreshBlockPositions();
            });
            container.Add(delayField);

            // Path points count
            var pathLabel = new Label($"Path points: {ei.Path?.Count ?? 0}");
            pathLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            pathLabel.style.marginTop = 4;
            container.Add(pathLabel);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        /// <summary>
        /// Handle block reorder in sequential layers.
        /// Receives the dragged block and the time position where it was dropped.
        /// Figures out the target slot from the layer's data model.
        /// </summary>
        private void OnBlockReorderRequested(ITimelineBlock draggedBlock, float dropTime)
        {
            if (_currentLayer is BossFightLayer bfLayer)
            {
                // Only SpellCardBlocks can be reordered
                if (draggedBlock is not SpellCardBlock scBlock) return;

                var ids = bfLayer.Segment.SpellCardIds;
                int fromIdx = ids.IndexOf(scBlock.SpellCardId);
                if (fromIdx < 0) return;

                int scCount = bfLayer.LoadedSpellCards.Count;

                // Find the insertion slot index (0..scCount).
                // Slot 0 = before first SC, slot 1 = between SC0 and SC1, etc.
                // We compare dropTime against each SC's midpoint in the visual layout.
                int insertSlot = scCount; // default: after last
                float accum = 0f;
                for (int i = 0; i < scCount; i++)
                {
                    float scDur = bfLayer.LoadedSpellCards[i].TimeLimit;
                    float mid = accum + scDur * 0.5f;
                    if (dropTime < mid)
                    {
                        insertSlot = i;
                        break;
                    }
                    accum += scDur;
                    if (i < scCount - 1)
                        accum += bfLayer.LoadedSpellCards[i].TransitionDuration;
                }

                // Convert insertSlot to the actual target index after removal.
                // If dragging forward (fromIdx < insertSlot), removal shifts slots down by 1.
                int targetIdx = insertSlot > fromIdx ? insertSlot - 1 : insertSlot;
                if (targetIdx == fromIdx) return;

                // Execute
                var id = ids[fromIdx];
                ids.RemoveAt(fromIdx);
                targetIdx = Mathf.Clamp(targetIdx, 0, ids.Count);
                ids.Insert(targetIdx, id);

                // Rebuild
                bfLayer.InvalidateBlocks();
                WireLayerToTrackArea(bfLayer);
                _trackArea.RebuildBlocks();
                ShowBossFightSpellCards(bfLayer.Segment);
                LoadBossFightPreview(bfLayer.Segment);
            }
            else if (_currentLayer is StageLayer stageLayer)
            {
                if (draggedBlock is not SegmentBlock segBlock) return;
                var segments = stageLayer.Stage.Segments;
                var seg = segBlock.DataSource as TimelineSegment;
                if (seg == null) return;

                int fromIdx = segments.IndexOf(seg);
                if (fromIdx < 0) return;

                // Find insertion slot (0..count)
                int insertSlot = segments.Count;
                float accum = 0f;
                for (int i = 0; i < segments.Count; i++)
                {
                    float mid = accum + segments[i].Duration * 0.5f;
                    if (dropTime < mid) { insertSlot = i; break; }
                    accum += segments[i].Duration;
                }

                int targetIdx = insertSlot > fromIdx ? insertSlot - 1 : insertSlot;
                if (targetIdx == fromIdx) return;

                segments.RemoveAt(fromIdx);
                targetIdx = Mathf.Clamp(targetIdx, 0, segments.Count);
                segments.Insert(targetIdx, seg);

                stageLayer.InvalidateBlocks();
                _trackArea.RebuildBlocks();
            }
        }

        private void OnEventSelected(TimelineEvent evt)
        {
            // Non-MidStage layers are handled by OnBlockSelectedGeneric — skip legacy path
            if (_currentLayer != null && _currentLayer is not MidStageLayer) return;

            _propertyContent.Clear();
            if (_patternEditor != null)
            {
                _patternEditor.Commands.OnStateChanged -= OnPatternEditorChanged;
                _patternEditor.Dispose();
            }
            _patternEditor = null;
            _propStartField = null;
            _propDurField = null;
            _selectedEvent = evt as SpawnPatternEvent;
            _selectedTimelineEvent = evt;

            if (evt == null)
            {
                _propertyHeaderLabel.text = "Properties";
                return;
            }

            if (evt is SpawnPatternEvent spEvt)
            {
                ShowSpawnPatternProperties(spEvt);
            }
            else if (evt is SpawnWaveEvent swEvt)
            {
                ShowSpawnWaveProperties(swEvt);
            }
        }

        private void ShowSpawnWaveProperties(SpawnWaveEvent evt)
        {
            _propertyHeaderLabel.text = $"Wave: {evt.WaveId}";

            var props = new VisualElement();
            props.style.paddingTop = 4;
            props.style.paddingLeft = 8;
            props.style.paddingRight = 8;

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
            props.Add(startField);
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
            props.Add(durField);
            _propDurField = durField;

            // Wave ID
            var waveLabel = new Label($"Wave: {evt.WaveId}");
            waveLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            waveLabel.style.marginTop = 4;
            props.Add(waveLabel);

            // Spawn Offset
            var offLabel = new Label("Spawn Offset");
            offLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            offLabel.style.marginTop = 8;
            props.Add(offLabel);

            var offX = new FloatField("X") { value = evt.SpawnOffset.x };
            var offY = new FloatField("Y") { value = evt.SpawnOffset.y };
            var offZ = new FloatField("Z") { value = evt.SpawnOffset.z };
            offX.isDelayed = true;
            offY.isDelayed = true;
            offZ.isDelayed = true;

            Action updateOff = () =>
            {
                var newOff = new Vector3(offX.value, offY.value, offZ.value);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change Spawn Offset",
                    () => evt.SpawnOffset,
                    v => evt.SpawnOffset = v,
                    newOff);
                _commandStack.Execute(cmd);
            };

            offX.RegisterValueChangedCallback(e => updateOff());
            offY.RegisterValueChangedCallback(e => updateOff());
            offZ.RegisterValueChangedCallback(e => updateOff());

            props.Add(offX);
            props.Add(offY);
            props.Add(offZ);

            _propertyContent.Add(props);
            ApplyLightTextTheme(props);
        }

        private void ShowSpawnPatternProperties(SpawnPatternEvent evt)
        {
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

            // Stage overview: dynamically show/hide Boss placeholder based on playhead position
            if (_currentLayer is StageLayer && _stageOverviewBossRanges != null)
            {
                BossSegmentRange? activeRange = null;
                foreach (var range in _stageOverviewBossRanges)
                {
                    if (time >= range.StartTime && time < range.EndTime)
                    {
                        activeRange = range;
                        break;
                    }
                }

                if (activeRange.HasValue)
                {
                    if (!_stageOverviewBossVisible)
                    {
                        var sc = new SpellCard
                        {
                            BossPath = activeRange.Value.BossPath,
                            TimeLimit = activeRange.Value.EndTime - activeRange.Value.StartTime
                        };
                        OnSpellCardEditingChanged?.Invoke(sc);
                        _stageOverviewBossVisible = true;
                    }
                }
                else
                {
                    if (_stageOverviewBossVisible)
                    {
                        OnSpellCardEditingChanged?.Invoke(null);
                        _stageOverviewBossVisible = false;
                    }
                }
            }
        }

        private void OnPlayStateChanged(bool playing)
        {
            _playPauseBtn.text = playing ? "\u23f8" : "\u25b6";
        }

        private void OnSeekRequested(float time)
        {
            _playback.Seek(time);
        }

        private void OnBlockDoubleClicked(ITimelineBlock block)
        {
            if (_currentLayer == null || block == null) return;
            if (!_currentLayer.CanDoubleClickEnter(block)) return;

            var childLayer = _currentLayer.CreateChildLayer(block);
            if (childLayer == null) return;

            // Special handling: if entering a segment from StageLayer, use existing navigation
            if (block.DataSource is TimelineSegment segment)
            {
                // Clear stage overview boss tracking
                _stageOverviewBossRanges = null;
                _stageOverviewBossVisible = false;

                // Push StageLayer onto stack
                _navigationStack.Push(new BreadcrumbEntry
                {
                    Layer = _currentLayer,
                    DisplayName = _currentLayer.DisplayName
                });

                // Reuse existing OnSegmentSelected logic for MidStage/BossFight handling
                _currentLayer = childLayer;

                if (segment.Type == SegmentType.BossFight)
                {
                    var bfLayer = new BossFightLayer(segment, _catalog, _library);
                    _currentLayer = bfLayer;
                    WireLayerToTrackArea(bfLayer);
                    ShowBossFightSpellCards(segment);
                    LoadBossFightPreview(segment);
                }
                else
                {
                    var midLayer = childLayer as MidStageLayer;
                    if (midLayer != null)
                        WireLayerToTrackArea(midLayer);
                    _trackArea.SetSegment(segment);
                    _playback.LoadSegment(segment);
                    OnSpellCardEditingChanged?.Invoke(null);
                }

                // Update breadcrumb
                _breadcrumbSegment.text = segment.Name;
                _breadcrumbStage.text = _stage.Name;
                RebuildBreadcrumb();
                return;
            }

            // Generic double-click: navigate into child layer
            NavigateTo(childLayer);

            // SpellCardDetailLayer: show Boss placeholder with this spell card's path
            if (childLayer is SpellCardDetailLayer scLayer)
            {
                var sc = scLayer.SpellCard;
                if (sc != null && sc.BossPath != null && sc.BossPath.Count > 0)
                    OnSpellCardEditingChanged?.Invoke(sc);
                else
                    OnSpellCardEditingChanged?.Invoke(null);
            }

            // TODO: PatternLayer preview — needs coordination between _singlePreviewer
            // and TimelinePlaybackController. Will be implemented later.
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

        private void OnAddWaveEventRequested(float atTime)
        {
            if (_stage == null || _catalog == null) return;

            var waveIds = new List<string>();
            foreach (var entry in _catalog.Waves)
                waveIds.Add(entry.Id);

            if (waveIds.Count == 0)
            {
                Debug.LogWarning("[TimelineEditor] No waves available in catalog.");
                return;
            }

            ShowWavePicker(waveIds, atTime);
        }

        private void ShowWavePicker(List<string> waveIds, float atTime)
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

            var title = new Label("Select Wave");
            title.style.color = new Color(0.3f, 0.9f, 0.4f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            picker.Add(title);

            foreach (var wid in waveIds)
            {
                // Show name from catalog if available
                var entry = _catalog.FindWave(wid);
                string label = entry != null && !string.IsNullOrEmpty(entry.Name)
                    ? $"{entry.Name}  ({wid})"
                    : wid;

                var btn = new Button(() =>
                {
                    picker.RemoveFromHierarchy();
                    CreateEventWithWave(wid, atTime);
                })
                { text = label };
                btn.style.backgroundColor = new Color(0.2f, 0.3f, 0.2f);
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

        private void CreateEventWithWave(string waveId, float atTime)
        {
            // Try to load wave to get duration
            float duration = 10f;
            if (_catalog != null)
            {
                var wavePath = _catalog.GetWavePath(waveId);
                if (System.IO.File.Exists(wavePath))
                {
                    try
                    {
                        var wave = YamlSerializer.DeserializeWave(
                            System.IO.File.ReadAllText(wavePath));
                        duration = wave.Duration;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[TimelineEditor] Failed to load wave '{waveId}': {e.Message}");
                    }
                }
            }

            var evt = new SpawnWaveEvent
            {
                Id = $"wevt_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                StartTime = atTime,
                Duration = duration,
                WaveId = waveId,
                SpawnOffset = Vector3.zero
            };

            _trackArea.AddEvent(evt);
        }

        private void OnStageDataChanged()
        {
            // Structural change: force the current layer to rebuild its block list from data
            InvalidateCurrentLayerBlocks();
            _trackArea.RebuildBlocks();
        }

        /// <summary>
        /// Tell the current layer to rebuild its internal block list.
        /// Called before RebuildBlocks() when the underlying data structure has changed
        /// (add/remove events, spell cards, etc.).
        /// </summary>
        private void InvalidateCurrentLayerBlocks()
        {
            if (_currentLayer is BossFightLayer bf) bf.InvalidateBlocks();
            else if (_currentLayer is MidStageLayer ml) ml.InvalidateBlocks();
            else if (_currentLayer is SpellCardDetailLayer sc) sc.InvalidateBlocks();
            else if (_currentLayer is StageLayer sl) sl.InvalidateBlocks();
            else if (_currentLayer is WaveLayer wl) wl.InvalidateBlocks();
        }

        private void OnCommandStateChanged()
        {
            _trackArea.RefreshBlockPositions();
        }

        /// <summary>
        /// Called when the embedded PatternEditorView's CommandStack changes.
        /// Refreshes the timeline's active previewer so edits are visible immediately.
        /// </summary>
        private void OnPatternEditorChanged()
        {
            if (_selectedEvent != null)
                _playback.RefreshEvent(_selectedEvent);
            _trackArea.InvalidateThumbnails();
        }

        /// <summary>
        /// Called during drag to update property fields in real-time without rebuilding.
        /// </summary>
        private void OnEventValuesChanged(TimelineEvent evt)
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
