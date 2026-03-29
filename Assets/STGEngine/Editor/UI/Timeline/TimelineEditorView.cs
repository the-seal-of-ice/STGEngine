using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Core.Serialization;
using STGEngine.Editor.Commands;
using STGEngine.Editor.UI;
using STGEngine.Editor.UI.AssetLibrary;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Editor.UI.Timeline.Layers;
using STGEngine.Runtime;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline
{
    /// <summary>
    /// Data for spawning enemy placeholders in the 3D viewport.
    /// Each entry represents one Wave with a time offset (for MidStage/Stage overview).
    /// </summary>
    public class WavePlaceholderData
    {
        public Wave Wave;
        /// <summary>Time offset within the parent segment/stage (seconds).</summary>
        public float TimeOffset;
        /// <summary>World-space offset applied to all enemy positions.</summary>
        public Vector3 SpawnOffset;
        }

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

        /// <summary>
        /// Fired when entering/exiting wave editing or navigating to a layer with wave data.
        /// Non-null list = show enemy placeholders for these waves.
        /// Null = hide all enemy placeholders.
        /// </summary>
        public Action<List<WavePlaceholderData>> OnWaveEditingChanged;

        /// <summary>Fired when the current layer changes (navigation). Used to refresh asset library button states.</summary>
        public Action OnLayerChanged;

        /// <summary>Fired when the Player button on the toolbar is clicked.</summary>
        public Action OnPlayerModeRequested;

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
        private readonly Label _breadcrumbSep3;
        private readonly Label _breadcrumbDetail;
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
        /// <summary>Per-instance override context for the SpellCard being edited (segmentId/sc_{index}).</summary>
        private string _editingSpellCardInstanceContext;

        // ── Recursive navigation stack ──
        private readonly Stack<BreadcrumbEntry> _navigationStack = new();
        private ITimelineLayer _currentLayerBacking;
        private ITimelineLayer _currentLayer
        {
            get => _currentLayerBacking;
            set
            {
                _currentLayerBacking = value;
                OnLayerChanged?.Invoke();
            }
        }

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

        // ── Clipboard ──
        private (Type blockType, object yamlData)? _clipboard;

        // ── Undo/Redo toolbar buttons ──
        private Button _undoBtn;
        private Button _redoBtn;

        // ── Command history panel ──
        private VisualElement _historyPanel;
        private ScrollView _historyScroll;
        private bool _historyVisible;

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
                        _editingSpellCardInstanceContext = null;
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
                        NotifyWavePlaceholders();
                    }
                }
            });
            _breadcrumbBar.Add(_breadcrumbStage);

            var sep = new Label(">");
            sep.style.color = new Color(0.5f, 0.5f, 0.5f);
            sep.style.marginLeft = 4;
            sep.style.marginRight = 4;
            sep.AddToClassList("breadcrumb-static-sep");
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

            // Fourth-level breadcrumb (Pattern detail) — hidden by default
            _breadcrumbSep3 = new Label(">");
            _breadcrumbSep3.style.color = new Color(0.5f, 0.5f, 0.5f);
            _breadcrumbSep3.style.marginLeft = 4;
            _breadcrumbSep3.style.marginRight = 4;
            _breadcrumbSep3.style.display = DisplayStyle.None;
            _breadcrumbBar.Add(_breadcrumbSep3);

            _breadcrumbDetail = new Label("");
            _breadcrumbDetail.style.color = new Color(0.5f, 1f, 0.7f);
            _breadcrumbDetail.style.unityFontStyleAndWeight = FontStyle.Bold;
            _breadcrumbDetail.style.display = DisplayStyle.None;
            _breadcrumbBar.Add(_breadcrumbDetail);

            // Make segment label clickable to navigate back
            _breadcrumbSegment.RegisterCallback<ClickEvent>(_ =>
            {
                if (_editingSpellCard != null)
                    ExitSpellCardEditing();
                else if (_navigationStack.Count > 1)
                    NavigateToDepth(1);
            });

            // Make SpellCard label clickable to navigate back to SpellCard layer
            _breadcrumbSpellCard.RegisterCallback<ClickEvent>(_ =>
            {
                if (_navigationStack.Count > 2)
                    NavigateToDepth(2);
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

            // ── Keyboard shortcuts ──
            Root.focusable = true;
            Root.RegisterCallback<AttachToPanelEvent>(_ => Root.Focus());
            Root.RegisterCallback<KeyDownEvent>(OnKeyDown);

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

            // Refresh preview now that catalog is available (wave expansion needs it)
            if (_currentLayer != null)
                LoadPreviewForLayer(_currentLayer);
        }

        /// <summary>
        /// Add a SpawnPatternEvent to the current MidStage segment at the playback time.
        /// Called from AssetLibraryPanel via PatternSandboxSetup.
        /// </summary>
        public void AddPatternEventFromLibrary(string patternId)
        {
            if (_stage == null) return;

            // Patterns can be added to MidStageLayer or SpellCardDetailLayer
            if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                // Delegate to the SpellCardDetail pattern picker flow
                var scp = new SpellCardPattern
                {
                    PatternId = patternId,
                    Delay = 0f,
                    Duration = 5f,
                    Offset = Vector3.zero
                };
                float layerDur = scLayer.TotalDuration;
                if (scp.Delay + scp.Duration > layerDur && layerDur > 0)
                {
                    ShowDurationOverflowDialog(scp.Duration, layerDur, scp.Delay, trimmedDur =>
                    {
                        scp.Duration = trimmedDur;
                        var cmd = ListCommand<SpellCardPattern>.Add(
                            scLayer.SpellCard.Patterns, scp, -1, "Add Pattern to SpellCard");
                        _commandStack.Execute(cmd);
                        scLayer.InvalidateBlocks();
                        OnStageDataChanged();
                        SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
                    });
                    return;
                }
                var cmd2 = ListCommand<SpellCardPattern>.Add(
                    scLayer.SpellCard.Patterns, scp, -1, "Add Pattern to SpellCard");
                _commandStack.Execute(cmd2);
                scLayer.InvalidateBlocks();
                OnStageDataChanged();
                SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
                return;
            }

            if (_currentLayer is not MidStageLayer midLayer) return;

            float atTime = _playback.CurrentTime;
            var pattern = _library.Resolve(patternId);
            float defaultDur = pattern?.Duration ?? 5f;
            float layerTotal = _currentLayer.TotalDuration;

            if (atTime + defaultDur > layerTotal && layerTotal > 0)
            {
                ShowDurationOverflowDialog(defaultDur, layerTotal, atTime, trimmedDur =>
                {
                    CreateEventWithPatternAndDuration(patternId, atTime, trimmedDur);
                });
                return;
            }
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

            // Try to get wave duration for overflow check
            float defaultDur = 10f;
            if (_catalog != null)
            {
                var wavePath = _catalog.GetWavePath(waveId);
                if (System.IO.File.Exists(wavePath))
                {
                    try
                    {
                        var wave = YamlSerializer.DeserializeWave(
                            System.IO.File.ReadAllText(wavePath));
                        defaultDur = wave.Duration;
                    }
                    catch { }
                }
            }

            float layerTotal = _currentLayer.TotalDuration;
            if (atTime + defaultDur > layerTotal && layerTotal > 0)
            {
                ShowDurationOverflowDialog(defaultDur, layerTotal, atTime, trimmedDur =>
                {
                    CreateEventWithWaveAndDuration(waveId, atTime, trimmedDur);
                });
                return;
            }
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
            ShowLayerSummary(_currentLayer);
            LoadBossFightPreview(seg);
            OnStageDataChanged();
        }

        /// <summary>
        /// Check whether the current layer can accept an asset of the given category.
        /// Used by AssetLibraryPanel to enable/disable "Add to Timeline" buttons.
        /// </summary>
        public bool CanAcceptAsset(AssetCategory category)
        {
            return category switch
            {
                AssetCategory.Patterns => _currentLayer is MidStageLayer
                                       || _currentLayer is SpellCardDetailLayer,
                AssetCategory.Waves    => _currentLayer is MidStageLayer,
                AssetCategory.SpellCards => _currentLayer is BossFightLayer
                                        || _editingBossFightSegment != null,
                _ => false
            };
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
                NotifyWavePlaceholders();
            };
            _stageLayer.OnDeleteSegmentRequested = blk =>
            {
                _stageLayer.DeleteSegment(blk);
            };
            _currentLayer = _stageLayer;
            _trackArea.SetLayer(_stageLayer);
            // Build a combined segment covering all segments so playback shows bullets at Stage level
            LoadStageOverviewPreview();
            NotifyWavePlaceholders();
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
                            // Keep the wave event for placeholder rendering
                            tempSegment.Events.Add(new SpawnWaveEvent
                            {
                                Id = $"_so_{seg.Id}_{sw.Id}",
                                StartTime = segmentOffset + sw.StartTime,
                                Duration = clampedDur,
                                WaveId = sw.WaveId,
                                SpawnOffset = sw.SpawnOffset
                            });
                            // Expand wave → enemy → pattern for bullet preview
                            tempSegment.Events.AddRange(ExpandWaveEvent(sw, segmentOffset, seg.Id));
                        }
                    }
                }
                else if (seg.Type == SegmentType.BossFight && _catalog != null)
                {
                    // Flatten spell card patterns into the overview
                    float scOffset = segmentOffset;
                    var localBossPath = new List<PathKeyframe>();
                    var segContext = OverrideManager.SegmentContext(seg.Id);

                    // Load all spell cards for this segment
                    var segCards = new List<SpellCard>();
                    foreach (var scId in seg.SpellCardIds)
                    {
                        var path = OverrideManager.ResolveSpellCardPath(_catalog, segContext, scId);
                        if (!System.IO.File.Exists(path)) { segCards.Add(null); continue; }
                        try
                        {
                            segCards.Add(YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path)));
                        }
                        catch { segCards.Add(null); }
                    }

                    for (int ci = 0; ci < segCards.Count; ci++)
                    {
                        var sc = segCards[ci];
                        if (sc == null) continue;
                        var scId = seg.SpellCardIds[ci];

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

                        // Insert transition path to next SC
                        if (ci < segCards.Count - 1 && sc.TransitionDuration > 0f)
                        {
                            float transDur = sc.TransitionDuration;
                            var endPos = sc.BossPath != null && sc.BossPath.Count > 0
                                ? sc.BossPath[sc.BossPath.Count - 1].Position
                                : new Vector3(0, 6, 0);

                            // Find next valid SC's start position
                            var startPos = new Vector3(0, 6, 0);
                            for (int ni = ci + 1; ni < segCards.Count; ni++)
                            {
                                if (segCards[ni]?.BossPath != null && segCards[ni].BossPath.Count > 0)
                                {
                                    startPos = segCards[ni].BossPath[0].Position;
                                    break;
                                }
                            }

                            float localTransStart = scOffset - segmentOffset;
                            localBossPath.Add(new PathKeyframe
                            {
                                Time = localTransStart,
                                Position = endPos
                            });
                            localBossPath.Add(new PathKeyframe
                            {
                                Time = localTransStart + transDur,
                                Position = startPos
                            });

                            scOffset += transDur;
                        }
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

            // ── Undo / Redo / History ──
            _undoBtn = new Button(OnUndoClicked) { text = "\u21b6" };
            _undoBtn.style.width = 28;
            _undoBtn.style.color = Lt;
            _undoBtn.style.backgroundColor = BtnBg;
            _undoBtn.style.marginLeft = 8;
            _undoBtn.tooltip = "Undo (Ctrl+Z)";
            _undoBtn.SetEnabled(false);
            _toolbar.Add(_undoBtn);

            _redoBtn = new Button(OnRedoClicked) { text = "\u21b7" };
            _redoBtn.style.width = 28;
            _redoBtn.style.color = Lt;
            _redoBtn.style.backgroundColor = BtnBg;
            _redoBtn.tooltip = "Redo (Ctrl+Y)";
            _redoBtn.SetEnabled(false);
            _toolbar.Add(_redoBtn);

            var historyBtn = new Button(ToggleHistoryPanel) { text = "History" };
            historyBtn.style.width = 52;
            historyBtn.style.color = Lt;
            historyBtn.style.backgroundColor = BtnBg;
            historyBtn.style.marginLeft = 2;
            historyBtn.tooltip = "Command History";
            _toolbar.Add(historyBtn);

            var saveBtn = new Button(SaveCurrentLayerExplicit) { text = "Save" };
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

            // ── Player mode toggle ──
            var playerBtn = new Button(() => OnPlayerModeRequested?.Invoke())
            { text = "\u25b6 Player" };
            playerBtn.style.width = 64;
            playerBtn.style.color = new Color(0.8f, 1f, 0.8f);
            playerBtn.style.backgroundColor = new Color(0.2f, 0.3f, 0.2f);
            playerBtn.style.marginLeft = 8;
            playerBtn.style.fontSize = 11;
            playerBtn.tooltip = "Toggle player mode (Spawn player + game camera)";
            _toolbar.Add(playerBtn);
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
            // Exit spell card editing if active
            if (_editingSpellCard != null)
            {
                _editingSpellCard = null;
                _editingSpellCardId = null;
                _breadcrumbSep2.style.display = DisplayStyle.None;
                _breadcrumbSpellCard.style.display = DisplayStyle.None;
                _editingBossFightSegment = null;
                _editingSpellCardInstanceContext = null;
                OnSpellCardEditingChanged?.Invoke(null);
            }

            // Reset navigation stack; push StageLayer as root so breadcrumb shows Stage > Segment
            _navigationStack.Clear();
            if (_stageLayer != null)
            {
                _navigationStack.Push(new BreadcrumbEntry
                {
                    Layer = _stageLayer,
                    DisplayName = _stageLayer.DisplayName
                });
            }

            // Clear stage overview boss tracking when entering a segment
            _stageOverviewBossRanges = null;
            _stageOverviewBossVisible = false;

            if (segment != null && segment.Type == SegmentType.BossFight)
            {
                var bfLayer = new BossFightLayer(segment, _catalog, _library);
                _currentLayer = bfLayer;
                WireLayerToTrackArea(bfLayer);
                _trackArea.SetLayer(bfLayer);
                LoadBossFightPreview(segment);
                NotifyWavePlaceholders();
            }
            else if (segment != null)
            {
                var midLayer = new MidStageLayer(segment);
                _currentLayer = midLayer;
                WireLayerToTrackArea(midLayer);
                _trackArea.SetLayer(midLayer);
                LoadMidStagePreview(segment);
                OnSpellCardEditingChanged?.Invoke(null);
                NotifyWavePlaceholders();
            }
            else
            {
                // No segment selected — return to Stage level
                _navigationStack.Clear();
                _currentLayer = _stageLayer;
                if (_stageLayer != null)
                    _trackArea.SetLayer(_stageLayer);
                _playback.LoadSegment(null);
                OnSpellCardEditingChanged?.Invoke(null);
                NotifyWavePlaceholders();
            }

            RebuildBreadcrumb();
            ShowLayerSummary(_currentLayer);
        }

        /// <summary>
        /// Build a combined temporary segment from all spell cards in a BossFight segment.
        /// Spell cards are laid out sequentially: SC1 at t=0, Transition, SC2 at t=SC1.TimeLimit+TransitionDuration, etc.
        /// Transition periods insert boss reposition keyframes (lerp from SC_n end → SC_n+1 start).
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

            // Load all spell cards first so we can reference next SC's start position
            var loadedCards = new List<(string id, SpellCard sc)>();
            foreach (var scId in segment.SpellCardIds)
            {
                var path = OverrideManager.ResolveSpellCardPath(_catalog, bfContext, scId);
                if (!System.IO.File.Exists(path)) continue;

                try
                {
                    var sc = YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path));
                    loadedCards.Add((scId, sc));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BossFightPreview] Failed to load '{scId}': {e.Message}");
                }
            }

            for (int ci = 0; ci < loadedCards.Count; ci++)
            {
                var (scId, sc) = loadedCards[ci];

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

                // Insert transition path: lerp from this SC's end position to next SC's start position
                if (ci < loadedCards.Count - 1 && sc.TransitionDuration > 0f)
                {
                    float transDur = sc.TransitionDuration;

                    // End position of current SC
                    var endPos = sc.BossPath != null && sc.BossPath.Count > 0
                        ? sc.BossPath[sc.BossPath.Count - 1].Position
                        : new Vector3(0, 6, 0);

                    // Start position of next SC
                    var nextSc = loadedCards[ci + 1].sc;
                    var startPos = nextSc.BossPath != null && nextSc.BossPath.Count > 0
                        ? nextSc.BossPath[0].Position
                        : new Vector3(0, 6, 0);

                    // Transition start keyframe (at current SC end position)
                    combinedBossPath.Add(new PathKeyframe
                    {
                        Time = timeOffset,
                        Position = endPos
                    });

                    // Transition end keyframe (at next SC start position)
                    combinedBossPath.Add(new PathKeyframe
                    {
                        Time = timeOffset + transDur,
                        Position = startPos
                    });

                    timeOffset += transDur;
                }
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
                var editIdx = i;
                var editBtn = new Button(() => EnterSpellCardEditing(segment, editScId,
                    OverrideManager.SpellCardInstanceContext(segment.Id, editIdx)))
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

        private void EnterSpellCardEditing(TimelineSegment segment, string spellCardId, string instanceContextId = null)
        {
            if (_catalog == null) return;

            // Load spell card from YAML (override-aware, using instance context)
            var resolveCtx = instanceContextId ?? OverrideManager.SegmentContext(segment.Id);
            var path = OverrideManager.ResolveSpellCardPath(_catalog, resolveCtx, spellCardId);
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
            _editingSpellCardInstanceContext = instanceContextId;

            // ── NavigateTo 7-step sequence (memorix #74) ──

            // 1. Push current layer onto navigation stack
            if (_currentLayer != null)
            {
                _navigationStack.Push(new BreadcrumbEntry
                {
                    Layer = _currentLayer,
                    DisplayName = _currentLayer.DisplayName
                });
            }

            // 2. Set _currentLayer
            // SpellCardDetailLayer.ContextId is used for Pattern Override context within this SC.
            // Format: "{instanceContext}/{spellCardId}" or fallback to "{segmentId}/{spellCardId}"
            var scDetailContext = !string.IsNullOrEmpty(instanceContextId)
                ? $"{instanceContextId}/{spellCardId}"
                : $"{segment.Id}/{spellCardId}";
            _currentLayer = new SpellCardDetailLayer(sc, spellCardId, _library,
                scDetailContext, _catalog);

            // 3. WireLayerToTrackArea (binds Add/Delete callbacks)
            WireLayerToTrackArea(_currentLayer);

            // 4. _trackArea.SetLayer (sets correct layer for context menu & interaction)
            _trackArea.SetLayer(_currentLayer);

            // 5. LoadPreview (build temporary segment for playback)
            LoadSpellCardPreview(sc);

            // 6. RebuildBreadcrumb
            RebuildBreadcrumb();

            // 7. ShowLayerSummary
            ShowLayerSummary(_currentLayer);

            OnSpellCardEditingChanged?.Invoke(sc);
        }

        /// <summary>
        /// Construct a temporary TimelineSegment from SpellCard.Patterns
        /// and load it into the playback controller for preview.
        /// NOTE: Does NOT call _trackArea.SetSegment — the caller is responsible
        /// for setting the correct layer on TrackArea (step 4 of NavigateTo sequence).
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

            _playback.LoadSegment(tempSegment);
        }

        /// <summary>
        /// Build a preview segment for a MidStage that includes expanded wave patterns.
        /// Copies all SpawnPatternEvents directly, and expands SpawnWaveEvents into
        /// SpawnPatternEvents via the Wave → EnemyInstance → EnemyType → EnemyPattern chain.
        /// </summary>
        private void LoadMidStagePreview(TimelineSegment segment)
        {
            var tempSegment = new TimelineSegment
            {
                Id = $"_mid_{segment.Id}",
                Name = segment.Name,
                Type = SegmentType.MidStage,
                Duration = segment.Duration
            };

            foreach (var evt in segment.Events)
            {
                if (evt is SpawnPatternEvent sp)
                {
                    var pattern = sp.ResolvedPattern ?? _library?.Resolve(sp.PatternId);
                    if (pattern == null) continue;
                    tempSegment.Events.Add(new SpawnPatternEvent
                    {
                        Id = sp.Id,
                        StartTime = sp.StartTime,
                        Duration = sp.Duration,
                        PatternId = sp.PatternId,
                        SpawnPosition = sp.SpawnPosition,
                        ResolvedPattern = pattern
                    });
                }
                else if (evt is SpawnWaveEvent sw)
                {
                    // Keep wave event for placeholder rendering
                    tempSegment.Events.Add(sw);
                    // Expand into pattern events for bullet preview
                    tempSegment.Events.AddRange(ExpandWaveEvent(sw, 0f, segment.Id));
                }
            }

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

            // Return to BossFight layer and reload preview
            if (_editingBossFightSegment != null)
            {
                ShowLayerSummary(_currentLayer);
                LoadBossFightPreview(_editingBossFightSegment);
                // LoadBossFightPreview already fires OnSpellCardEditingChanged with combined SC
            }
            else
            {
                _trackArea.SetLayer(null);
                _playback.LoadSegment(null);
                OnSpellCardEditingChanged?.Invoke(null);
            }

            _editingBossFightSegment = null;
            _editingSpellCardInstanceContext = null;
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
            LoadPreviewForLayer(layer);
            RebuildBreadcrumb();
            ShowLayerSummary(layer);

            // Notify wave/enemy placeholders for current layer context
            NotifyWavePlaceholders();
        }

        /// <summary>
        /// Navigate back one level. Pops the stack and restores the parent layer.
        /// </summary>
        public void NavigateBack()
        {
            if (_navigationStack.Count == 0) return;

            // Clear SpellCard editing context when leaving SpellCardDetailLayer or deeper
            if (_currentLayer is SpellCardDetailLayer or PatternLayer)
            {
                _editingSpellCard = null;
                _editingSpellCardId = null;
                _editingBossFightSegment = null;
                _editingSpellCardInstanceContext = null;
            }

            var entry = _navigationStack.Pop();
            _currentLayer = entry.Layer;

            // Returning from a child layer — parent's block data may be stale.
            // Invalidate blocks so they pick up any changes saved to disk.
            if (_currentLayer is WaveLayer parentWave)
            {
                parentWave.InvalidateBlocks();
            }
            else if (_currentLayer is EnemyTypeLayer parentEt)
            {
                parentEt.Library = _library; // ensure latest library cache
                parentEt.InvalidateBlocks();
            }

            WireLayerToTrackArea(_currentLayer);
            _trackArea.SetLayer(_currentLayer);
            LoadPreviewForLayer(_currentLayer);
            RebuildBreadcrumb();
            ShowLayerSummary(_currentLayer);

            NotifyWavePlaceholders();
        }

        /// <summary>
        /// Navigate back to a specific depth in the stack.
        /// depth=0 means go back to the root layer.
        /// </summary>
        public void NavigateToDepth(int depth)
        {
            // Clear SpellCard editing context when navigating above SpellCard level
            if (_currentLayer is SpellCardDetailLayer or PatternLayer)
            {
                _editingSpellCard = null;
                _editingSpellCardId = null;
                _editingBossFightSegment = null;
                _editingSpellCardInstanceContext = null;
            }

            while (_navigationStack.Count > depth && _navigationStack.Count > 0)
            {
                var entry = _navigationStack.Pop();
                _currentLayer = entry.Layer;
            }

            if (_currentLayer != null)
            {
                // Returning from a child layer — parent's block data may be stale.
                // Invalidate blocks so they pick up any changes saved to disk.
                if (_currentLayer is WaveLayer parentWave)
                {
                    parentWave.InvalidateBlocks();
                }
                else if (_currentLayer is EnemyTypeLayer parentEt)
                {
                    parentEt.Library = _library;
                    parentEt.InvalidateBlocks();
                }

                WireLayerToTrackArea(_currentLayer);
                _trackArea.SetLayer(_currentLayer);
                LoadPreviewForLayer(_currentLayer);
            }
            RebuildBreadcrumb();
            ShowLayerSummary(_currentLayer);

            NotifyWavePlaceholders();
        }

        /// <summary>
        /// Load the correct preview for a layer. BossFightLayer and StageLayer need
        /// special handling because their LoadPreview implementations are stubs
        /// (they require catalog/library context to build combined preview segments).
        /// All other layers use the standard ITimelineLayer.LoadPreview path.
        /// </summary>
        private void LoadPreviewForLayer(ITimelineLayer layer)
        {
            if (layer is BossFightLayer bfLayer)
            {
                LoadBossFightPreview(bfLayer.Segment);
            }
            else if (layer is StageLayer)
            {
                LoadStageOverviewPreview();
            }
            else if (layer is SpellCardDetailLayer scLayer && scLayer.SpellCard != null)
            {
                LoadSpellCardPreview(scLayer.SpellCard);
            }
            else if (layer is MidStageLayer midLayer && midLayer.Segment != null)
            {
                LoadMidStagePreview(midLayer.Segment);
            }
            else
            {
                layer.LoadPreview(_playback);
            }
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
                midLayer.ContextId = midLayer.Segment?.Id;
                midLayer.InvalidateBlocks(); // Rebuild blocks now that Catalog is set
                midLayer.OnAddPatternRequested = time => OnAddEventRequested(time);
                midLayer.OnAddWaveRequested = time => OnAddWaveEventRequested(time);
                midLayer.OnDeleteRequested = blk =>
                {
                    _trackArea.SelectBlock(blk);
                    _trackArea.DeleteSelectedEvent();
                };
                midLayer.OnRenameRequested = blk =>
                {
                    if (blk?.DataSource is SpawnPatternEvent sp)
                    {
                        var curName = _catalog?.FindPattern(sp.PatternId)?.Name ?? sp.PatternId;
                        ShowRenameDialog("Pattern", curName, newName =>
                        {
                            RenameResource("Pattern", sp.PatternId, newName);
                            _trackArea.RebuildBlocks();
                            ShowLayerSummary(_currentLayer);
                        });
                    }
                    else if (blk?.DataSource is SpawnWaveEvent sw)
                    {
                        var curName = _catalog?.FindWave(sw.WaveId)?.Name ?? sw.WaveId;
                        ShowRenameDialog("Wave", curName, newName =>
                        {
                            RenameResource("Wave", sw.WaveId, newName);
                            _trackArea.RebuildBlocks();
                            ShowLayerSummary(_currentLayer);
                        });
                    }
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
                            ShowLayerSummary(_currentLayer);
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
                bfLayer.OnSaveAsNewTemplateRequested = (resourceId, resourceType, instanceCtx) =>
                {
                    ShowSaveAsNewTemplateDialog(instanceCtx, resourceId, resourceType);
                };
            }
            else if (layer is SpellCardDetailLayer scDetailLayer)
            {
                scDetailLayer.OnAddPatternRequested = time =>
                {
                    ShowPatternPickerForSpellCard(scDetailLayer);
                };
                scDetailLayer.OnDeletePatternRequested = blk =>
                {
                    if (blk?.DataSource is SpellCardPattern scp)
                    {
                        int idx = scDetailLayer.SpellCard.Patterns.IndexOf(scp);
                        if (idx >= 0)
                        {
                            var cmd = ListCommand<SpellCardPattern>.Remove(
                                scDetailLayer.SpellCard.Patterns, idx, "Delete Pattern from SpellCard");
                            _commandStack.Execute(cmd);
                            scDetailLayer.InvalidateBlocks();
                            OnStageDataChanged();
                            SaveSpellCardInContext(scDetailLayer.SpellCard, scDetailLayer.SpellCardId);
                        }
                    }
                };
            }
            else if (layer is WaveLayer waveLayer)
            {
                // Inject catalog for EnemyType name resolution
                waveLayer.Catalog = _catalog;

                // ContextId is set by MidStageLayer.CreateChildLayer (per-event instance).
                // Only set here as fallback if not already set (e.g. direct navigation).
                if (string.IsNullOrEmpty(waveLayer.ContextId))
                {
                    foreach (var entry in _navigationStack)
                    {
                        if (entry.Layer is MidStageLayer parentMid)
                        {
                            waveLayer.ContextId = parentMid.ContextId;
                            break;
                        }
                    }
                }

                waveLayer.OnAddEnemyRequested = () =>
                {
                    ShowEnemyTypePicker(waveLayer);
                };
                waveLayer.OnDeleteEnemyRequested = blk =>
                {
                    if (blk?.DataSource is EnemyInstance ei)
                    {
                        int idx = waveLayer.Wave.Enemies.IndexOf(ei);
                        if (idx >= 0)
                        {
                            var cmd = ListCommand<EnemyInstance>.Remove(
                                waveLayer.Wave.Enemies, idx, "Delete Enemy");
                            _commandStack.Execute(cmd);
                            waveLayer.InvalidateBlocks();
                            OnStageDataChanged();
                            SaveWaveData(waveLayer);
                        }
                    }
                };
                waveLayer.OnWavePropertiesChanged = () =>
                {
                    SaveWaveData(waveLayer);
                    _trackArea.RebuildBlocks();
                };
                waveLayer.OnRenameEnemyTypeRequested = blk =>
                {
                    if (blk?.DataSource is EnemyInstance ei)
                    {
                        var curName = _catalog?.FindEnemyType(ei.EnemyTypeId)?.Name ?? ei.EnemyTypeId;
                        ShowRenameDialog("EnemyType", curName, newName =>
                        {
                            RenameResource("EnemyType", ei.EnemyTypeId, newName);
                            waveLayer.InvalidateBlocks();
                            _trackArea.RebuildBlocks();
                            ShowLayerSummary(_currentLayer);
                        });
                    }
                };
                waveLayer.OnRevertOverrideRequested = () =>
                {
                    OverrideManager.DeleteOverride(waveLayer.ContextId, waveLayer.WaveId);
                    // Reload wave from original file
                    var origPath = _catalog.GetWavePath(waveLayer.WaveId);
                    if (System.IO.File.Exists(origPath))
                    {
                        var origWave = YamlSerializer.DeserializeWaveFromFile(origPath);
                        waveLayer.ReloadWave(origWave);
                    }
                    waveLayer.InvalidateBlocks();
                    _trackArea.RebuildBlocks();
                    LoadPreviewForLayer(waveLayer);
                    ShowLayerSummary(_currentLayer);
                };
                waveLayer.OnSaveAsNewTemplateRequested = (resourceId, resourceType, ctx) =>
                {
                    ShowSaveAsNewTemplateDialog(ctx, resourceId, resourceType);
                };
            }
            else if (layer is EnemyTypeLayer etLayer)
            {
                etLayer.Library = _library;
                etLayer.InvalidateBlocks(); // Rebuild blocks now that Library is set
                etLayer.OnEnemyTypeChanged = () =>
                {
                    SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
                };
                etLayer.OnAddPatternRequested = () =>
                {
                    ShowPatternPickerForEnemyType(etLayer);
                };
                etLayer.OnDeletePatternRequested = blk =>
                {
                    if (blk?.DataSource is EnemyPattern ep)
                    {
                        int idx = etLayer.EnemyType.Patterns.IndexOf(ep);
                        if (idx >= 0)
                        {
                            var cmd = ListCommand<EnemyPattern>.Remove(
                                etLayer.EnemyType.Patterns, idx, "Delete Pattern from EnemyType");
                            _commandStack.Execute(cmd);
                            SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
                        }
                    }
                };
                etLayer.OnRenamePatternRequested = blk =>
                {
                    if (blk?.DataSource is EnemyPattern ep)
                    {
                        var curName = _catalog?.FindPattern(ep.PatternId)?.Name ?? ep.PatternId;
                        ShowRenameDialog("Pattern", curName, newName =>
                        {
                            RenameResource("Pattern", ep.PatternId, newName);
                            etLayer.InvalidateBlocks();
                            _trackArea.RebuildBlocks();
                            ShowLayerSummary(_currentLayer);
                        });
                    }
                };
                etLayer.OnRevertOverrideRequested = () =>
                {
                    OverrideManager.DeleteOverride(etLayer.ContextId, etLayer.EnemyTypeId);
                    // Reload EnemyType from original file
                    var origPath = _catalog.GetEnemyTypePath(etLayer.EnemyTypeId);
                    if (System.IO.File.Exists(origPath))
                    {
                        var origEt = YamlSerializer.DeserializeEnemyTypeFromFile(origPath);
                        // Replace in-place
                        etLayer.EnemyType.Name = origEt.Name;
                        etLayer.EnemyType.Health = origEt.Health;
                        etLayer.EnemyType.Scale = origEt.Scale;
                        etLayer.EnemyType.Color = origEt.Color;
                        etLayer.EnemyType.MeshType = origEt.MeshType;
                        etLayer.EnemyType.Patterns.Clear();
                        if (origEt.Patterns != null)
                            etLayer.EnemyType.Patterns.AddRange(origEt.Patterns);
                    }
                    etLayer.InvalidateBlocks();
                    _trackArea.RebuildBlocks();
                    LoadPreviewForLayer(etLayer);
                    ShowLayerSummary(_currentLayer);
                };
                etLayer.OnSaveAsNewTemplateRequested = (resourceId, resourceType, ctx) =>
                {
                    ShowSaveAsNewTemplateDialog(ctx, resourceId, resourceType);
                };
            }
        }

        private void ShowRenameDialog(string resourceType, string currentName, Action<string> onConfirm)
        {
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

            var title = new Label($"Rename {resourceType}");
            title.style.fontSize = 14;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            var nameField = new TextField("Name:") { value = currentName };
            nameField.style.marginBottom = 8;
            panel.Add(nameField);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var confirmBtn = new Button(() =>
            {
                var newName = nameField.value?.Trim();
                if (string.IsNullOrEmpty(newName) || newName == currentName)
                {
                    dialog.RemoveFromHierarchy();
                    return;
                }
                onConfirm?.Invoke(newName);
                dialog.RemoveFromHierarchy();
            }) { text = "Rename" };
            confirmBtn.style.backgroundColor = new Color(0.2f, 0.4f, 0.5f);
            confirmBtn.style.color = new Color(0.9f, 0.9f, 0.9f);

            var cancelBtn = new Button(() => dialog.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn.style.marginLeft = 8;

            btnRow.Add(confirmBtn);
            btnRow.Add(cancelBtn);
            panel.Add(btnRow);
            dialog.Add(panel);

            Root.panel.visualTree.Add(dialog);
        }

        /// <summary>
        /// Rename a resource's display Name in catalog and YAML file.
        /// UUID-based: only changes Name, never touches Id, file path, or references.
        /// </summary>
        private void RenameResource(string resourceType, string uuid, string newName)
        {
            if (_catalog == null || string.IsNullOrEmpty(uuid)) return;

            CatalogEntry entry = resourceType switch
            {
                "Pattern" => _catalog.FindPattern(uuid),
                "Wave" => _catalog.FindWave(uuid),
                "EnemyType" => _catalog.FindEnemyType(uuid),
                "SpellCard" => _catalog.FindSpellCard(uuid),
                _ => null
            };

            if (entry == null)
            {
                Debug.LogWarning($"[Rename] {resourceType} '{uuid}' not found in catalog.");
                return;
            }

            entry.Name = newName;
            STGCatalog.Save(_catalog);

            // Also update the Name field inside the YAML file
            var filePath = System.IO.Path.Combine(STGCatalog.BasePath, entry.File);
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    var content = System.IO.File.ReadAllText(filePath);
                    // Replace "name: ..." line
                    content = System.Text.RegularExpressions.Regex.Replace(
                        content, @"(?<=^name:\s).+$", newName,
                        System.Text.RegularExpressions.RegexOptions.Multiline);
                    System.IO.File.WriteAllText(filePath, content);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Rename] Failed to update YAML name: {e.Message}");
                }
            }

            Debug.Log($"[Rename] {resourceType} '{uuid}' renamed to '{newName}'");
        }

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

            var cancelBtn2 = new Button(() => dialog.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn2.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn2.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn2.style.marginLeft = 8;

            btnRow.Add(saveBtn);
            btnRow.Add(cancelBtn2);
            panel.Add(btnRow);
            dialog.Add(panel);

            Root.panel.visualTree.Add(dialog);
        }

        /// <summary>
        /// Rebuild the breadcrumb bar to reflect the current navigation stack.
        /// Generates N clickable labels + separators, with the last one being non-clickable.
        /// </summary>
        private void RebuildBreadcrumb()
        {
            // Collect all layers root-first
            var layers = new List<BreadcrumbEntry>();
            var stackArray = _navigationStack.ToArray();
            for (int i = stackArray.Length - 1; i >= 0; i--)
                layers.Add(stackArray[i]);
            if (_currentLayer != null)
                layers.Add(new BreadcrumbEntry { Layer = _currentLayer, DisplayName = _currentLayer.DisplayName });

            // Remove old dynamic breadcrumb elements (keep seed controls)
            // The breadcrumb bar has: [dynamic crumbs...] [spacer] [seed controls]
            // We tagged dynamic elements with a class to find them
            var toRemove = new List<VisualElement>();
            foreach (var child in _breadcrumbBar.Children())
            {
                if (child.ClassListContains("breadcrumb-dynamic"))
                    toRemove.Add(child);
            }
            foreach (var el in toRemove)
                el.RemoveFromHierarchy();

            // Also hide the old hardcoded labels (they may still be in the hierarchy)
            _breadcrumbStage.style.display = DisplayStyle.None;
            _breadcrumbSegment.style.display = DisplayStyle.None;
            _breadcrumbSep2.style.display = DisplayStyle.None;
            _breadcrumbSpellCard.style.display = DisplayStyle.None;
            _breadcrumbSep3.style.display = DisplayStyle.None;
            _breadcrumbDetail.style.display = DisplayStyle.None;

            // Hide the static separator between Stage and Segment
            foreach (var child in _breadcrumbBar.Children())
            {
                if (child.ClassListContains("breadcrumb-static-sep"))
                    child.style.display = DisplayStyle.None;
            }

            // Find the spacer (first element with flexGrow=1) to insert before it
            VisualElement spacer = null;
            foreach (var child in _breadcrumbBar.Children())
            {
                if (child.style.flexGrow == new StyleFloat(1f))
                {
                    spacer = child;
                    break;
                }
            }
            int insertIndex = spacer != null ? _breadcrumbBar.IndexOf(spacer) : _breadcrumbBar.childCount;

            // Generate dynamic breadcrumb labels
            for (int i = 0; i < layers.Count; i++)
            {
                if (i > 0)
                {
                    var sep = new Label(">");
                    sep.AddToClassList("breadcrumb-dynamic");
                    sep.style.color = new Color(0.5f, 0.5f, 0.5f);
                    sep.style.marginLeft = 4;
                    sep.style.marginRight = 4;
                    _breadcrumbBar.Insert(insertIndex++, sep);
                }

                var displayName = layers[i].DisplayName;

                // Show [M] marker for modified SpellCards
                if (layers[i].Layer is SpellCardDetailLayer scLayer &&
                    !string.IsNullOrEmpty(scLayer.ContextId) &&
                    _editingBossFightSegment != null &&
                    !string.IsNullOrEmpty(_editingSpellCardInstanceContext) &&
                    OverrideManager.HasOverride(
                        _editingSpellCardInstanceContext,
                        scLayer.SpellCardId))
                {
                    displayName = $"[M] {displayName}";
                }

                bool isLast = (i == layers.Count - 1);
                var label = new Label(displayName);
                label.AddToClassList("breadcrumb-dynamic");
                label.style.unityFontStyleAndWeight = FontStyle.Bold;

                if (isLast)
                {
                    // Current layer — bright color, not clickable
                    label.style.color = new Color(0.5f, 1f, 0.7f);
                }
                else
                {
                    // Parent layer — clickable
                    label.style.color = new Color(0.5f, 0.8f, 1f);
                    int depth = i;
                    label.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (depth == 0 && _stageLayer != null)
                        {
                            // Special handling for Stage level
                            if (_editingSpellCard != null)
                            {
                                _editingSpellCard = null;
                                _editingSpellCardId = null;
                                _editingBossFightSegment = null;
                                _editingSpellCardInstanceContext = null;
                            }
                            NavigateToDepth(0);
                            _currentLayer = _stageLayer;
                            _trackArea.SetLayer(_stageLayer);
                            LoadStageOverviewPreview();
                            RebuildBreadcrumb();
                            NotifyWavePlaceholders();
                        }
                        else
                        {
                            if (_editingSpellCard != null)
                                ExitSpellCardEditing();
                            else if (_navigationStack.Count > depth)
                                NavigateToDepth(depth);
                        }
                    });
                    label.style.cursor = new UnityEngine.UIElements.Cursor(); // indicate clickable
                }

                label.style.marginRight = 4;
                _breadcrumbBar.Insert(insertIndex++, label);
            }
        }

        private void SaveCurrentSpellCard()
        {
            if (_editingSpellCard == null || _editingSpellCardId == null || _catalog == null) return;

            try
            {
                var yaml = YamlSerializer.SerializeSpellCard(_editingSpellCard);

                // If editing within a BossFight segment context, save as override
                if (_editingBossFightSegment != null && !string.IsNullOrEmpty(_editingSpellCardInstanceContext))
                {
                    OverrideManager.SaveOverride(_editingSpellCardInstanceContext, _editingSpellCardId, yaml);
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
        /// Save a SpellCard to disk (override or original).
        /// Uses explicit contextId if provided, otherwise falls back to global editing context.
        /// </summary>
        private void SaveSpellCardInContext(SpellCard sc, string scId, string contextId = null)
        {
            if (sc == null || scId == null || _catalog == null) return;
            // Resolve effective context: explicit param > global editing state > null (original file)
            var effectiveCtx = contextId
                ?? (_editingBossFightSegment != null ? _editingSpellCardInstanceContext : null);
            try
            {
                var yaml = YamlSerializer.SerializeSpellCard(sc);
                if (!string.IsNullOrEmpty(effectiveCtx))
                {
                    OverrideManager.SaveOverride(effectiveCtx, scId, yaml);
                }
                else
                {
                    var path = _catalog.GetSpellCardPath(scId);
                    System.IO.File.WriteAllText(path, yaml);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Failed to save spell card '{scId}': {e.Message}");
            }
        }

        /// <summary>
        /// Save a Wave to disk using the Override mechanism.
        /// If a contextId is set, saves to Modified/{contextId}/{waveId}.yaml (preserving the original template).
        /// Otherwise saves directly to the original file.
        /// </summary>
        private void SaveWaveData(WaveLayer waveLayer)
        {
            if (waveLayer?.Wave == null || _catalog == null) return;
            try
            {
                var yaml = YamlSerializer.SerializeWave(waveLayer.Wave);
                var contextId = waveLayer.ContextId;
                if (!string.IsNullOrEmpty(contextId))
                {
                    OverrideManager.SaveOverride(contextId, waveLayer.WaveId, yaml);
                }
                else
                {
                    var path = _catalog.GetWavePath(waveLayer.WaveId);
                    System.IO.File.WriteAllText(path, yaml);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Failed to save wave '{waveLayer.WaveId}': {e.Message}");
            }

            // Notify placeholders of wave data changes
            NotifyWavePlaceholders();
        }

        /// <summary>
        /// Save an EnemyType to disk and update the catalog.
        /// </summary>
        private void SaveEnemyType(EnemyType enemyType, string enemyTypeId, string contextId = null)
        {
            if (_catalog == null || enemyType == null) return;
            try
            {
                if (!string.IsNullOrEmpty(contextId))
                {
                    var yaml = YamlSerializer.SerializeEnemyType(enemyType);
                    OverrideManager.SaveOverride(contextId, enemyTypeId, yaml);
                }
                else
                {
                    var path = _catalog.GetEnemyTypePath(enemyTypeId);
                    YamlSerializer.SerializeEnemyTypeToFile(enemyType, path);
                }
                _catalog.AddOrUpdateEnemyType(enemyTypeId, enemyType.Name);
                STGCatalog.Save(_catalog);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Failed to save EnemyType '{enemyTypeId}': {e.Message}");
            }
        }

        /// <summary>
        /// Expand a SpawnWaveEvent into SpawnPatternEvents by resolving
        /// Wave → EnemyInstance → EnemyType → EnemyPattern chain.
        /// Each enemy's patterns are offset by the enemy's SpawnDelay + path position.
        /// </summary>
        private List<SpawnPatternEvent> ExpandWaveEvent(SpawnWaveEvent sw, float timeOffset, string segmentId = null)
        {
            var result = new List<SpawnPatternEvent>();
            if (_catalog == null || _library == null) return result;

            // Resolve wave (override first, then catalog fallback)
            // Override contextId = "{segmentId}/{eventId}" (per-event-instance isolation)
            string waveContextId = !string.IsNullOrEmpty(segmentId) ? $"{segmentId}/{sw.Id}" : null;
            var wavePath = OverrideManager.ResolveWavePath(_catalog, waveContextId, sw.WaveId);
            if (string.IsNullOrEmpty(wavePath) || !System.IO.File.Exists(wavePath)) return result;
            Wave wave;
            try { wave = YamlSerializer.DeserializeWaveFromFile(wavePath); }
            catch { return result; }
            if (wave?.Enemies == null) return result;

            foreach (var ei in wave.Enemies)
            {
                // Resolve enemy type (override-aware, same context as wave)
                var etPath = OverrideManager.ResolveEnemyTypePath(_catalog, waveContextId, ei.EnemyTypeId);
                if (string.IsNullOrEmpty(etPath) || !System.IO.File.Exists(etPath)) continue;
                EnemyType enemyType;
                try { enemyType = YamlSerializer.DeserializeEnemyTypeFromFile(etPath); }
                catch { continue; }
                if (enemyType?.Patterns == null || enemyType.Patterns.Count == 0) continue;

                float enemyStart = timeOffset + sw.StartTime + ei.SpawnDelay;

                foreach (var ep in enemyType.Patterns)
                {
                    var pattern = _library.Resolve(ep.PatternId);
                    if (pattern == null) continue;

                    // Enemy position at pattern fire time (relative to enemy spawn)
                    var enemyPos = EnemyTypeLayer.EvaluateEnemyPath(ei.Path, ep.Delay);

                    result.Add(new SpawnPatternEvent
                    {
                        Id = $"_wave_{sw.WaveId}_{ei.EnemyTypeId}_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                        StartTime = enemyStart + ep.Delay,
                        Duration = ep.Duration,
                        PatternId = ep.PatternId,
                        SpawnPosition = sw.SpawnOffset + enemyPos + ep.Offset,
                        ResolvedPattern = pattern
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Build WavePlaceholderData from the current layer context and fire OnWaveEditingChanged.
        /// - WaveLayer: single wave, no offset
        /// - MidStageLayer: all SpawnWaveEvents in the segment, each with its StartTime offset
        /// - StageLayer: all waves across all MidStage segments, with cumulative segment offsets
        /// - Other layers: null (hide placeholders)
        /// Called internally on navigation changes, and can be called externally after event subscription.
        /// </summary>
        public void NotifyWavePlaceholders()
        {
            if (OnWaveEditingChanged == null) return;

            if (_currentLayer is WaveLayer wl)
            {
                var list = new List<WavePlaceholderData>
                {
                    new() { Wave = wl.Wave, TimeOffset = 0f, SpawnOffset = Vector3.zero }
                };
                OnWaveEditingChanged.Invoke(list);
            }
            else if (_currentLayer is MidStageLayer midLayer)
            {
                var list = BuildWavePlaceholdersForSegment(midLayer.Segment, 0f);
                OnWaveEditingChanged.Invoke(list.Count > 0 ? list : null);
            }
            else if (_currentLayer is StageLayer && _stage != null)
            {
                var list = new List<WavePlaceholderData>();
                float segOffset = 0f;
                foreach (var seg in _stage.Segments)
                {
                    if (seg.Type == SegmentType.MidStage)
                        list.AddRange(BuildWavePlaceholdersForSegment(seg, segOffset));
                    segOffset += seg.Duration;
                }
                OnWaveEditingChanged.Invoke(list.Count > 0 ? list : null);
            }
            else
            {
                OnWaveEditingChanged.Invoke(null);
            }
        }

        /// <summary>
        /// Load all waves from SpawnWaveEvents in a segment and build placeholder data.
        /// </summary>
        private List<WavePlaceholderData> BuildWavePlaceholdersForSegment(
            TimelineSegment segment, float segmentOffset)
        {
            var result = new List<WavePlaceholderData>();
            if (segment == null || _catalog == null) return result;

            var segContext = OverrideManager.SegmentContext(segment.Id);

            foreach (var evt in segment.Events)
            {
                if (evt is SpawnWaveEvent sw)
                {
                    try
                    {
                        var eventContextId = $"{segment.Id}/{sw.Id}";
                        var path = OverrideManager.ResolveWavePath(_catalog, eventContextId, sw.WaveId);
                        if (!System.IO.File.Exists(path))
                            path = _catalog.GetWavePath(sw.WaveId);
                        if (!System.IO.File.Exists(path)) continue;

                        var wave = YamlSerializer.DeserializeWave(
                            System.IO.File.ReadAllText(path));
                        result.Add(new WavePlaceholderData
                        {
                            Wave = wave,
                            TimeOffset = segmentOffset + sw.StartTime,
                            SpawnOffset = sw.SpawnOffset
                        });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[TimelineEditor] Failed to load wave '{sw.WaveId}' for placeholder: {e.Message}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Show a pattern picker popup for adding a pattern to a SpellCard.
        /// </summary>
        private void ShowPatternPickerForSpellCard(SpellCardDetailLayer scLayer)
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
                picker.style.borderLeftColor = picker.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            picker.style.paddingTop = picker.style.paddingBottom = 8;
            picker.style.paddingLeft = picker.style.paddingRight = 12;
            picker.style.borderTopLeftRadius = picker.style.borderTopRightRadius =
                picker.style.borderBottomLeftRadius = picker.style.borderBottomRightRadius = 6;
            picker.style.minWidth = 200;
            picker.style.maxHeight = 300;

            var title = new Label("Select Pattern");
            title.style.fontSize = 12;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            picker.Add(title);

            foreach (var entry in _catalog.Patterns)
            {
                var label = entry.DisplayLabel;
                var patId = entry.Id;
                var btn = new Button(() =>
                {
                    picker.RemoveFromHierarchy();
                    var scp = new SpellCardPattern
                    {
                        PatternId = patId,
                        Delay = 0f,
                        Duration = 5f,
                        Offset = Vector3.zero
                    };
                    var cmd = ListCommand<SpellCardPattern>.Add(
                        scLayer.SpellCard.Patterns, scp, -1, "Add Pattern to SpellCard");
                    _commandStack.Execute(cmd);
                    scLayer.InvalidateBlocks();
                    OnStageDataChanged();
                    SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
                }) { text = label };
                btn.style.backgroundColor = new Color(0.25f, 0.2f, 0.3f);
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
        /// Show a pattern picker popup for adding a pattern to an EnemyType.
        /// </summary>
        private void ShowPatternPickerForEnemyType(EnemyTypeLayer etLayer)
        {
            var patternIds = new List<string>(_library.PatternIds);
            if (patternIds.Count == 0)
            {
                Debug.LogWarning("[TimelineEditor] No patterns available in library.");
                return;
            }

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

            var pickerTitle = new Label("Select Pattern");
            pickerTitle.style.color = new Color(0.9f, 0.9f, 0.9f);
            pickerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            pickerTitle.style.marginBottom = 8;
            picker.Add(pickerTitle);

            foreach (var pid in patternIds)
            {
                var btn = new Button(() =>
                {
                    picker.RemoveFromHierarchy();
                    var newEp = new EnemyPattern
                    {
                        PatternId = pid,
                        Delay = 0f,
                        Duration = 5f
                    };
                    var cmd = ListCommand<EnemyPattern>.Add(
                        etLayer.EnemyType.Patterns, newEp, desc: "Add Pattern to EnemyType");
                    _commandStack.Execute(cmd);
                    SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
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

        // ═══════════════════════════════════════════════════════════════════
        //  Property Panel Builders (Phase 5/6)
        // ═══════════════════════════════════════════════════════════════════

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
                if (block?.DataSource is TimelineSegment segment)
                {
                    _propertyContent.Clear();
                    BuildSegmentProperties(segment);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                    ApplyLightTextTheme(_propertyContent);
                }
            }
            else if (_currentLayer is BossFightLayer bfLayer)
            {
                if (block is SpellCardBlock scBlock)
                {
                    var sc = scBlock.DataSource as SpellCard;
                    if (sc != null)
                    {
                        _propertyContent.Clear();
                        BuildSpellCardBlockProperties(sc, scBlock.SpellCardId, bfLayer, scBlock.InstanceContextId);
                    }
                }
                else if (block is TransitionBlock transBlock)
                {
                    _propertyContent.Clear();
                    BuildTransitionBlockProperties(transBlock, bfLayer);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                    ApplyLightTextTheme(_propertyContent);
                }
            }
            else if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                if (block?.DataSource is SpellCardPattern scp)
                {
                    _propertyContent.Clear();
                    BuildSpellCardPatternProperties(scp, scLayer);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                    ApplyLightTextTheme(_propertyContent);
                }
            }
            else if (_currentLayer is WaveLayer waveLayer)
            {
                if (block?.DataSource is EnemyInstance ei)
                {
                    _propertyContent.Clear();
                    BuildEnemyInstanceProperties(ei, waveLayer);
                }
                else
                {
                    _propertyContent.Clear();
                    _currentLayer.BuildPropertiesPanel(_propertyContent, null);
                    ApplyLightTextTheme(_propertyContent);
                }
            }
            else if (_currentLayer is PatternLayer)
            {
                ShowLayerSummary(_currentLayer);
            }
            else if (_currentLayer is EnemyTypeLayer etLayer2)
            {
                if (block?.DataSource is EnemyPattern ep)
                {
                    _propertyContent.Clear();
                    BuildEnemyPatternProperties(ep, etLayer2);
                }
                else
                {
                    ShowLayerSummary(_currentLayer);
                }
            }
            else
            {
                _propertyContent.Clear();
                _currentLayer.BuildPropertiesPanel(_propertyContent, block);
                ApplyLightTextTheme(_propertyContent);
            }
        }

        // ─── Layer Summary (shown when no block is selected) ───

        /// <summary>
        /// Show a brief read-only summary of the current layer in the properties panel.
        /// Displayed when entering a layer or after structural changes (no block selected).
        /// </summary>
        private void ShowLayerSummary(ITimelineLayer layer)
        {
            _propertyContent.Clear();

            // Clean up previous pattern editor and previewer
            if (_patternEditor != null)
            {
                _patternEditor.Commands.OnStateChanged -= OnPatternEditorChanged;
                _patternEditor.Dispose();
                if (_singlePreviewer != null)
                    _singlePreviewer.Pattern = null;
            }
            _patternEditor = null;

            if (layer == null) return;

            _propertyHeaderLabel.text = layer.DisplayName;

            if (layer is PatternLayer patLayer && patLayer.Pattern != null && _singlePreviewer != null)
            {
                BuildPatternLayerProperties(patLayer);
                return;
            }

            if (layer is SpellCardDetailLayer scDetailLayer)
            {
                BossFightLayer parentBf = null;
                foreach (var entry in _navigationStack)
                {
                    if (entry.Layer is BossFightLayer bf) { parentBf = bf; break; }
                }
                BuildSpellCardBlockProperties(scDetailLayer.SpellCard, scDetailLayer.SpellCardId, parentBf,
                    _editingSpellCardInstanceContext);
                return;
            }

            if (layer is EnemyTypeLayer etLayer)
            {
                _propertyHeaderLabel.text = $"EnemyType: {etLayer.DisplayName}";

                var container = new VisualElement();
                container.style.paddingTop = 4;
                container.style.paddingLeft = 8;
                container.style.paddingRight = 8;

                // Save context status bar
                container.Add(CreateSaveContextBar("EnemyType", etLayer.ContextId));

                // If we have a source EnemyInstance, show its path keyframe editor first
                if (etLayer.SourceInstance != null)
                {
                    var ei = etLayer.SourceInstance;

                    var instanceHeader = new Label($"Enemy Instance: {ei.EnemyTypeId}");
                    instanceHeader.style.color = new Color(1f, 0.7f, 0.5f);
                    instanceHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                    instanceHeader.style.marginBottom = 4;
                    container.Add(instanceHeader);

                    // SpawnDelay (read-only info in this context)
                    var delayInfo = new Label($"Spawn Delay: {ei.SpawnDelay:F1}s");
                    delayInfo.style.color = new Color(0.6f, 0.6f, 0.6f);
                    delayInfo.style.marginBottom = 4;
                    container.Add(delayInfo);

                    // Path keyframe editor — find parent WaveLayer for save callback
                    WaveLayer parentWave = null;
                    foreach (var entry in _navigationStack)
                    {
                        if (entry.Layer is WaveLayer wl) { parentWave = wl; break; }
                    }
                    if (parentWave != null)
                    {
                        if (ei.Path == null) ei.Path = new List<PathKeyframe>();
                        BuildEnemyPathEditor(container, ei, parentWave);
                    }

                    var separator = new VisualElement();
                    separator.style.height = 1;
                    separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                    separator.style.marginTop = 10;
                    separator.style.marginBottom = 6;
                    container.Add(separator);
                }

                // EnemyType stats panel
                etLayer.BuildEnemyTypePropertiesPanel(container, _commandStack);

                _propertyContent.Add(container);
                ApplyLightTextTheme(_propertyContent);
                return;
            }

            // Wave/generic layers: add save context bar before default panel
            if (layer is WaveLayer wvSummary)
            {
                _propertyContent.Add(CreateSaveContextBar("Wave", wvSummary.ContextId));
            }

            layer.BuildPropertiesPanel(_propertyContent, null);
            ApplyLightTextTheme(_propertyContent);
        }

        // ─── Per-Layer Editable Properties ───

        private void BuildPatternLayerProperties(PatternLayer patLayer)
        {
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Save context status bar
            container.Add(CreateSaveContextBar("Pattern", patLayer.ContextId));

            var patternHeader = new Label($"Pattern: {_catalog?.FindPattern(patLayer.PatternId)?.Name ?? patLayer.PatternId}");
            patternHeader.style.color = new Color(0.7f, 0.85f, 1f);
            patternHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            patternHeader.style.marginBottom = 4;
            container.Add(patternHeader);

            _singlePreviewer.Pattern = patLayer.Pattern;
            _patternEditor = new PatternEditorView(_singlePreviewer, GetPatternOverrideContext());
            _patternEditor.OnMeshTypeChanged = OnMeshTypeChanged;
            _patternEditor.SetPattern(patLayer.Pattern);
            _patternEditor.Commands.OnStateChanged += OnPatternEditorChanged;

            var editorRoot = _patternEditor.Root;
            editorRoot.style.width = Length.Percent(100);
            editorRoot.style.minWidth = StyleKeyword.Auto;
            editorRoot.style.maxWidth = StyleKeyword.Auto;
            editorRoot.style.backgroundColor = Color.clear;
            container.Add(editorRoot);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

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
                    "Rename Segment",
                    () => segment.Name, v => segment.Name = v, e.newValue);
                _commandStack.Execute(cmd);
                // Execute triggers OnCommandStateChanged → RebuildBlocks (updates label)
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

        private void BuildSpellCardBlockProperties(SpellCard sc, string scId, BossFightLayer bfLayer,
            string instanceContextId = null)
        {
            _propertyHeaderLabel.text = $"SpellCard: {_catalog?.FindSpellCard(scId)?.Name ?? scId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Helper: execute command + persist to disk (uses explicit instance context)
            void ExecAndSave(ICommand cmd)
            {
                _commandStack.Execute(cmd);
                SaveSpellCardInContext(sc, scId, instanceContextId);
            }

            // Name
            var nameField = new TextField("Name") { value = sc.Name ?? "" };
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(e =>
            {
                ExecAndSave(new PropertyChangeCommand<string>(
                    "Rename SpellCard",
                    () => sc.Name, v => sc.Name = v, e.newValue));
            });
            container.Add(nameField);

            // TimeLimit
            var tlField = new FloatField("Time Limit") { value = sc.TimeLimit };
            tlField.isDelayed = true;
            tlField.RegisterValueChangedCallback(e =>
            {
                ExecAndSave(new PropertyChangeCommand<float>(
                    "Change SpellCard TimeLimit",
                    () => sc.TimeLimit, v => sc.TimeLimit = v,
                    Mathf.Max(1f, e.newValue)));
            });
            container.Add(tlField);

            // Health
            var hpField = new FloatField("Health") { value = sc.Health };
            hpField.isDelayed = true;
            hpField.RegisterValueChangedCallback(e =>
            {
                ExecAndSave(new PropertyChangeCommand<float>(
                    "Change SpellCard Health",
                    () => sc.Health, v => sc.Health = v,
                    Mathf.Max(1f, e.newValue)));
            });
            container.Add(hpField);

            // TransitionDuration
            var transField = new FloatField("Transition Duration") { value = sc.TransitionDuration };
            transField.isDelayed = true;
            transField.RegisterValueChangedCallback(e =>
            {
                ExecAndSave(new PropertyChangeCommand<float>(
                    "Change Transition Duration",
                    () => sc.TransitionDuration, v => sc.TransitionDuration = v,
                    Mathf.Max(0.1f, e.newValue)));
            });
            container.Add(transField);

            // DesignEstimate
            var deField = new FloatField("Design Estimate") { value = sc.DesignEstimate };
            deField.isDelayed = true;
            deField.RegisterValueChangedCallback(e =>
            {
                ExecAndSave(new PropertyChangeCommand<float>(
                    "Change Design Estimate",
                    () => sc.DesignEstimate, v => sc.DesignEstimate = v,
                    e.newValue));
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
                EnterSpellCardEditing(bfLayer.Segment, scId, instanceContextId);
            }) { text = "Edit SpellCard Details ▶" };
            editBtn.style.marginTop = 8;
            editBtn.style.backgroundColor = new Color(0.25f, 0.3f, 0.45f);
            editBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            container.Add(editBtn);

            // Rename SpellCard
            var currentScName = _catalog?.FindSpellCard(scId)?.Name ?? scId;
            var renameBtn = new Button(() =>
            {
                ShowRenameDialog("SpellCard", currentScName, newName =>
                {
                    RenameResource("SpellCard", scId, newName);
                    bfLayer.InvalidateBlocks();
                    _trackArea.RebuildBlocks();
                    ShowLayerSummary(_currentLayer);
                });
            }) { text = "Rename..." };
            renameBtn.style.marginTop = 4;
            renameBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.2f);
            renameBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            container.Add(renameBtn);

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildTransitionBlockProperties(TransitionBlock transBlock, BossFightLayer bfLayer)
        {
            var sc = transBlock.DataSource as SpellCard;
            _propertyHeaderLabel.text = "Transition";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            var desc = new Label("Bullet-clear + boss reposition between spell cards.");
            desc.style.color = new Color(0.6f, 0.6f, 0.6f);
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginBottom = 8;
            container.Add(desc);

            if (sc != null)
            {
                var durField = new FloatField("Duration") { value = sc.TransitionDuration };
                durField.isDelayed = true;
                durField.RegisterValueChangedCallback(e =>
                {
                    var cmd = new PropertyChangeCommand<float>(
                        "Change Transition Duration",
                        () => sc.TransitionDuration, v => sc.TransitionDuration = v,
                        Mathf.Max(0.1f, e.newValue));
                    _commandStack.Execute(cmd);
                    SaveSpellCardInContext(sc, transBlock.Id.Replace("_transition_", ""));
                });
                container.Add(durField);

                var ownerLabel = new Label($"Owner: {sc.Name ?? "(unnamed)"}");
                ownerLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                ownerLabel.style.marginTop = 4;
                container.Add(ownerLabel);
            }

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildSpellCardPatternProperties(SpellCardPattern scp, SpellCardDetailLayer scLayer)
        {
            _propertyHeaderLabel.text = $"Pattern: {_catalog?.FindPattern(scp.PatternId)?.Name ?? scp.PatternId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Pattern ID (read-only)
            var idLabel = new Label($"Pattern: {_catalog?.FindPattern(scp.PatternId)?.Name ?? scp.PatternId}");
            idLabel.style.color = new Color(0.7f, 0.85f, 1f);
            idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLabel.style.marginBottom = 4;
            container.Add(idLabel);

            // Delay
            var delayField = new FloatField("Delay") { value = scp.Delay };
            delayField.isDelayed = true;
            delayField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Pattern Delay",
                    () => scp.Delay, v => scp.Delay = v,
                    Mathf.Max(0f, e.newValue));
                _commandStack.Execute(cmd);
            });
            container.Add(delayField);

            // Duration
            var durField = new FloatField("Duration") { value = scp.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Pattern Duration",
                    () => scp.Duration, v => scp.Duration = v,
                    Mathf.Max(0.1f, e.newValue));
                _commandStack.Execute(cmd);
            });
            container.Add(durField);

            // Offset X/Y/Z
            var ox = new FloatField("Offset X") { value = scp.Offset.x };
            ox.isDelayed = true;
            ox.RegisterValueChangedCallback(e =>
            {
                var newOffset = new Vector3(e.newValue, scp.Offset.y, scp.Offset.z);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change Pattern Offset",
                    () => scp.Offset, v => scp.Offset = v, newOffset);
                _commandStack.Execute(cmd);
            });
            container.Add(ox);

            var oy = new FloatField("Offset Y") { value = scp.Offset.y };
            oy.isDelayed = true;
            oy.RegisterValueChangedCallback(e =>
            {
                var newOffset = new Vector3(scp.Offset.x, e.newValue, scp.Offset.z);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change Pattern Offset",
                    () => scp.Offset, v => scp.Offset = v, newOffset);
                _commandStack.Execute(cmd);
            });
            container.Add(oy);

            var oz = new FloatField("Offset Z") { value = scp.Offset.z };
            oz.isDelayed = true;
            oz.RegisterValueChangedCallback(e =>
            {
                var newOffset = new Vector3(scp.Offset.x, scp.Offset.y, e.newValue);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change Pattern Offset",
                    () => scp.Offset, v => scp.Offset = v, newOffset);
                _commandStack.Execute(cmd);
            });
            container.Add(oz);

            // Inline pattern editor if resolved
            var resolvedPattern = _library?.Resolve(scp.PatternId);
            if (resolvedPattern != null && _singlePreviewer != null)
            {
                var separator = new VisualElement();
                separator.style.height = 1;
                separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                separator.style.marginTop = 8;
                separator.style.marginBottom = 4;
                container.Add(separator);

                var patternHeader = new Label("Pattern Parameters");
                patternHeader.style.color = new Color(0.8f, 0.8f, 0.8f);
                patternHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                patternHeader.style.marginBottom = 4;
                container.Add(patternHeader);

                _singlePreviewer.Pattern = resolvedPattern;
                _patternEditor = new PatternEditorView(_singlePreviewer, GetPatternOverrideContext());
                _patternEditor.OnMeshTypeChanged = OnMeshTypeChanged;
                _patternEditor.SetPattern(resolvedPattern);
                _patternEditor.Commands.OnStateChanged += OnPatternEditorChanged;

                var editorRoot = _patternEditor.Root;
                editorRoot.style.width = Length.Percent(100);
                editorRoot.style.minWidth = StyleKeyword.Auto;
                editorRoot.style.maxWidth = StyleKeyword.Auto;
                editorRoot.style.backgroundColor = Color.clear;
                container.Add(editorRoot);
            }

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildEnemyInstanceProperties(EnemyInstance ei, WaveLayer waveLayer)
        {
            _propertyHeaderLabel.text = $"Enemy: {_catalog?.FindEnemyType(ei.EnemyTypeId)?.Name ?? ei.EnemyTypeId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // EnemyType ID (read-only)
            var idLabel = new Label($"Type: {_catalog?.FindEnemyType(ei.EnemyTypeId)?.Name ?? ei.EnemyTypeId}");
            idLabel.style.color = new Color(1f, 0.7f, 0.5f);
            idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLabel.style.marginBottom = 4;
            container.Add(idLabel);

            // Rename EnemyType button
            var renameEtBtn = new Button(() =>
            {
                var curName = _catalog?.FindEnemyType(ei.EnemyTypeId)?.Name ?? "";
                ShowRenameDialog("EnemyType", curName, newName =>
                {
                    RenameResource("EnemyType", ei.EnemyTypeId, newName);
                    waveLayer.InvalidateBlocks();
                    _trackArea.RebuildBlocks();
                    BuildEnemyInstanceProperties(ei, waveLayer);
                });
            }) { text = "Rename..." };
            renameEtBtn.style.marginTop = 2;
            renameEtBtn.style.marginBottom = 4;
            renameEtBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.2f);
            renameEtBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            container.Add(renameEtBtn);

            // SpawnDelay
            var delayField = new FloatField("Spawn Delay") { value = ei.SpawnDelay };
            delayField.isDelayed = true;
            delayField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Spawn Delay",
                    () => ei.SpawnDelay, v => ei.SpawnDelay = v,
                    Mathf.Max(0f, e.newValue));
                _commandStack.Execute(cmd);
            });
            container.Add(delayField);

            // Path editor
            BuildEnemyPathEditor(container, ei, waveLayer);

            // ── Append EnemyType stats below path editor ──
            if (_catalog != null)
            {
                var waveCtx = waveLayer?.ContextId;
                var etPath = OverrideManager.ResolveEnemyTypePath(_catalog, waveCtx, ei.EnemyTypeId);
                if (System.IO.File.Exists(etPath))
                {
                    var enemyType = YamlSerializer.DeserializeEnemyTypeFromFile(etPath);

                    var separator = new VisualElement();
                    separator.style.height = 1;
                    separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                    separator.style.marginTop = 10;
                    separator.style.marginBottom = 6;
                    container.Add(separator);

                    var etHeader = new Label("EnemyType Properties");
                    etHeader.style.color = new Color(0.85f, 0.85f, 0.85f);
                    etHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                    etHeader.style.marginBottom = 4;
                    container.Add(etHeader);

                    // Reuse EnemyTypeLayer's panel builder
                    var tempLayer = new EnemyTypeLayer(enemyType, ei.EnemyTypeId, _catalog);
                    tempLayer.ContextId = waveCtx;
                    tempLayer.OnEnemyTypeChanged = () =>
                    {
                        SaveEnemyType(enemyType, ei.EnemyTypeId, waveCtx);
                    };
                    tempLayer.BuildEnemyTypePropertiesPanel(container, _commandStack);
                }
            }

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        private void BuildEnemyPatternProperties(EnemyPattern ep, EnemyTypeLayer etLayer)
        {
            _propertyHeaderLabel.text = $"Pattern: {_catalog?.FindPattern(ep.PatternId)?.Name ?? ep.PatternId}";
            var container = new VisualElement();
            container.style.paddingTop = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;

            // Pattern ID (read-only)
            var idLabel = new Label($"Pattern: {_catalog?.FindPattern(ep.PatternId)?.Name ?? ep.PatternId}");
            idLabel.style.color = new Color(0.7f, 0.85f, 1f);
            idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            idLabel.style.marginBottom = 4;
            container.Add(idLabel);

            // Delay
            var delayField = new FloatField("Delay") { value = ep.Delay };
            delayField.isDelayed = true;
            delayField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Pattern Delay",
                    () => ep.Delay, v => ep.Delay = v,
                    Mathf.Max(0f, e.newValue));
                _commandStack.Execute(cmd);
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
            });
            container.Add(delayField);

            // Duration
            var durField = new FloatField("Duration") { value = ep.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                var cmd = new PropertyChangeCommand<float>(
                    "Change Pattern Duration",
                    () => ep.Duration, v => ep.Duration = v,
                    Mathf.Max(0.1f, e.newValue));
                _commandStack.Execute(cmd);
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
            });
            container.Add(durField);

            // Offset X/Y/Z
            var epOx = new FloatField("Offset X") { value = ep.Offset.x };
            epOx.isDelayed = true;
            epOx.RegisterValueChangedCallback(e =>
            {
                var newOffset = new Vector3(e.newValue, ep.Offset.y, ep.Offset.z);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change EnemyPattern Offset",
                    () => ep.Offset, v => ep.Offset = v, newOffset);
                _commandStack.Execute(cmd);
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
            });
            container.Add(epOx);

            var epOy = new FloatField("Offset Y") { value = ep.Offset.y };
            epOy.isDelayed = true;
            epOy.RegisterValueChangedCallback(e =>
            {
                var newOffset = new Vector3(ep.Offset.x, e.newValue, ep.Offset.z);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change EnemyPattern Offset",
                    () => ep.Offset, v => ep.Offset = v, newOffset);
                _commandStack.Execute(cmd);
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
            });
            container.Add(epOy);

            var epOz = new FloatField("Offset Z") { value = ep.Offset.z };
            epOz.isDelayed = true;
            epOz.RegisterValueChangedCallback(e =>
            {
                var newOffset = new Vector3(ep.Offset.x, ep.Offset.y, e.newValue);
                var cmd = new PropertyChangeCommand<Vector3>(
                    "Change EnemyPattern Offset",
                    () => ep.Offset, v => ep.Offset = v, newOffset);
                _commandStack.Execute(cmd);
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
            });
            container.Add(epOz);

            // Inline pattern editor if resolved
            var resolvedPattern = _library?.Resolve(ep.PatternId);
            if (resolvedPattern != null && _singlePreviewer != null)
            {
                var separator = new VisualElement();
                separator.style.height = 1;
                separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                separator.style.marginTop = 8;
                separator.style.marginBottom = 4;
                container.Add(separator);

                var patternHeader = new Label("Pattern Parameters");
                patternHeader.style.color = new Color(0.8f, 0.8f, 0.8f);
                patternHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                patternHeader.style.marginBottom = 4;
                container.Add(patternHeader);

                _singlePreviewer.Pattern = resolvedPattern;
                _patternEditor = new PatternEditorView(_singlePreviewer, GetPatternOverrideContext());
                _patternEditor.OnMeshTypeChanged = OnMeshTypeChanged;
                _patternEditor.SetPattern(resolvedPattern);
                _patternEditor.Commands.OnStateChanged += OnPatternEditorChanged;

                var editorRoot = _patternEditor.Root;
                editorRoot.style.width = Length.Percent(100);
                editorRoot.style.minWidth = StyleKeyword.Auto;
                editorRoot.style.maxWidth = StyleKeyword.Auto;
                editorRoot.style.backgroundColor = Color.clear;
                container.Add(editorRoot);
            }

            _propertyContent.Add(container);
            ApplyLightTextTheme(container);
        }

        /// <summary>
        /// Build a collapsible keyframe list editor for an EnemyInstance's movement path.
        /// Follows the same UI pattern as BuildBossPathEditor.
        /// </summary>
        private void BuildEnemyPathEditor(VisualElement parent, EnemyInstance ei, WaveLayer waveLayer)
        {
            var keyframes = ei.Path;

            // Rebuild helper: re-select the same block to refresh the entire panel
            void RebuildList()
            {
                var selected = _trackArea.SelectedBlock;
                if (selected != null)
                    OnBlockSelectedGeneric(selected);
                else if (_currentLayer != null)
                    ShowLayerSummary(_currentLayer);
            }

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

                // Summary row
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
                    SaveWaveData(waveLayer);
                    _trackArea.InvalidateThumbnails();
                    RebuildList();
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

                // Toggle expand/collapse
                bool expanded = false;
                summaryRow.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target is Button) return;
                    expanded = !expanded;
                    detail.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    expandLabel.text = expanded ? "\u25bc" : "\u25b6";
                });

                wrapper.Add(summaryRow);

                // Detail fields
                var timeField = new FloatField("Time") { value = kf.Time };
                timeField.isDelayed = true;
                timeField.RegisterValueChangedCallback(e =>
                {
                    kf.Time = Mathf.Max(0f, e.newValue);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveWaveData(waveLayer);
                    _trackArea.InvalidateThumbnails();
                });
                detail.Add(timeField);

                var xField = new FloatField("X") { value = kf.Position.x };
                xField.isDelayed = true;
                xField.RegisterValueChangedCallback(e =>
                {
                    kf.Position = new Vector3(e.newValue, kf.Position.y, kf.Position.z);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveWaveData(waveLayer);
                    _trackArea.InvalidateThumbnails();
                });
                detail.Add(xField);

                var yField = new FloatField("Y") { value = kf.Position.y };
                yField.isDelayed = true;
                yField.RegisterValueChangedCallback(e =>
                {
                    kf.Position = new Vector3(kf.Position.x, e.newValue, kf.Position.z);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveWaveData(waveLayer);
                    _trackArea.InvalidateThumbnails();
                });
                detail.Add(yField);

                var zField = new FloatField("Z") { value = kf.Position.z };
                zField.isDelayed = true;
                zField.RegisterValueChangedCallback(e =>
                {
                    kf.Position = new Vector3(kf.Position.x, kf.Position.y, e.newValue);
                    summaryText.text = $"KF {idx}: T={kf.Time:F1}  ({kf.Position.x:F1}, {kf.Position.y:F1}, {kf.Position.z:F1})";
                    SaveWaveData(waveLayer);
                    _trackArea.InvalidateThumbnails();
                });
                detail.Add(zField);

                wrapper.Add(detail);
                parent.Add(wrapper);
            }

            var addKfBtn = new Button(() =>
            {
                float lastTime = keyframes.Count > 0 ? keyframes[keyframes.Count - 1].Time + 3f : 0f;
                var lastPos = keyframes.Count > 0 ? keyframes[keyframes.Count - 1].Position : new Vector3(0, 5, 0);
                keyframes.Add(new PathKeyframe { Time = lastTime, Position = lastPos });
                SaveWaveData(waveLayer);
                _trackArea.InvalidateThumbnails();
                RebuildList();
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
                targetIdx = Mathf.Clamp(targetIdx, 0, ids.Count - 1);

                // Execute via CommandStack so undo/redo works
                var cmd = ListCommand<string>.Move(ids, fromIdx, targetIdx,
                    "Reorder Spell Card");
                _commandStack.Execute(cmd);

                // BossFightLayer caches SpellCard data from disk — must reload after reorder.
                // OnCommandStateChanged already calls InvalidateCurrentLayerBlocks + RebuildBlocks,
                // but we need to re-wire callbacks since block instances changed.
                bfLayer.InvalidateBlocks();
                WireLayerToTrackArea(bfLayer);
                _trackArea.RebuildBlocks();
                ShowLayerSummary(_currentLayer);
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
                targetIdx = Mathf.Clamp(targetIdx, 0, segments.Count - 1);

                // Execute via CommandStack so undo/redo works
                var cmd = ListCommand<TimelineSegment>.Move(segments, fromIdx, targetIdx,
                    "Reorder Segment");
                _commandStack.Execute(cmd);

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
            _propertyHeaderLabel.text = $"Wave: {_catalog?.FindWave(evt.WaveId)?.Name ?? evt.WaveId}";
            float layerDuration = _currentLayer?.TotalDuration ?? float.MaxValue;

            var props = new VisualElement();
            props.style.paddingTop = 4;
            props.style.paddingLeft = 8;
            props.style.paddingRight = 8;

            // Start Time
            var startField = new FloatField("Start Time") { value = evt.StartTime };
            startField.isDelayed = true;
            startField.RegisterValueChangedCallback(e =>
            {
                float maxStart = Mathf.Max(0f, layerDuration - evt.Duration);
                var cmd = new PropertyChangeCommand<float>(
                    "Change Start Time",
                    () => evt.StartTime,
                    v => evt.StartTime = v,
                    Mathf.Clamp(e.newValue, 0f, maxStart));
                _commandStack.Execute(cmd);
            });
            props.Add(startField);
            _propStartField = startField;

            // Duration
            var durField = new FloatField("Duration") { value = evt.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                float maxDur = Mathf.Max(0.1f, layerDuration - evt.StartTime);
                var cmd = new PropertyChangeCommand<float>(
                    "Change Duration",
                    () => evt.Duration,
                    v => evt.Duration = v,
                    Mathf.Clamp(e.newValue, 0.1f, maxDur));
                _commandStack.Execute(cmd);
            });
            props.Add(durField);
            _propDurField = durField;

            // Wave ID
            var waveLabel = new Label($"Wave: {_catalog?.FindWave(evt.WaveId)?.Name ?? evt.WaveId}");
            waveLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            waveLabel.style.marginTop = 4;
            props.Add(waveLabel);

            // Rename Wave button
            var renameWaveBtn = new Button(() =>
            {
                var curName = _catalog?.FindWave(evt.WaveId)?.Name ?? "";
                ShowRenameDialog("Wave", curName, newName =>
                {
                    RenameResource("Wave", evt.WaveId, newName);
                    _trackArea.RebuildBlocks();
                    ShowSpawnWaveProperties(evt);
                });
            }) { text = "Rename..." };
            renameWaveBtn.style.marginTop = 2;
            renameWaveBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.2f);
            renameWaveBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            props.Add(renameWaveBtn);

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
            _propertyHeaderLabel.text = $"Event: {_catalog?.FindPattern(evt.PatternId)?.Name ?? evt.PatternId}";
            float layerDuration = _currentLayer?.TotalDuration ?? float.MaxValue;

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
                float maxStart = Mathf.Max(0f, layerDuration - evt.Duration);
                var cmd = new PropertyChangeCommand<float>(
                    "Change Start Time",
                    () => evt.StartTime,
                    v => evt.StartTime = v,
                    Mathf.Clamp(e.newValue, 0f, maxStart));
                _commandStack.Execute(cmd);
            });
            eventProps.Add(startField);
            _propStartField = startField;

            // Duration
            var durField = new FloatField("Duration") { value = evt.Duration };
            durField.isDelayed = true;
            durField.RegisterValueChangedCallback(e =>
            {
                float maxDur = Mathf.Max(0.1f, layerDuration - evt.StartTime);
                var cmd = new PropertyChangeCommand<float>(
                    "Change Duration",
                    () => evt.Duration,
                    v => evt.Duration = v,
                    Mathf.Clamp(e.newValue, 0.1f, maxDur));
                _commandStack.Execute(cmd);
            });
            eventProps.Add(durField);
            _propDurField = durField;

            // Pattern ID
            var patternLabel = new Label($"Pattern: {_catalog?.FindPattern(evt.PatternId)?.Name ?? evt.PatternId}");
            patternLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            patternLabel.style.marginTop = 4;
            eventProps.Add(patternLabel);

            // Rename Pattern button
            var renamePatBtn = new Button(() =>
            {
                var curName = _catalog?.FindPattern(evt.PatternId)?.Name ?? "";
                ShowRenameDialog("Pattern", curName, newName =>
                {
                    RenameResource("Pattern", evt.PatternId, newName);
                    _trackArea.RebuildBlocks();
                    ShowSpawnPatternProperties(evt);
                });
            }) { text = "Rename..." };
            renamePatBtn.style.marginTop = 2;
            renamePatBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.2f);
            renamePatBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            eventProps.Add(renamePatBtn);

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

            // Inline pattern editor if resolved (clone to avoid mutating shared cache)
            if (evt.ResolvedPattern != null && _singlePreviewer != null)
            {
                // Replace with a clone so edits don't pollute the shared PatternLibrary cache
                if (_library != null)
                {
                    var clone = _library.ResolveClone(evt.PatternId);
                    if (clone != null)
                        evt.ResolvedPattern = clone;
                }
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

                // Warn if inline pattern editor will write to original file
                if (GetPatternOverrideContext() == null)
                    eventProps.Add(CreateOriginalFileWarning("Pattern"));

                _singlePreviewer.Pattern = evt.ResolvedPattern;
                _patternEditor = new PatternEditorView(_singlePreviewer, GetPatternOverrideContext());
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
                    ShowLayerSummary(bfLayer);
                    LoadBossFightPreview(segment);
                }
                else
                {
                    var midLayer = childLayer as MidStageLayer;
                    if (midLayer != null)
                        WireLayerToTrackArea(midLayer);
                    _trackArea.SetLayer(childLayer);
                    LoadMidStagePreview(segment);
                    OnSpellCardEditingChanged?.Invoke(null);
                }

                // Update breadcrumb
                _breadcrumbSegment.text = segment.Name;
                _breadcrumbStage.text = _stage.Name;
                RebuildBreadcrumb();
                NotifyWavePlaceholders();
                return;
            }

            // Generic double-click: navigate into child layer
            NavigateTo(childLayer);

            // SpellCardDetailLayer: track editing context for save/override logic
            if (childLayer is SpellCardDetailLayer scLayer)
            {
                _editingSpellCard = scLayer.SpellCard;
                _editingSpellCardId = scLayer.SpellCardId;
                // Set per-instance override context from the source SpellCardBlock
                if (block is SpellCardBlock scBlock)
                    _editingSpellCardInstanceContext = scBlock.InstanceContextId;
                // Find the parent BossFight segment from the navigation stack
                foreach (var entry in _navigationStack)
                {
                    if (entry.Layer is BossFightLayer bf)
                    {
                        _editingBossFightSegment = bf.Segment;
                        break;
                    }
                }

                var sc = scLayer.SpellCard;
                if (sc != null && sc.BossPath != null && sc.BossPath.Count > 0)
                    OnSpellCardEditingChanged?.Invoke(sc);
                else
                    OnSpellCardEditingChanged?.Invoke(null);
            }

            // PatternLayer: activate single previewer for live pattern preview
            if (childLayer is PatternLayer patLayer && patLayer.Pattern != null && _singlePreviewer != null)
            {
                _singlePreviewer.Pattern = patLayer.Pattern;
                // Hide boss placeholder — we're previewing a standalone pattern
                OnSpellCardEditingChanged?.Invoke(null);
            }
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

        private void CreateEventWithPatternAndDuration(string patternId, float atTime, float duration)
        {
            var pattern = _library.Resolve(patternId);
            if (pattern == null) return;

            var evt = new SpawnPatternEvent
            {
                Id = $"evt_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                StartTime = atTime,
                Duration = duration,
                PatternId = patternId,
                SpawnPosition = new Vector3(0, 5, 0),
                ResolvedPattern = pattern
            };

            _trackArea.AddEvent(evt);
        }

        private void CreateEventWithWaveAndDuration(string waveId, float atTime, float duration)
        {
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
            else if (_currentLayer is EnemyTypeLayer et) et.InvalidateBlocks();
        }

        private void OnCommandStateChanged()
        {
            // Ensure layer's internal block list is up-to-date before visual rebuild.
            // All layer types may need this after structural commands (add/remove/move).
            InvalidateCurrentLayerBlocks();

            // Rebuild visual elements so labels, colors, and positions all update on undo/redo.
            _trackArea.RebuildBlocks();

            // Refresh preview — structural changes (add/remove pattern, enemy, etc.)
            // need the playback segment rebuilt so 3D bullets and placeholders update.
            if (_currentLayer != null)
                LoadPreviewForLayer(_currentLayer);

            // Refresh the properties panel to sync any value changes from drag/undo/redo.
            var selected = _trackArea.SelectedBlock;
            if (selected != null)
            {
                // MidStageLayer uses legacy OnEventSelected path
                if (_currentLayer is MidStageLayer && selected.DataSource is TimelineEvent te)
                    OnEventSelected(te);
                else
                    OnBlockSelectedGeneric(selected);
            }
            else if (_currentLayer != null)
            {
                ShowLayerSummary(_currentLayer);
            }

            // Auto-save: persist data changes from timeline drag/undo/redo to disk.
            // Attribute panel edits use ExecAndSave (which saves inline), but timeline
            // drags only go through PropertyChangeCommand → _commandStack.Execute,
            // so we need to save here as well.
            AutoSaveCurrentLayer();

            // Refresh wave/enemy placeholders (covers add/remove enemy, undo/redo)
            NotifyWavePlaceholders();

            // Refresh undo/redo button states + history panel
            RefreshUndoRedoButtons();
            RefreshHistoryPanel();
        }

        /// <summary>
        /// Persist the current layer's data to disk after any command execution.
        /// Covers timeline drag (Move/Resize) and undo/redo that bypass ExecAndSave.
        /// </summary>
        private void AutoSaveCurrentLayer()
        {
            if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
            }
            else if (_currentLayer is BossFightLayer bfLayer)
            {
                // A SpellCard block was resized (TimeLimit) or a Transition was resized
                // — save all loaded spell cards in this BossFight
                for (int i = 0; i < bfLayer.BlockCount; i++)
                {
                    var blk = bfLayer.GetBlock(i);
                    if (blk is SpellCardBlock scBlk && blk.DataSource is SpellCard sc)
                        SaveSpellCardInContext(sc, scBlk.SpellCardId);
                }
                // BossFight structure (SpellCardIds list) is part of Stage — save Stage too
                AutoSaveStage();
            }
            else if (_currentLayer is WaveLayer waveLayer)
            {
                SaveWaveData(waveLayer);
            }
            else if (_currentLayer is EnemyTypeLayer etLayer)
            {
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
            }
            else if (_currentLayer is PatternLayer patLayer)
            {
                // Pattern duration changed via resize — save pattern file
                SaveEditedPattern(patLayer.Pattern, patLayer.PatternId, GetPatternOverrideContext());
            }
            else if (_currentLayer is MidStageLayer || _currentLayer is StageLayer)
            {
                // MidStage events (StartTime/Duration) and Stage segments are stored in Stage file
                AutoSaveStage();
            }
        }

        /// <summary>
        /// Explicit save triggered by Ctrl+S or Save button.
        /// Context-aware: saves the current layer's data and shows a brief confirmation flash.
        /// For Stage/MidStage layers, opens the Stage save dialog (existing behavior).
        /// For resource layers (Wave/EnemyType/SpellCard/Pattern), saves immediately.
        /// </summary>
        private void SaveCurrentLayerExplicit()
        {
            if (_currentLayer == null) return;

            string savedWhat = null;

            if (_currentLayer is PatternLayer patLayer)
            {
                SaveEditedPattern(patLayer.Pattern, patLayer.PatternId, GetPatternOverrideContext());
                savedWhat = $"Pattern: {_catalog?.FindPattern(patLayer.PatternId)?.Name ?? patLayer.PatternId}";
            }
            else if (_currentLayer is EnemyTypeLayer etLayer)
            {
                SaveEnemyType(etLayer.EnemyType, etLayer.EnemyTypeId, etLayer.ContextId);
                savedWhat = $"EnemyType: {etLayer.DisplayName}";
            }
            else if (_currentLayer is WaveLayer waveLayer)
            {
                SaveWaveData(waveLayer);
                savedWhat = $"Wave: {waveLayer.DisplayName}";
            }
            else if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
                savedWhat = $"SpellCard: {scLayer.DisplayName}";
            }
            else if (_currentLayer is BossFightLayer bfLayer)
            {
                for (int i = 0; i < bfLayer.BlockCount; i++)
                {
                    var blk = bfLayer.GetBlock(i);
                    if (blk is SpellCardBlock scBlk && blk.DataSource is SpellCard sc)
                        SaveSpellCardInContext(sc, scBlk.SpellCardId, scBlk.InstanceContextId);
                }
                AutoSaveStage();
                savedWhat = $"BossFight: {bfLayer.DisplayName}";
            }
            else
            {
                // Stage / MidStage — open Stage save dialog
                OnSaveStage();
                return;
            }

            if (savedWhat != null)
                ShowSaveFlash(savedWhat);
        }

        /// <summary>
        /// Show a brief green flash on the toolbar to confirm a save operation.
        /// </summary>
        private void ShowSaveFlash(string message)
        {
            var flash = new Label($"\u2713 Saved: {message}");
            flash.style.color = new Color(0.5f, 1f, 0.5f);
            flash.style.fontSize = 10;
            flash.style.marginLeft = 8;
            flash.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toolbar.Add(flash);
            flash.schedule.Execute(() => flash.RemoveFromHierarchy()).StartingIn(2000);
        }

        /// <summary>
        /// "Save As" for the current layer — serialize current in-memory data to a new template file.
        /// Works regardless of Override state (unlike SaveAsNewTemplate which requires an Override file).
        /// For Stage/MidStage, falls back to OnSaveStage dialog.
        /// </summary>
        private void SaveCurrentLayerAs()
        {
            if (_currentLayer == null || _catalog == null) return;

            string resourceType = null;
            string currentName = null;
            System.Func<string, bool> doSave = null;

            if (_currentLayer is PatternLayer patLayer)
            {
                resourceType = "pattern";
                currentName = _catalog.FindPattern(patLayer.PatternId)?.Name ?? patLayer.PatternId;
                doSave = newId =>
                {
                    var path = System.IO.Path.Combine(STGCatalog.PatternsDir, $"{newId}.yaml");
                    if (System.IO.File.Exists(path)) return false;
                    YamlSerializer.SerializeToFile(patLayer.Pattern, path);
                    _catalog.AddOrUpdatePattern(newId, currentName);
                    STGCatalog.Save(_catalog);
                    return true;
                };
            }
            else if (_currentLayer is EnemyTypeLayer etLayer)
            {
                resourceType = "enemytype";
                currentName = etLayer.DisplayName;
                doSave = newId =>
                {
                    var path = System.IO.Path.Combine(STGCatalog.EnemyTypesDir, $"{newId}.yaml");
                    if (System.IO.File.Exists(path)) return false;
                    YamlSerializer.SerializeEnemyTypeToFile(etLayer.EnemyType, path);
                    _catalog.AddOrUpdateEnemyType(newId, currentName);
                    STGCatalog.Save(_catalog);
                    return true;
                };
            }
            else if (_currentLayer is WaveLayer waveLayer)
            {
                resourceType = "wave";
                currentName = waveLayer.DisplayName;
                doSave = newId =>
                {
                    var path = System.IO.Path.Combine(STGCatalog.WavesDir, $"{newId}.yaml");
                    if (System.IO.File.Exists(path)) return false;
                    YamlSerializer.SerializeWaveToFile(waveLayer.Wave, path);
                    _catalog.AddOrUpdateWave(newId, currentName);
                    STGCatalog.Save(_catalog);
                    return true;
                };
            }
            else if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                resourceType = "spellcard";
                currentName = scLayer.DisplayName;
                doSave = newId =>
                {
                    var path = System.IO.Path.Combine(STGCatalog.SpellCardsDir, $"{newId}.yaml");
                    if (System.IO.File.Exists(path)) return false;
                    var yaml = YamlSerializer.SerializeSpellCard(scLayer.SpellCard);
                    System.IO.File.WriteAllText(path, yaml);
                    _catalog.AddOrUpdateSpellCard(newId, currentName);
                    STGCatalog.Save(_catalog);
                    return true;
                };
            }
            else
            {
                // Stage / MidStage / BossFight — use Stage save dialog
                OnSaveStage();
                return;
            }

            // Show "Save As" dialog
            ShowSaveAsDialog(resourceType, currentName, doSave);
        }

        private void ShowSaveAsDialog(string resourceType, string currentName, System.Func<string, bool> doSave)
        {
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
            panel.style.width = 320;

            var title = new Label($"Save {resourceType} As New Template");
            title.style.fontSize = 14;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            var desc = new Label($"Current: {currentName}");
            desc.style.color = new Color(0.7f, 0.7f, 0.7f);
            desc.style.marginBottom = 8;
            panel.Add(desc);

            var nameField = new TextField("Name:") { value = $"{currentName} (copy)" };
            nameField.style.marginBottom = 8;
            panel.Add(nameField);

            var statusLabel = new Label("");
            statusLabel.style.color = new Color(1f, 0.5f, 0.3f);
            statusLabel.style.fontSize = 10;
            statusLabel.style.marginBottom = 4;
            panel.Add(statusLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var saveBtn = new Button(() =>
            {
                var name = nameField.value?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    statusLabel.text = "Name cannot be empty.";
                    return;
                }
                var newId = Guid.NewGuid().ToString("N").Substring(0, 12);
                if (doSave(newId))
                {
                    // Update the name in catalog
                    switch (resourceType.ToLower())
                    {
                        case "pattern":    _catalog.AddOrUpdatePattern(newId, name);    break;
                        case "enemytype":  _catalog.AddOrUpdateEnemyType(newId, name);  break;
                        case "wave":       _catalog.AddOrUpdateWave(newId, name);       break;
                        case "spellcard":  _catalog.AddOrUpdateSpellCard(newId, name);  break;
                    }
                    STGCatalog.Save(_catalog);
                    ShowSaveFlash($"New {resourceType}: {name}");
                    dialog.RemoveFromHierarchy();
                }
                else
                {
                    statusLabel.text = "Failed to save. File may already exist.";
                }
            }) { text = "Save As" };
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

        /// <summary>
        /// Persist the current Stage to disk. Called after any structural or property change
        /// to Segments or MidStage Events (which are stored inside the Stage YAML).
        /// Silently skips if the stage has no known file path (e.g. unsaved new stage).
        /// </summary>
        private void AutoSaveStage()
        {
            if (_stage == null || string.IsNullOrEmpty(_stage.Id) || _catalog == null) return;
            try
            {
                var path = _catalog.GetStagePath(_stage.Id);
                if (path != null && System.IO.File.Exists(path))
                {
                    YamlSerializer.SerializeStageToFile(_stage, path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] AutoSave stage failed: {e.Message}");
            }
        }

        /// <summary>
        /// Called when the embedded PatternEditorView's CommandStack changes.
        /// Refreshes the timeline's active previewer so edits are visible immediately.
        /// </summary>
        private void OnPatternEditorChanged()
        {
            var ctx = GetPatternOverrideContext();

            if (_selectedEvent != null)
            {
                _playback.RefreshEvent(_selectedEvent);

                // MidStage inline edit: save the edited pattern to disk
                SaveEditedPattern(_selectedEvent.ResolvedPattern, _selectedEvent.PatternId, ctx);
            }
            else if (_currentLayer is PatternLayer patLayer)
            {
                // In PatternLayer: reload the temporary segment to pick up pattern changes
                patLayer.LoadPreview(_playback);

                // Save the edited pattern to disk
                SaveEditedPattern(patLayer.Pattern, patLayer.PatternId, ctx);
            }
            else if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                // SpellCard inline pattern edit: save the pattern to disk
                if (_patternEditor != null)
                {
                    var editedPattern = _singlePreviewer?.Pattern;
                    var selectedBlk = _trackArea.SelectedBlock;
                    if (selectedBlk?.DataSource is SpellCardPattern scp && editedPattern != null)
                    {
                        SaveEditedPattern(editedPattern, scp.PatternId, ctx);
                    }
                }
            }
            _trackArea.InvalidateThumbnails();
        }

        /// <summary>
        /// Save an edited BulletPattern to disk.
        /// If contextId is provided, writes to Override (Modified/{contextId}/{patternId}.yaml).
        /// Otherwise writes to the original pattern file (STGData/Patterns/{id}.yaml).
        /// Also refreshes the PatternLibrary cache so other references pick up the change.
        /// </summary>
        private void SaveEditedPattern(BulletPattern pattern, string patternId, string contextId = null)
        {
            if (pattern == null || string.IsNullOrEmpty(patternId) || _catalog == null) return;
            try
            {
                if (!string.IsNullOrEmpty(contextId))
                {
                    var yaml = YamlSerializer.Serialize(pattern);
                    OverrideManager.SaveOverride(contextId, patternId, yaml);
                }
                else
                {
                    var path = _catalog.GetPatternPath(patternId);
                    if (path != null)
                        YamlSerializer.SerializeToFile(pattern, path);
                }
                // Update the shared cache so thumbnails and other references see the change
                _library?.Register(pattern);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimelineEditor] Failed to save pattern '{patternId}': {e.Message}");
            }
        }

        /// <summary>
        /// Find the Pattern Override contextId from the current editing context.
        /// Walks the navigation stack to find the nearest parent with a contextId.
        /// Returns null if no override context (= save to original file).
        /// </summary>
        private string GetPatternOverrideContext()
        {
            // PatternLayer carries its own contextId (set by parent's CreateChildLayer)
            if (_currentLayer is PatternLayer patLayer && !string.IsNullOrEmpty(patLayer.ContextId))
                return patLayer.ContextId;

            // If editing inside a SpellCardDetailLayer, use its contextId
            if (_currentLayer is SpellCardDetailLayer scLayer && !string.IsNullOrEmpty(scLayer.ContextId))
                return scLayer.ContextId;

            // If editing inside a MidStageLayer with a selected event, use per-event-instance context
            if (_currentLayer is MidStageLayer midLayer && !string.IsNullOrEmpty(midLayer.ContextId)
                && _selectedEvent != null)
                return $"{midLayer.ContextId}/{_selectedEvent.Id}";

            // Walk the navigation stack for deeper layers
            foreach (var entry in _navigationStack)
            {
                if (entry.Layer is SpellCardDetailLayer parentSc && !string.IsNullOrEmpty(parentSc.ContextId))
                    return parentSc.ContextId;
                if (entry.Layer is MidStageLayer parentMid && !string.IsNullOrEmpty(parentMid.ContextId))
                    return parentMid.ContextId;
            }

            return null; // No override context — save to original file
        }

        /// <summary>
        /// Create a yellow warning bar indicating the user is editing an original template file.
        /// Add this to the top of property panels when changes go directly to the source file
        /// instead of an Override copy.
        /// </summary>
        private static VisualElement CreateOriginalFileWarning(string resourceType)
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = new Color(0.55f, 0.45f, 0.1f, 0.85f);
            bar.style.paddingLeft = 6;
            bar.style.paddingRight = 6;
            bar.style.paddingTop = 3;
            bar.style.paddingBottom = 3;
            bar.style.marginBottom = 4;
            bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius =
                bar.style.borderBottomLeftRadius = bar.style.borderBottomRightRadius = 3;

            var icon = new Label("\u26a0"); // ⚠
            icon.style.fontSize = 12;
            icon.style.color = new Color(1f, 0.9f, 0.3f);
            icon.style.marginRight = 4;
            bar.Add(icon);

            var text = new Label($"Editing original {resourceType} — changes affect all references");
            text.style.fontSize = 10;
            text.style.color = new Color(1f, 0.95f, 0.7f);
            text.style.whiteSpace = WhiteSpace.Normal;
            bar.Add(text);

            return bar;
        }

        /// <summary>
        /// Create an orange info bar indicating the user is editing an override copy.
        /// </summary>
        private static VisualElement CreateOverrideModeBar(string resourceType)
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = new Color(0.35f, 0.25f, 0.1f, 0.85f);
            bar.style.paddingLeft = 6;
            bar.style.paddingRight = 6;
            bar.style.paddingTop = 3;
            bar.style.paddingBottom = 3;
            bar.style.marginBottom = 4;
            bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius =
                bar.style.borderBottomLeftRadius = bar.style.borderBottomRightRadius = 3;

            var icon = new Label("\u270e"); // ✎
            icon.style.fontSize = 12;
            icon.style.color = new Color(1f, 0.7f, 0.3f);
            icon.style.marginRight = 4;
            bar.Add(icon);

            var text = new Label($"Override mode — {resourceType} changes saved to instance copy");
            text.style.fontSize = 10;
            text.style.color = new Color(1f, 0.85f, 0.6f);
            text.style.whiteSpace = WhiteSpace.Normal;
            bar.Add(text);

            return bar;
        }

        /// <summary>
        /// Create a context-aware status bar for the properties panel.
        /// Shows override mode (orange) or original file warning (yellow) based on contextId.
        /// </summary>
        private static VisualElement CreateSaveContextBar(string resourceType, string contextId)
        {
            return !string.IsNullOrEmpty(contextId)
                ? CreateOverrideModeBar(resourceType)
                : CreateOriginalFileWarning(resourceType);
        }
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

        /// <summary>
        /// Show a dialog when an added asset's duration would exceed the layer's time limit.
        /// User can choose to auto-trim or cancel.
        /// </summary>
        private void ShowDurationOverflowDialog(float assetDuration, float layerDuration,
            float insertTime, Action<float> onTrimConfirmed)
        {
            float overflow = (insertTime + assetDuration) - layerDuration;
            float trimmedDuration = Mathf.Max(0.5f, layerDuration - insertTime);

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0, 0, 0, 0.5f);
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            panel.style.paddingTop = panel.style.paddingBottom = 12;
            panel.style.paddingLeft = panel.style.paddingRight = 16;
            panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
                panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 6;
            panel.style.width = 340;

            var title = new Label("Duration Exceeds Layer Limit");
            title.style.fontSize = 13;
            title.style.color = new Color(1f, 0.85f, 0.4f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            var msg = new Label(
                $"Insert at {insertTime:F1}s + duration {assetDuration:F1}s = {insertTime + assetDuration:F1}s\n" +
                $"Layer limit: {layerDuration:F1}s  (overflow: {overflow:F1}s)\n\n" +
                $"Auto-trim duration to {trimmedDuration:F1}s?");
            msg.style.color = new Color(0.85f, 0.85f, 0.85f);
            msg.style.fontSize = 11;
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.marginBottom = 12;
            panel.Add(msg);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var trimBtn = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onTrimConfirmed?.Invoke(trimmedDuration);
            }) { text = "Auto-Trim" };
            trimBtn.style.backgroundColor = new Color(0.2f, 0.45f, 0.3f);
            trimBtn.style.color = new Color(0.95f, 0.95f, 0.95f);

            var cancelBtn = new Button(() => overlay.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.35f, 0.25f, 0.25f);
            cancelBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            cancelBtn.style.marginLeft = 8;

            btnRow.Add(trimBtn);
            btnRow.Add(cancelBtn);
            panel.Add(btnRow);
            overlay.Add(panel);

            Root.panel.visualTree.Add(overlay);
        }

        /// <summary>
        /// Show a picker dialog to select an EnemyType when adding an enemy to a Wave.
        /// </summary>
        private void ShowEnemyTypePicker(WaveLayer waveLayer)
        {
            if (_catalog == null || _catalog.EnemyTypes.Count == 0)
            {
                Debug.LogWarning("[TimelineEditor] No enemy types in catalog. Create one from the Assets panel first.");
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
                picker.style.borderLeftColor = picker.style.borderRightColor = new Color(0.6f, 0.35f, 0.2f);
            picker.style.paddingTop = picker.style.paddingBottom = 8;
            picker.style.paddingLeft = picker.style.paddingRight = 12;
            picker.style.borderTopLeftRadius = picker.style.borderTopRightRadius =
                picker.style.borderBottomLeftRadius = picker.style.borderBottomRightRadius = 6;
            picker.style.minWidth = 220;
            picker.style.maxHeight = 300;

            var title = new Label("Select Enemy Type");
            title.style.fontSize = 12;
            title.style.color = new Color(1f, 0.6f, 0.3f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            picker.Add(title);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 220;

            foreach (var entry in _catalog.EnemyTypes)
            {
                string label = !string.IsNullOrEmpty(entry.Name)
                    ? $"{entry.Name}  ({entry.Id})"
                    : entry.Id;

                var typeId = entry.Id;
                var btn = new Button(() =>
                {
                    picker.RemoveFromHierarchy();
                    AddEnemyInstanceToWave(waveLayer, typeId);
                }) { text = label };
                btn.style.backgroundColor = new Color(0.3f, 0.22f, 0.15f);
                btn.style.color = new Color(0.9f, 0.9f, 0.9f);
                btn.style.marginBottom = 2;
                scroll.Add(btn);
            }

            picker.Add(scroll);

            var cancelBtn = new Button(() => picker.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn.style.marginTop = 4;
            picker.Add(cancelBtn);

            Root.panel.visualTree.Add(picker);
            ApplyLightTextTheme(picker);
        }

        private void AddEnemyInstanceToWave(WaveLayer waveLayer, string enemyTypeId)
        {
            float spawnDelay = _playback.CurrentTime;
            float defaultPathDur = 3f;
            float waveDur = waveLayer.TotalDuration;

            var enemy = new EnemyInstance
            {
                EnemyTypeId = enemyTypeId,
                SpawnDelay = Mathf.Min(spawnDelay, Mathf.Max(0f, waveDur - 0.5f)),
                Path = new List<PathKeyframe>
                {
                    new() { Time = 0f, Position = new Vector3(0, 5, 0) },
                    new() { Time = defaultPathDur, Position = new Vector3(0, -5, 0) }
                }
            };

            // Check overflow: if spawn + path duration exceeds wave duration
            float endTime = enemy.SpawnDelay + defaultPathDur;
            if (endTime > waveDur && waveDur > 0)
            {
                ShowDurationOverflowDialog(defaultPathDur, waveDur, enemy.SpawnDelay, trimmedDur =>
                {
                    // Adjust last keyframe time to trimmed duration
                    if (enemy.Path.Count > 1)
                        enemy.Path[enemy.Path.Count - 1] = new PathKeyframe
                        {
                            Time = trimmedDur,
                            Position = enemy.Path[enemy.Path.Count - 1].Position
                        };
                    var cmd = ListCommand<EnemyInstance>.Add(
                        waveLayer.Wave.Enemies, enemy, -1, "Add Enemy Instance");
                    _commandStack.Execute(cmd);
                    waveLayer.InvalidateBlocks();
                    OnStageDataChanged();
                    SaveWaveData(waveLayer);
                });
                return;
            }

            var cmd2 = ListCommand<EnemyInstance>.Add(
                waveLayer.Wave.Enemies, enemy, -1, "Add Enemy Instance");
            _commandStack.Execute(cmd2);
            waveLayer.InvalidateBlocks();
            OnStageDataChanged();
            SaveWaveData(waveLayer);
        }

        // ═══════════════════════════════════════════════════════════════
        // ── Keyboard Shortcuts ──
        // ═══════════════════════════════════════════════════════════════

        private void OnKeyDown(KeyDownEvent evt)
        {
            // Player mode: swallow ALL key events so they don't reach UI elements
            if (SuppressShortcuts)
            {
                evt.StopPropagation();
                evt.PreventDefault();
                return;
            }

            bool ctrl = evt.ctrlKey || evt.commandKey;
            bool shift = evt.shiftKey;

            if (HandleKeyboardShortcut(evt.keyCode, ctrl, shift))
            {
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        /// <summary>
        /// When true, all keyboard shortcuts are suppressed.
        /// Set by PatternSandboxSetup when player mode is active.
        /// </summary>
        public bool SuppressShortcuts { get; set; }

        /// <summary>
        /// Process a keyboard shortcut. Returns true if the key was handled.
        /// Called from OnKeyDown (UI Toolkit focus) and from PatternSandboxSetup.Update
        /// (global Input polling) so shortcuts work even when the scene viewport has focus.
        /// </summary>
        public bool HandleKeyboardShortcut(KeyCode keyCode, bool ctrl, bool shift)
        {
            if (SuppressShortcuts) return false;
            // Ctrl+Z → Undo
            if (ctrl && !shift && keyCode == KeyCode.Z)
            {
                var stack = _patternEditor?.Commands ?? _commandStack;
                if (stack.CanUndo) stack.Undo();
                return true;
            }

            // Ctrl+Y or Ctrl+Shift+Z → Redo
            if ((ctrl && keyCode == KeyCode.Y) ||
                (ctrl && shift && keyCode == KeyCode.Z))
            {
                var stack = _patternEditor?.Commands ?? _commandStack;
                if (stack.CanRedo) stack.Redo();
                return true;
            }

            // Delete / Backspace → Delete selected block
            if (!ctrl && (keyCode == KeyCode.Delete || keyCode == KeyCode.Backspace))
            {
                DeleteSelectedBlock();
                return true;
            }

            // Space → Toggle play
            if (!ctrl && keyCode == KeyCode.Space)
            {
                OnTogglePlay();
                return true;
            }

            // Ctrl+Shift+S → Save As (new template from current layer)
            if (ctrl && shift && keyCode == KeyCode.S)
            {
                SaveCurrentLayerAs();
                return true;
            }

            // Ctrl+S → Context-aware save
            if (ctrl && !shift && keyCode == KeyCode.S)
            {
                SaveCurrentLayerExplicit();
                return true;
            }

            // Ctrl+C → Copy
            if (ctrl && keyCode == KeyCode.C)
            {
                CopySelectedBlock();
                return true;
            }

            // Ctrl+V → Paste
            if (ctrl && keyCode == KeyCode.V)
            {
                PasteBlock();
                return true;
            }

            // Ctrl+D → Duplicate
            if (ctrl && keyCode == KeyCode.D)
            {
                DuplicateSelectedBlock();
                return true;
            }

            return false;
        }

        private void DeleteSelectedBlock()
        {
            var selected = _trackArea.SelectedBlock;
            if (selected == null) return;

            if (_currentLayer is MidStageLayer)
            {
                _trackArea.DeleteSelectedEvent();
            }
            else if (_currentLayer is BossFightLayer bfLayer)
            {
                bfLayer.OnDeleteSpellCardRequested?.Invoke(selected);
            }
            else if (_currentLayer is WaveLayer waveLayer)
            {
                waveLayer.OnDeleteEnemyRequested?.Invoke(selected);
            }
            else if (_currentLayer is SpellCardDetailLayer scLayer)
            {
                scLayer.OnDeletePatternRequested?.Invoke(selected);
            }
            else if (_currentLayer is StageLayer)
            {
                _stageLayer.OnDeleteSegmentRequested?.Invoke(selected);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ── Copy / Paste / Duplicate ──
        // ═══════════════════════════════════════════════════════════════

        private void CopySelectedBlock()
        {
            var selected = _trackArea.SelectedBlock;
            if (selected?.DataSource == null) return;

            var data = selected.DataSource;
            string yaml = null;

            if (data is TimelineEvent te)
                yaml = YamlSerializer.SerializeStage(new Stage { Segments = new List<TimelineSegment> { new() { Events = new List<TimelineEvent> { te } } } });
            else if (data is EnemyInstance ei)
                yaml = YamlSerializer.SerializeWave(new Wave { Enemies = new List<EnemyInstance> { ei } });
            else if (data is SpellCardPattern scp)
                yaml = YamlSerializer.SerializeSpellCard(new SpellCard { Patterns = new List<SpellCardPattern> { scp } });

            if (yaml != null)
            {
                _clipboard = (data.GetType(), yaml);
                Debug.Log($"[Timeline] Copied {data.GetType().Name}");
            }
        }

        private void PasteBlock()
        {
            if (_clipboard == null) return;
            var (blockType, yamlData) = _clipboard.Value;
            float pasteTime = _playback.CurrentTime;

            if (typeof(TimelineEvent).IsAssignableFrom(blockType) && _currentLayer is MidStageLayer midLayer)
            {
                var tempStage = YamlSerializer.DeserializeStage((string)yamlData);
                var srcEvt = tempStage?.Segments?[0]?.Events?[0];
                if (srcEvt == null) return;
                var clone = CloneTimelineEvent(srcEvt, pasteTime);
                midLayer.Segment.Events.Add(clone);
                midLayer.InvalidateBlocks();
                OnStageDataChanged();
            }
            else if (blockType == typeof(EnemyInstance) && _currentLayer is WaveLayer waveLayer)
            {
                var tempWave = YamlSerializer.DeserializeWave((string)yamlData);
                var srcEi = tempWave?.Enemies?[0];
                if (srcEi == null) return;
                var clone = CloneEnemyInstance(srcEi, pasteTime);
                waveLayer.Wave.Enemies.Add(clone);
                waveLayer.InvalidateBlocks();
                OnStageDataChanged();
                SaveWaveData(waveLayer);
            }
            else if (blockType == typeof(SpellCardPattern) && _currentLayer is SpellCardDetailLayer scLayer)
            {
                var tempSc = YamlSerializer.DeserializeSpellCard((string)yamlData);
                var srcScp = tempSc?.Patterns?[0];
                if (srcScp == null) return;
                var clone = new SpellCardPattern
                {
                    PatternId = srcScp.PatternId,
                    Delay = pasteTime,
                    Duration = srcScp.Duration,
                    Offset = srcScp.Offset
                };
                scLayer.SpellCard.Patterns.Add(clone);
                scLayer.InvalidateBlocks();
                OnStageDataChanged();
                SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
            }
        }

        private void DuplicateSelectedBlock()
        {
            var selected = _trackArea.SelectedBlock;
            if (selected?.DataSource == null) return;

            var data = selected.DataSource;
            float offset = 0.5f;

            if (data is TimelineEvent te && _currentLayer is MidStageLayer midLayer)
            {
                var clone = CloneTimelineEvent(te, te.StartTime + offset);
                midLayer.Segment.Events.Add(clone);
                midLayer.InvalidateBlocks();
                OnStageDataChanged();
            }
            else if (data is EnemyInstance ei && _currentLayer is WaveLayer waveLayer)
            {
                var clone = CloneEnemyInstance(ei, ei.SpawnDelay + offset);
                waveLayer.Wave.Enemies.Add(clone);
                waveLayer.InvalidateBlocks();
                OnStageDataChanged();
                SaveWaveData(waveLayer);
            }
            else if (data is SpellCardPattern scp && _currentLayer is SpellCardDetailLayer scLayer)
            {
                var clone = new SpellCardPattern
                {
                    PatternId = scp.PatternId,
                    Delay = scp.Delay + offset,
                    Duration = scp.Duration,
                    Offset = scp.Offset
                };
                scLayer.SpellCard.Patterns.Add(clone);
                scLayer.InvalidateBlocks();
                OnStageDataChanged();
                SaveSpellCardInContext(scLayer.SpellCard, scLayer.SpellCardId);
            }
        }

        private static TimelineEvent CloneTimelineEvent(TimelineEvent src, float newStartTime)
        {
            TimelineEvent clone;
            if (src is SpawnPatternEvent spe)
            {
                clone = new SpawnPatternEvent
                {
                    PatternId = spe.PatternId,
                    Duration = spe.Duration,
                    SpawnPosition = spe.SpawnPosition
                };
            }
            else if (src is SpawnWaveEvent swe)
            {
                clone = new SpawnWaveEvent
                {
                    WaveId = swe.WaveId,
                    Duration = swe.Duration
                };
            }
            else
            {
                // TimelineEvent is abstract; fallback to SpawnPatternEvent
                clone = new SpawnPatternEvent { Duration = src.Duration };
            }
            clone.Id = Guid.NewGuid().ToString("N")[..8];
            clone.StartTime = newStartTime;
            return clone;
        }

        private static EnemyInstance CloneEnemyInstance(EnemyInstance src, float newSpawnDelay)
        {
            return new EnemyInstance
            {
                EnemyTypeId = src.EnemyTypeId,
                SpawnDelay = newSpawnDelay,
                Path = src.Path != null ? new List<PathKeyframe>(src.Path.Select(k =>
                    new PathKeyframe { Time = k.Time, Position = k.Position })) : null
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // ── Undo/Redo Buttons ──
        // ═══════════════════════════════════════════════════════════════

        private void OnUndoClicked()
        {
            var stack = _patternEditor?.Commands ?? _commandStack;
            if (stack.CanUndo) stack.Undo();
        }

        private void OnRedoClicked()
        {
            var stack = _patternEditor?.Commands ?? _commandStack;
            if (stack.CanRedo) stack.Redo();
        }

        private void RefreshUndoRedoButtons()
        {
            var stack = _patternEditor?.Commands ?? _commandStack;
            _undoBtn?.SetEnabled(stack.CanUndo);
            _redoBtn?.SetEnabled(stack.CanRedo);
        }

        // ═══════════════════════════════════════════════════════════════
        // ── Command History Panel ──
        // ═══════════════════════════════════════════════════════════════

        private void ToggleHistoryPanel()
        {
            _historyVisible = !_historyVisible;
            if (_historyVisible)
                ShowHistoryPanel();
            else
                HideHistoryPanel();
        }

        private void ShowHistoryPanel()
        {
            if (_historyPanel != null)
            {
                _historyPanel.style.display = DisplayStyle.Flex;
                RefreshHistoryPanel();
                return;
            }

            _historyPanel = new VisualElement();
            _historyPanel.style.position = Position.Absolute;
            _historyPanel.style.left = 0;
            _historyPanel.style.right = 0;
            _historyPanel.style.top = 30; // below toolbar
            _historyPanel.style.maxHeight = 200;
            _historyPanel.style.backgroundColor = new Color(0.13f, 0.13f, 0.15f, 0.96f);
            _historyPanel.style.borderBottomWidth = 1;
            _historyPanel.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f);
            _historyPanel.style.paddingTop = 4;
            _historyPanel.style.paddingBottom = 4;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.marginBottom = 4;

            var titleLabel = new Label("Command History");
            titleLabel.style.color = Lt;
            titleLabel.style.fontSize = 11;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.flexGrow = 1;
            header.Add(titleLabel);

            var closeBtn = new Button(() => { _historyVisible = false; HideHistoryPanel(); }) { text = "\u2715" };
            closeBtn.style.width = 20;
            closeBtn.style.height = 18;
            closeBtn.style.fontSize = 10;
            closeBtn.style.color = Lt;
            closeBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            closeBtn.style.borderTopWidth = closeBtn.style.borderBottomWidth =
                closeBtn.style.borderLeftWidth = closeBtn.style.borderRightWidth = 0;
            header.Add(closeBtn);

            _historyPanel.Add(header);

            _historyScroll = new ScrollView(ScrollViewMode.Vertical);
            _historyScroll.style.flexGrow = 1;
            _historyScroll.style.maxHeight = 170;
            _historyPanel.Add(_historyScroll);

            Root.Add(_historyPanel);
            RefreshHistoryPanel();
            ApplyLightTextTheme(_historyPanel);
        }

        private void HideHistoryPanel()
        {
            if (_historyPanel != null)
                _historyPanel.style.display = DisplayStyle.None;
        }

        private void RefreshHistoryPanel()
        {
            if (_historyScroll == null || !_historyVisible) return;

            _historyScroll.Clear();

            var undoHistory = _commandStack.UndoHistory;
            var redoHistory = _commandStack.RedoHistory;

            // Undo items (oldest first, newest at bottom) — gray text
            for (int i = 0; i < undoHistory.Count; i++)
            {
                int targetUndoCount = undoHistory.Count - i; // how many undos to reach this position
                var item = BuildHistoryItem(undoHistory[i].Description, false, targetUndoCount);
                _historyScroll.Add(item);
            }

            // Current position indicator
            var currentLine = new VisualElement();
            currentLine.style.flexDirection = FlexDirection.Row;
            currentLine.style.alignItems = Align.Center;
            currentLine.style.height = 18;
            currentLine.style.paddingLeft = 8;

            var greenBar = new VisualElement();
            greenBar.style.height = 2;
            greenBar.style.flexGrow = 1;
            greenBar.style.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            currentLine.Add(greenBar);

            var currentLabel = new Label("\u25b8 Current");
            currentLabel.style.color = new Color(0.3f, 0.8f, 0.3f);
            currentLabel.style.fontSize = 10;
            currentLabel.style.marginLeft = 4;
            currentLine.Add(currentLabel);

            _historyScroll.Add(currentLine);

            // Redo items (oldest first) — dimmer text + strikethrough style
            for (int i = 0; i < redoHistory.Count; i++)
            {
                int targetRedoCount = i + 1; // how many redos to reach this position
                var item = BuildHistoryItem(redoHistory[i].Description, true, targetRedoCount);
                _historyScroll.Add(item);
            }

            // Auto-scroll to current position
            _historyScroll.schedule.Execute(() =>
            {
                _historyScroll.scrollOffset = new Vector2(0, _historyScroll.scrollOffset.y + 9999);
                // Scroll back a bit to show current indicator
                float targetY = Mathf.Max(0, (undoHistory.Count + 0.5f) * 20f - _historyScroll.resolvedStyle.height * 0.5f);
                _historyScroll.scrollOffset = new Vector2(0, targetY);
            }).ExecuteLater(10);
        }

        private VisualElement BuildHistoryItem(string description, bool isRedo, int stepsToReach)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.height = 20;
            item.style.paddingLeft = 12;
            item.style.paddingRight = 8;

            var label = new Label(description ?? "(unnamed)");
            label.style.fontSize = 10;
            label.style.flexGrow = 1;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;

            if (isRedo)
            {
                label.style.color = new Color(0.5f, 0.5f, 0.5f);
                // Simulate strikethrough with a line overlay
                var strike = new VisualElement();
                strike.style.position = Position.Absolute;
                strike.style.left = 12;
                strike.style.right = 8;
                strike.style.top = 10;
                strike.style.height = 1;
                strike.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                item.Add(strike);
            }
            else
            {
                label.style.color = new Color(0.7f, 0.7f, 0.7f);
            }

            item.Add(label);

            // Hover highlight
            item.RegisterCallback<MouseEnterEvent>(_ =>
                item.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 0.5f));
            item.RegisterCallback<MouseLeaveEvent>(_ =>
                item.style.backgroundColor = StyleKeyword.Null);

            // Click to jump to this position
            int steps = stepsToReach;
            bool redo = isRedo;
            item.RegisterCallback<ClickEvent>(_ =>
            {
                if (redo)
                {
                    for (int j = 0; j < steps && _commandStack.CanRedo; j++)
                        _commandStack.Redo();
                }
                else
                {
                    for (int j = 0; j < steps && _commandStack.CanUndo; j++)
                        _commandStack.Undo();
                }
            });

            return item;
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
