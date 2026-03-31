using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Timeline;
using STGEngine.Editor.Commands;
using STGEngine.Editor.UI.Timeline.Layers;

namespace STGEngine.Editor.UI.Timeline
{
    public class TrackAreaView : IDisposable
    {
        public VisualElement Root { get; }

        // ─── New generic events ───
        public event Action<ITimelineBlock> OnBlockSelected;
        public event Action OnBlocksChanged;
        public event Action<ITimelineBlock> OnBlockValuesChanged;
        public event Action<ITimelineBlock> OnBlockDoubleClicked;
        public event Action<float> OnSeekRequested;
        /// <summary>Raised when a block is dragged to reorder in a sequential layer. Args: draggedBlock, dropTimeInSeconds.</summary>
        public event Action<ITimelineBlock, float> OnBlockReorderRequested;

        // ─── Legacy events (kept for backward compat during migration) ───
        public event Action<TimelineEvent> OnEventSelected;
        public event Action OnEventsChanged;
        public event Action<TimelineEvent> OnEventValuesChanged;
        public event Action<float> OnAddEventRequested;
        public event Action<float> OnAddWaveEventRequested;

        private ITimelineLayer _layer;
        private readonly CommandStack _commandStack;

        private readonly VisualElement _rulerArea;
        private readonly VisualElement _trackContent;
        private readonly VisualElement _playheadLine;
        private readonly VisualElement _playheadRulerMarker;

        // ── Parent duration limit line ──
        // When navigating into a child layer (e.g. Stage → MidStage → SpellCard → Pattern),
        // this white vertical line marks the time boundary inherited from the parent block.
        // It tells the designer "content beyond this line exceeds the parent's allocated time".
        //
        // CONVENTION FOR NEW LAYERS:
        //   When implementing NavigateTo / OnBlockDoubleClicked for a new layer type,
        //   call _trackArea.SetParentDuration(block.Duration) after entering the child.
        //   When returning (NavigateBack / NavigateToDepth), the parent duration is
        //   automatically restored from the navigation stack or cleared at root level.
        //   If the child layer uses TimeOrigin, the line accounts for it automatically.
        private readonly VisualElement _parentDurationLine;
        private float _parentDuration = -1f; // <0 means hidden

        // ── Time hints ──
        private readonly Label _playheadTimeLabel;           // red bubble on playhead
        private readonly Label _hoverTimeLabel;              // grey tooltip following mouse
        private readonly List<Label> _rulerLabelPool = new();// reusable labels for ruler ticks
        private const int RulerLabelPoolSize = 24;

        private float _pixelsPerSecond = 60f;
        private float _scrollOffset;
        private const float MinPPS = 15f;
        private const float MaxPPS = 300f;

        // Edge-scroll during drag
        private const float EdgeScrollMargin = 30f;  // px from edge to trigger scroll
        private const float EdgeScrollSpeed = 200f;   // px/sec base scroll speed
        private const long EdgeScrollIntervalMs = 30;  // timer tick interval
        private IVisualElementScheduledItem _edgeScrollTimer;
        private float _lastEdgeScrollMouseX;           // track area local X of last mouse pos

        private readonly List<BlockInfo> _blocks = new();
        private ITimelineBlock _selectedBlock;

        // Active hover popups — tracked so they can be dismissed on click/rebuild
        private readonly List<VisualElement> _activePopups = new();

        private enum DragMode { None, Move, Resize, Scrub, Reorder }
        private DragMode _dragMode;
        private BlockInfo _dragBlockInfo;
        private float _dragStartMouseX;
        private float _dragStartValue;
        private int _reorderOriginalIndex; // original index in _blocks for reorder drag

        private const float TrackRowHeight = 34f;
        private const float TrackPadding = 4f;
        private float _currentPlayTime;

        // Double-click: track which block was last clicked
        private ITimelineBlock _lastClickedBlock;

        public float SnapPlayheadThreshold { get; set; }
        public float SnapGridSize { get; set; }

        /// <summary>
        /// Provider for blocking progress. Returns (isBlocked, blockingEventId, progress 0→1).
        /// Injected by TimelineEditorView to drive the green progress line inside blocking ActionBlocks.
        /// </summary>
        public Func<(bool isBlocked, string eventId, float progress)> BlockingProgressProvider { get; set; }

        private struct BlockInfo
        {
            public ITimelineBlock Block;
            public VisualElement Element;
            public VisualElement ResizeHandle;
            public int Row;
        }

        // ─── Constructor ───

        public TrackAreaView(CommandStack commandStack)
        {
            _commandStack = commandStack;

            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 0.95f);

            // Time ruler
            _rulerArea = new VisualElement();
            _rulerArea.style.height = 24;
            _rulerArea.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            _rulerArea.style.borderBottomWidth = 1;
            _rulerArea.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            _rulerArea.generateVisualContent += DrawRuler;
            _rulerArea.RegisterCallback<MouseDownEvent>(OnRulerMouseDown);
            Root.Add(_rulerArea);

            // Playhead marker on ruler
            _playheadRulerMarker = new VisualElement();
            _playheadRulerMarker.style.position = Position.Absolute;
            _playheadRulerMarker.style.width = 8;
            _playheadRulerMarker.style.height = 8;
            _playheadRulerMarker.style.bottom = 0;
            _playheadRulerMarker.style.backgroundColor = new Color(1f, 0.2f, 0.2f);
            _playheadRulerMarker.style.borderTopLeftRadius = _playheadRulerMarker.style.borderTopRightRadius =
                _playheadRulerMarker.style.borderBottomLeftRadius = _playheadRulerMarker.style.borderBottomRightRadius = 4;
            _rulerArea.Add(_playheadRulerMarker);

            // Playhead time bubble — red label above the playhead marker
            _playheadTimeLabel = new Label("0.00");
            _playheadTimeLabel.style.position = Position.Absolute;
            _playheadTimeLabel.style.bottom = 8;
            _playheadTimeLabel.style.fontSize = 9;
            _playheadTimeLabel.style.color = Color.white;
            _playheadTimeLabel.style.backgroundColor = new Color(0.85f, 0.15f, 0.15f, 0.9f);
            _playheadTimeLabel.style.paddingLeft = 3;
            _playheadTimeLabel.style.paddingRight = 3;
            _playheadTimeLabel.style.paddingTop = 1;
            _playheadTimeLabel.style.paddingBottom = 1;
            _playheadTimeLabel.style.borderTopLeftRadius = _playheadTimeLabel.style.borderTopRightRadius =
                _playheadTimeLabel.style.borderBottomLeftRadius = _playheadTimeLabel.style.borderBottomRightRadius = 3;
            _playheadTimeLabel.pickingMode = PickingMode.Ignore;
            _rulerArea.Add(_playheadTimeLabel);

            // Ruler tick labels — pre-allocated pool
            for (int i = 0; i < RulerLabelPoolSize; i++)
            {
                var lbl = new Label();
                lbl.style.position = Position.Absolute;
                lbl.style.top = 1;
                lbl.style.fontSize = 9;
                lbl.style.color = new Color(0.6f, 0.6f, 0.6f);
                lbl.style.unityTextAlign = TextAnchor.UpperLeft;
                lbl.pickingMode = PickingMode.Ignore;
                lbl.style.display = DisplayStyle.None;
                _rulerArea.Add(lbl);
                _rulerLabelPool.Add(lbl);
            }

            // Track content area
            _trackContent = new VisualElement();
            _trackContent.style.flexGrow = 1;
            _trackContent.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            _trackContent.style.overflow = Overflow.Hidden;
            _trackContent.RegisterCallback<WheelEvent>(OnWheel);
            _trackContent.RegisterCallback<MouseDownEvent>(OnTrackRightClick);
            Root.Add(_trackContent);

            // Playhead line in track
            _playheadLine = new VisualElement();
            _playheadLine.style.position = Position.Absolute;
            _playheadLine.style.width = 2;
            _playheadLine.style.top = 0;
            _playheadLine.style.bottom = 0;
            _playheadLine.style.backgroundColor = new Color(1f, 0.2f, 0.2f);
            _trackContent.Add(_playheadLine);

            // Parent duration limit line (white, initially hidden)
            _parentDurationLine = new VisualElement();
            _parentDurationLine.style.position = Position.Absolute;
            _parentDurationLine.style.width = 1;
            _parentDurationLine.style.top = 0;
            _parentDurationLine.style.bottom = 0;
            _parentDurationLine.style.backgroundColor = new Color(1f, 1f, 1f, 0.5f);
            _parentDurationLine.pickingMode = PickingMode.Ignore;
            _parentDurationLine.style.display = DisplayStyle.None;
            _trackContent.Add(_parentDurationLine);

            // Hover time tooltip — follows mouse in track area
            _hoverTimeLabel = new Label();
            _hoverTimeLabel.style.position = Position.Absolute;
            _hoverTimeLabel.style.fontSize = 10;
            _hoverTimeLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _hoverTimeLabel.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f, 0.85f);
            _hoverTimeLabel.style.paddingLeft = 4;
            _hoverTimeLabel.style.paddingRight = 4;
            _hoverTimeLabel.style.paddingTop = 2;
            _hoverTimeLabel.style.paddingBottom = 2;
            _hoverTimeLabel.style.borderTopLeftRadius = _hoverTimeLabel.style.borderTopRightRadius =
                _hoverTimeLabel.style.borderBottomLeftRadius = _hoverTimeLabel.style.borderBottomRightRadius = 3;
            _hoverTimeLabel.pickingMode = PickingMode.Ignore;
            _hoverTimeLabel.style.display = DisplayStyle.None;
            _trackContent.Add(_hoverTimeLabel);

            // Mouse move/leave on track for hover time
            _trackContent.RegisterCallback<MouseMoveEvent>(OnTrackMouseMove);
            _trackContent.RegisterCallback<MouseLeaveEvent>(_ => _hoverTimeLabel.style.display = DisplayStyle.None);

            // Mouse move/leave on ruler for hover time
            _rulerArea.RegisterCallback<MouseMoveEvent>(OnRulerHoverMove);
            _rulerArea.RegisterCallback<MouseLeaveEvent>(_ => _hoverTimeLabel.style.display = DisplayStyle.None);

            TimelineEditorView.RegisterThemeOverride(Root);
        }

        // ─── Layer Binding ───

        public void SetLayer(ITimelineLayer layer)
        {
            _layer = layer;
            _selectedBlock = null;
            RebuildBlocks();
        }

        /// <summary>
        /// Set the parent layer's time limit. A white vertical line is drawn at this time
        /// to indicate the boundary inherited from the parent block.
        /// Pass a negative value to hide the line (e.g. at root level).
        /// </summary>
        public void SetParentDuration(float duration)
        {
            _parentDuration = duration;
            bool visible = duration >= 0f;
            _parentDurationLine.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) UpdateParentDurationLine();
        }

        /// <summary>
        /// Override the layer reference without rebuilding blocks.
        /// Used when blocks are already correct (e.g., from SetSegment) but the
        /// context menu / interaction layer needs to be different.
        /// </summary>
        public void OverrideLayerReference(ITimelineLayer layer)
        {
            _layer = layer;
        }

        /// <summary>Legacy bridge: wraps a segment in MidStageLayer.</summary>
        public void SetSegment(TimelineSegment segment)
        {
            if (segment == null)
            {
                SetLayer(null);
                return;
            }
            var midLayer = new MidStageLayer(segment);
            // Wire MidStageLayer callbacks to legacy events
            midLayer.OnAddPatternRequested = time => OnAddEventRequested?.Invoke(time);
            midLayer.OnAddWaveRequested = time => OnAddWaveEventRequested?.Invoke(time);
            midLayer.OnDeleteRequested = blk =>
            {
                SelectBlock(blk);
                DeleteSelectedEvent();
            };
            SetLayer(midLayer);
        }

        public void SetPlayTime(float time)
        {
            _currentPlayTime = time;
            UpdatePlayhead();

            // Repaint blocking ActionBlock to update progress line
            if (BlockingProgressProvider != null)
            {
                var (isBlocked, eventId, _) = BlockingProgressProvider();
                if (isBlocked && !string.IsNullOrEmpty(eventId))
                {
                    foreach (var info in _blocks)
                    {
                        if (info.Block is ActionBlock ab && ab.Id == eventId)
                        {
                            info.Element.MarkDirtyRepaint();
                            break;
                        }
                    }
                }
            }
        }

        public float PixelsPerSecond => _pixelsPerSecond;
        public ITimelineBlock SelectedBlock => _selectedBlock;

        /// <summary>Legacy accessor.</summary>
        public TimelineEvent SelectedEvent => _selectedBlock?.DataSource as TimelineEvent;

        public ITimelineLayer CurrentLayer => _layer;

        /// <summary>Dismiss all active hover popups.</summary>
        private void DismissAllPopups()
        {
            foreach (var p in _activePopups)
                p?.RemoveFromHierarchy();
            _activePopups.Clear();
        }

        public void RebuildBlocks()
        {
            DismissAllPopups();
            RecalcSequentialLayoutIfNeeded();

            var toRemove = new List<VisualElement>();
            foreach (var child in _trackContent.Children())
            {
                if (child != _playheadLine && child != _parentDurationLine)
                    toRemove.Add(child);
            }
            foreach (var c in toRemove)
                _trackContent.Remove(c);

            _blocks.Clear();

            if (_layer == null) return;

            var allBlocks = _layer.GetAllBlocks();
            var rows = AssignRows(allBlocks);

            for (int i = 0; i < allBlocks.Count; i++)
            {
                var blk = allBlocks[i];
                int row = rows.TryGetValue(blk, out var r) ? r : 0;
                CreateBlock(blk, row);
            }

            // Restore visual selection highlight after rebuild
            if (_selectedBlock != null)
            {
                bool found = false;
                foreach (var b in _blocks)
                {
                    if (b.Block.Id == _selectedBlock.Id)
                    {
                        _selectedBlock = b.Block;
                        ApplySelectionStyle(b.Element, true);
                        found = true;
                        break;
                    }
                }
                if (!found)
                    _selectedBlock = null;
            }

            UpdateAllBlockPositions();
            UpdatePlayhead();
            _rulerArea.MarkDirtyRepaint();
        }

        public void Dispose()
        {
            _blocks.Clear();
        }

        /// <summary>
        /// Rebuild blocks to refresh all thumbnails with fresh data.
        /// Called when pattern data changes (emitter/modifier edits).
        /// </summary>
        public void InvalidateThumbnails()
        {
            RebuildBlocks();
        }

        // ─── Thumbnail Helpers ───

        /// <summary>
        /// Create a small thumbnail icon with hover-to-enlarge popup.
        /// </summary>
        private VisualElement CreateThumbnailIcon(float size, System.Action<Painter2D, float, float> drawAction)
        {
            var icon = new VisualElement();
            icon.style.width = size;
            icon.style.height = size;
            icon.style.marginLeft = 2;
            icon.style.flexShrink = 0;
            icon.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.6f);
            icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius =
                icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = 2;
            icon.generateVisualContent += ctx =>
            {
                float w = icon.resolvedStyle.width;
                float h = icon.resolvedStyle.height;
                if (w > 0f && h > 0f)
                    drawAction(ctx.painter2D, w, h);
            };

            VisualElement popup = null;
            icon.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (popup != null) return;
                float popupSize = 120f;
                popup = new VisualElement();
                popup.style.position = Position.Absolute;
                popup.style.width = popupSize;
                popup.style.height = popupSize;
                popup.style.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
                popup.style.borderTopWidth = popup.style.borderBottomWidth =
                    popup.style.borderLeftWidth = popup.style.borderRightWidth = 1;
                popup.style.borderTopColor = popup.style.borderBottomColor =
                    popup.style.borderLeftColor = popup.style.borderRightColor = new Color(0.4f, 0.4f, 0.5f);
                popup.style.borderTopLeftRadius = popup.style.borderTopRightRadius =
                    popup.style.borderBottomLeftRadius = popup.style.borderBottomRightRadius = 4;
                popup.pickingMode = PickingMode.Ignore;

                var iconWorld = icon.worldBound;
                popup.style.left = iconWorld.x;
                popup.style.top = iconWorld.y - popupSize - 4;

                popup.generateVisualContent += ctx =>
                {
                    drawAction(ctx.painter2D, popupSize, popupSize);
                };

                Root.panel?.visualTree?.Add(popup);
                _activePopups.Add(popup);
            });
            icon.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (popup != null)
                {
                    popup.RemoveFromHierarchy();
                    _activePopups.Remove(popup);
                    popup = null;
                }
            });

            return icon;
        }

        // ─── Block Creation ───

        private void CreateBlock(ITimelineBlock blk, int row)
        {
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.height = TrackRowHeight - 6;
            // Lower opacity for blocks with thumbnails so child content is more visible
            var bgColor = blk.BlockColor;
            if (blk.HasThumbnail)
                bgColor.a = 0.45f;
            element.style.backgroundColor = bgColor;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 3;
            element.style.borderTopWidth = element.style.borderBottomWidth =
                element.style.borderLeftWidth = element.style.borderRightWidth = 1;
            element.style.borderTopColor = element.style.borderBottomColor =
                element.style.borderLeftColor = element.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);

            // Modified (override) blocks: orange border highlight
            if (blk.IsModified)
            {
                var modColor = new Color(1f, 0.65f, 0.2f, 0.9f);
                element.style.borderTopColor = element.style.borderBottomColor =
                    element.style.borderLeftColor = element.style.borderRightColor = modColor;
                element.style.borderTopWidth = element.style.borderBottomWidth =
                    element.style.borderLeftWidth = element.style.borderRightWidth = 2;
            }

            element.style.overflow = Overflow.Hidden;
            element.style.paddingLeft = 4;
            element.style.justifyContent = Justify.Center;

            // Layout: horizontal row for label + inline thumbnail
            var contentRow = new VisualElement();
            contentRow.style.flexDirection = FlexDirection.Row;
            contentRow.style.alignItems = Align.Center;
            contentRow.style.flexGrow = 1;
            contentRow.style.overflow = Overflow.Hidden;

            var label = new Label(blk.DisplayLabel);
            label.style.color = new Color(0.9f, 0.9f, 0.9f);
            label.style.fontSize = 10;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.flexShrink = 1;
            contentRow.Add(label);

            // Inline thumbnail: small icon after label with hover popup
            if (blk.HasThumbnail && blk.ThumbnailInline)
            {
                float thumbSize = TrackRowHeight - 10;

                // Icon 1: Emitter-only thumbnail
                contentRow.Add(CreateThumbnailIcon(thumbSize, blk.DrawThumbnail));

                // Modifier thumbnails (separate icons)
                if (blk is IModifierThumbnailProvider modProvider && modProvider.HasModifierThumbnails)
                {
                    int modCount = modProvider.ModifierThumbnailCount;

                    // Icon 2: First modifier (single bullet + first modifier)
                    // Hover expands to show all per-modifier thumbnails
                    if (modCount > 0)
                    {
                        var modIcon = new VisualElement();
                        modIcon.style.width = thumbSize;
                        modIcon.style.height = thumbSize;
                        modIcon.style.marginLeft = 2;
                        modIcon.style.flexShrink = 0;
                        modIcon.style.backgroundColor = new Color(0.08f, 0.1f, 0.14f, 0.7f);
                        modIcon.style.borderTopLeftRadius = modIcon.style.borderTopRightRadius =
                            modIcon.style.borderBottomLeftRadius = modIcon.style.borderBottomRightRadius = 2;
                        modIcon.style.borderLeftWidth = modIcon.style.borderRightWidth =
                            modIcon.style.borderTopWidth = modIcon.style.borderBottomWidth = 1;
                        modIcon.style.borderLeftColor = modIcon.style.borderRightColor =
                            modIcon.style.borderTopColor = modIcon.style.borderBottomColor =
                                new Color(0.4f, 0.5f, 0.6f, 0.5f);

                        // Draw first modifier inline
                        var provider = modProvider; // capture for closure
                        modIcon.generateVisualContent += ctx =>
                        {
                            float w = modIcon.resolvedStyle.width;
                            float h = modIcon.resolvedStyle.height;
                            if (w > 0f && h > 0f)
                                provider.DrawModifierThumbnail(ctx.painter2D, w, h, 0);
                        };

                        // Hover: show all per-modifier thumbnails in a horizontal popup
                        VisualElement modPopup = null;
                        modIcon.RegisterCallback<MouseEnterEvent>(_ =>
                        {
                            if (modPopup != null) return;
                            float popupSize = 120f;
                            float popupW = popupSize * modCount + (modCount - 1) * 2f;

                            modPopup = new VisualElement();
                            modPopup.style.position = Position.Absolute;
                            modPopup.style.flexDirection = FlexDirection.Row;
                            modPopup.style.width = popupW;
                            modPopup.style.height = popupSize + 16f; // extra for label
                            modPopup.style.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
                            modPopup.style.borderTopWidth = modPopup.style.borderBottomWidth =
                                modPopup.style.borderLeftWidth = modPopup.style.borderRightWidth = 1;
                            modPopup.style.borderTopColor = modPopup.style.borderBottomColor =
                                modPopup.style.borderLeftColor = modPopup.style.borderRightColor =
                                    new Color(0.4f, 0.4f, 0.5f);
                            modPopup.style.borderTopLeftRadius = modPopup.style.borderTopRightRadius =
                                modPopup.style.borderBottomLeftRadius = modPopup.style.borderBottomRightRadius = 4;
                            modPopup.style.paddingLeft = modPopup.style.paddingRight =
                                modPopup.style.paddingTop = modPopup.style.paddingBottom = 2;
                            modPopup.pickingMode = PickingMode.Ignore;

                            var iconWorld = modIcon.worldBound;
                            modPopup.style.left = iconWorld.x;
                            modPopup.style.top = iconWorld.y - popupSize - 20f;

                            for (int mi = 0; mi < modCount; mi++)
                            {
                                int capturedMi = mi;
                                var cell = new VisualElement();
                                cell.style.width = popupSize;
                                cell.style.flexDirection = FlexDirection.Column;
                                if (mi > 0) cell.style.marginLeft = 2;

                                // Modifier label
                                var modLabel = new Label(provider.GetModifierLabel(mi));
                                modLabel.style.fontSize = 9;
                                modLabel.style.color = new Color(0.7f, 0.8f, 0.9f);
                                modLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                                modLabel.style.height = 14;
                                cell.Add(modLabel);

                                // Thumbnail canvas
                                var canvas = new VisualElement();
                                canvas.style.width = popupSize;
                                canvas.style.height = popupSize;
                                canvas.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 0.8f);
                                canvas.generateVisualContent += ctx =>
                                {
                                    provider.DrawModifierThumbnail(ctx.painter2D, popupSize, popupSize, capturedMi);
                                };
                                cell.Add(canvas);

                                modPopup.Add(cell);
                            }

                            Root.panel?.visualTree?.Add(modPopup);
                            _activePopups.Add(modPopup);
                        });
                        modIcon.RegisterCallback<MouseLeaveEvent>(_ =>
                        {
                            if (modPopup != null)
                            {
                                modPopup.RemoveFromHierarchy();
                                _activePopups.Remove(modPopup);
                                modPopup = null;
                            }
                        });

                        contentRow.Add(modIcon);
                    }

                    // Icon 3: All bullets + all modifiers
                    if (modProvider.HasAllBulletsThumbnail)
                    {
                        contentRow.Add(CreateThumbnailIcon(thumbSize, modProvider.DrawAllBulletsThumbnail));
                    }
                }
            }

            element.Add(contentRow);

            // Background thumbnail (color bars, hatching — drawn underneath content)
            if (blk.HasThumbnail && !blk.ThumbnailInline)
            {
                element.generateVisualContent += ctx =>
                {
                    float w = element.resolvedStyle.width;
                    float h = element.resolvedStyle.height;
                    if (w > 0f && h > 0f)
                        blk.DrawThumbnail(ctx.painter2D, w, h);
                };
                // Ensure repaint after first layout (resolvedStyle may be 0 during initial generateVisualContent)
                element.RegisterCallback<GeometryChangedEvent>(_ => element.MarkDirtyRepaint());
            }

            // DesignEstimate green line (drawn via generateVisualContent)
            float estimate = blk.DesignEstimate;
            if (estimate >= 0f && estimate < blk.Duration)
            {
                element.generateVisualContent += ctx => DrawDesignEstimateLine(ctx, blk);
            }

            // Blocking progress line for ActionBlocks (drawn via generateVisualContent)
            if (blk is ActionBlock actBlk && actBlk.IsBlocking && actBlk.Duration > 0f)
            {
                element.generateVisualContent += ctx => DrawBlockingProgressLine(ctx, actBlk);
            }

            // Resize handle — hide for blocks with read-only Duration (e.g. path-derived)
            var resizeHandle = new VisualElement();
            resizeHandle.style.position = Position.Absolute;
            resizeHandle.style.right = 0;
            resizeHandle.style.top = 0;
            resizeHandle.style.bottom = 0;
            resizeHandle.style.width = 6;
            resizeHandle.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            bool canResize = CanResizeDuration(blk);
            if (!canResize)
                resizeHandle.style.display = DisplayStyle.None;
            element.Add(resizeHandle);

            var info = new BlockInfo
            {
                Block = blk,
                Element = element,
                ResizeHandle = resizeHandle,
                Row = row
            };
            _blocks.Add(info);

            // Left-click: select + start drag
            element.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0 && !IsOverResizeHandle(e, element))
                {
                    // Double-click detection: must be same block as last click
                    if (e.clickCount == 2 && _lastClickedBlock == blk)
                    {
                        _lastClickedBlock = null;
                        OnBlockDoubleClicked?.Invoke(blk);
                        e.StopPropagation();
                        return;
                    }

                    _lastClickedBlock = blk;
                    SelectBlock(blk);
                    if (blk.CanMove)
                    {
                        StartDrag(DragMode.Move, info, e.mousePosition.x);
                    }
                    else if (_layer != null && _layer.IsSequential
                             && !(blk is STGEngine.Editor.UI.Timeline.Layers.TransitionBlock))
                    {
                        // Sequential mode: drag to reorder (skip transition blocks)
                        StartDrag(DragMode.Reorder, info, e.mousePosition.x);
                    }
                    e.StopPropagation();
                }
            });

            // Resize handle drag
            resizeHandle.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0)
                {
                    StartDrag(DragMode.Resize, info, e.mousePosition.x);
                    e.StopPropagation();
                }
            });

            _trackContent.Add(element);
            _parentDurationLine.BringToFront();
            _playheadLine.BringToFront();
        }

        private void DrawDesignEstimateLine(MeshGenerationContext ctx, ITimelineBlock blk)
        {
            float estimate = blk.DesignEstimate;
            if (estimate < 0f || estimate >= blk.Duration) return;

            float blockWidth = blk.Duration * _pixelsPerSecond;
            if (blockWidth <= 0f) return;

            float lineX = (estimate / blk.Duration) * blockWidth;
            float height = TrackRowHeight - 6;

            var painter = ctx.painter2D;

            // Semi-transparent overlay on the right side (past DesignEstimate)
            painter.fillColor = new Color(0f, 0f, 0f, 0.3f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(lineX, 0));
            painter.LineTo(new Vector2(blockWidth, 0));
            painter.LineTo(new Vector2(blockWidth, height));
            painter.LineTo(new Vector2(lineX, height));
            painter.ClosePath();
            painter.Fill();

            // Green vertical line
            painter.strokeColor = new Color(0.2f, 0.9f, 0.3f, 0.9f);
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(lineX, 0));
            painter.LineTo(new Vector2(lineX, height));
            painter.Stroke();
        }

        private void DrawBlockingProgressLine(MeshGenerationContext ctx, ActionBlock actBlk)
        {
            if (BlockingProgressProvider == null) return;
            var (isBlocked, eventId, progress) = BlockingProgressProvider();
            if (!isBlocked || eventId != actBlk.Id || progress <= 0f) return;

            float blockWidth = actBlk.Duration * _pixelsPerSecond;
            if (blockWidth <= 0f) return;

            float lineX = progress * blockWidth;
            float height = TrackRowHeight - 6;

            var painter = ctx.painter2D;

            // Swept green fill (left of progress line)
            painter.fillColor = new Color(0.2f, 0.9f, 0.3f, 0.15f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, 0));
            painter.LineTo(new Vector2(lineX, 0));
            painter.LineTo(new Vector2(lineX, height));
            painter.LineTo(new Vector2(0, height));
            painter.ClosePath();
            painter.Fill();

            // Green vertical progress line
            painter.strokeColor = new Color(0.2f, 0.9f, 0.3f, 0.9f);
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(lineX, 0));
            painter.LineTo(new Vector2(lineX, height));
            painter.Stroke();
        }

        private bool IsOverResizeHandle(MouseDownEvent e, VisualElement element)
        {
            var localPos = element.WorldToLocal(e.mousePosition);
            return localPos.x >= element.resolvedStyle.width - 8;
        }

        // ─── Snap ───

        private float SnapTime(float time)
        {
            float result = time;

            if (SnapGridSize > 0f)
                result = Mathf.Round(time / SnapGridSize) * SnapGridSize;

            if (SnapPlayheadThreshold > 0f && Mathf.Abs(time - _currentPlayTime) <= SnapPlayheadThreshold)
                result = _currentPlayTime;

            return Mathf.Max(0f, result);
        }

        // ─── Drag Logic ───

        private void StartDrag(DragMode mode, BlockInfo info, float mouseX)
        {
            _dragMode = mode;
            _dragBlockInfo = info;
            _dragStartMouseX = mouseX;
            _dragStartValue = mode == DragMode.Move ? info.Block.StartTime : info.Block.Duration;
            _hoverTimeLabel.style.display = DisplayStyle.None; // hide during drag

            _trackContent.RegisterCallback<MouseMoveEvent>(OnDragMove);
            _trackContent.RegisterCallback<MouseUpEvent>(OnDragEnd);
            _trackContent.CaptureMouse();
        }

        private void OnDragMove(MouseMoveEvent e)
        {
            if (_dragMode == DragMode.None) return;

            float deltaX = e.mousePosition.x - _dragStartMouseX;
            float deltaTime = deltaX / _pixelsPerSecond;
            var blk = _dragBlockInfo.Block;

            if (_dragMode == DragMode.Move)
            {
                float maxTime = _layer?.TotalDuration ?? float.MaxValue;
                float rawStart = Mathf.Max(0f, _dragStartValue + deltaTime);
                float duration = blk.Duration;
                float rawEnd = rawStart + duration;

                // Clamp so block end doesn't exceed layer duration
                if (rawEnd > maxTime)
                    rawStart = Mathf.Max(0f, maxTime - duration);

                float snappedStart = SnapTime(rawStart);
                float snappedEnd = SnapTime(rawStart + duration);

                bool startSnapped = Mathf.Abs(snappedStart - rawStart) > 0.0001f;
                bool endSnapped = Mathf.Abs(snappedEnd - (rawStart + duration)) > 0.0001f;

                float newStart;
                if (startSnapped && endSnapped)
                {
                    newStart = Mathf.Abs(snappedStart - rawStart) <= Mathf.Abs(snappedEnd - (rawStart + duration))
                        ? snappedStart
                        : Mathf.Max(0f, snappedEnd - duration);
                }
                else if (startSnapped)
                    newStart = snappedStart;
                else if (endSnapped)
                    newStart = Mathf.Max(0f, snappedEnd - duration);
                else
                    newStart = rawStart;

                // Final clamp: ensure block stays within [0, maxTime]
                newStart = Mathf.Clamp(newStart, 0f, Mathf.Max(0f, maxTime - duration));
                blk.StartTime = newStart;
            }
            else if (_dragMode == DragMode.Resize)
            {
                // In sequential mode, blocks can freely resize (total duration adjusts)
                bool isSequential = _layer?.IsSequential ?? false;
                float maxTime = isSequential ? float.MaxValue : (_layer?.TotalDuration ?? float.MaxValue);
                float rawDuration = Mathf.Max(0.5f, _dragStartValue + deltaTime);
                float startTime = blk.StartTime;
                // Clamp duration so block end doesn't exceed layer duration
                float maxDuration = Mathf.Max(0.5f, maxTime - startTime);
                rawDuration = Mathf.Min(rawDuration, maxDuration);
                float snappedEnd = SnapTime(startTime + rawDuration);
                float newDuration = Mathf.Clamp(snappedEnd - startTime, 0.5f, maxDuration);
                blk.Duration = newDuration;

                // Repaint thumbnail with clip-based rendering during drag
                _dragBlockInfo.Element.MarkDirtyRepaint();
            }
            else if (_dragMode == DragMode.Reorder)
            {
                // Visual feedback: offset the dragged block element horizontally
                float reorderOrigin = _layer?.TimeOrigin ?? 0f;
                float left = (blk.StartTime + reorderOrigin) * _pixelsPerSecond - _scrollOffset + deltaX;
                _dragBlockInfo.Element.style.left = left;
                // Slight vertical lift to indicate dragging
                _dragBlockInfo.Element.style.top = TrackPadding + _dragBlockInfo.Row * TrackRowHeight - 3;
                _dragBlockInfo.Element.style.opacity = 0.7f;
                UpdateEdgeScroll(e.mousePosition);
                return; // skip normal position update
            }

            RecalcSequentialLayoutIfNeeded();
            UpdateAllBlockPositions();
            OnBlockValuesChanged?.Invoke(blk);
            // Legacy bridge
            if (blk.DataSource is TimelineEvent te)
                OnEventValuesChanged?.Invoke(te);

            UpdateEdgeScroll(e.mousePosition);
        }

        private void OnDragEnd(MouseUpEvent e)
        {
            if (_dragMode == DragMode.None) return;

            _trackContent.UnregisterCallback<MouseMoveEvent>(OnDragMove);
            _trackContent.UnregisterCallback<MouseUpEvent>(OnDragEnd);
            _trackContent.ReleaseMouse();

            var blk = _dragBlockInfo.Block;

            if (_dragMode == DragMode.Move)
            {
                float oldVal = _dragStartValue;
                float newVal = blk.StartTime;
                if (Mathf.Abs(oldVal - newVal) > 0.001f)
                {
                    blk.StartTime = oldVal;
                    var cmd = new PropertyChangeCommand<float>(
                        "Move Block",
                        () => blk.StartTime,
                        v => blk.StartTime = v,
                        newVal);
                    _commandStack.Execute(cmd);
                    // Note: OnStateChanged → RefreshBlockPositions handles UI update.
                    // Legacy OnEventValuesChanged for property panel sync:
                    if (blk.DataSource is TimelineEvent moveEvt)
                        OnEventValuesChanged?.Invoke(moveEvt);
                }
            }
            else if (_dragMode == DragMode.Resize)
            {
                float oldVal = _dragStartValue;
                float newVal = blk.Duration;
                if (Mathf.Abs(oldVal - newVal) > 0.001f)
                {
                    blk.Duration = oldVal;
                    var cmd = new PropertyChangeCommand<float>(
                        "Resize Block",
                        () => blk.Duration,
                        v => blk.Duration = v,
                        newVal);
                    _commandStack.Execute(cmd);
                    // Legacy bridge for property panel sync:
                    if (blk.DataSource is TimelineEvent resizeEvt)
                        OnEventValuesChanged?.Invoke(resizeEvt);
                }

                // Trigger thumbnail repaint after resize
                _dragBlockInfo.Element.MarkDirtyRepaint();
            }
            else if (_dragMode == DragMode.Reorder)
            {
                // Reset visual feedback
                _dragBlockInfo.Element.style.opacity = 1f;
                _dragBlockInfo.Element.style.top = TrackPadding + _dragBlockInfo.Row * TrackRowHeight;

                // Ignore if barely moved (treat as a click, not a reorder)
                float dragDist = Mathf.Abs(e.mousePosition.x - _dragStartMouseX);
                if (dragDist < 10f)
                {
                    RecalcSequentialLayoutIfNeeded();
                    UpdateAllBlockPositions();
                    _dragMode = DragMode.None;
                    _dragBlockInfo = default;
                    StopEdgeScroll();
                    return;
                }

                float dropX = e.mousePosition.x + _scrollOffset;
                float dropTime = dropX / _pixelsPerSecond;
                OnBlockReorderRequested?.Invoke(_dragBlockInfo.Block, dropTime);
            }

            _dragMode = DragMode.None;
            _dragBlockInfo = default;
            StopEdgeScroll();
            UpdateAllBlockPositions();
        }

        // ─── Selection ───

        public void SelectBlock(ITimelineBlock blk)
        {
            DismissAllPopups();
            _selectedBlock = blk;
            foreach (var b in _blocks)
            {
                ApplySelectionStyle(b.Element, b.Block == blk);
            }
            OnBlockSelected?.Invoke(blk);
            // Legacy bridge
            OnEventSelected?.Invoke(blk?.DataSource as TimelineEvent);
        }

        /// <summary>Legacy bridge.</summary>
        public void SelectEvent(TimelineEvent evt)
        {
            if (evt == null) { SelectBlock(null); return; }
            foreach (var b in _blocks)
            {
                if (b.Block.DataSource == evt)
                {
                    SelectBlock(b.Block);
                    return;
                }
            }
            SelectBlock(null);
        }

        private void ApplySelectionStyle(VisualElement element, bool selected)
        {
            if (selected)
            {
                element.style.borderTopColor = element.style.borderBottomColor =
                    element.style.borderLeftColor = element.style.borderRightColor =
                        new Color(0.3f, 0.6f, 1f);
                element.style.borderTopWidth = element.style.borderBottomWidth =
                    element.style.borderLeftWidth = element.style.borderRightWidth = 2;
            }
            else
            {
                element.style.borderTopColor = element.style.borderBottomColor =
                    element.style.borderLeftColor = element.style.borderRightColor =
                        new Color(0.4f, 0.4f, 0.4f);
                element.style.borderTopWidth = element.style.borderBottomWidth =
                    element.style.borderLeftWidth = element.style.borderRightWidth = 1;
            }
        }

        // ─── Edge Scroll ───

        /// <summary>
        /// Check if mouse is near the left/right edge of the track area and start/stop
        /// auto-scrolling accordingly. Call from any drag-move handler.
        /// </summary>
        private void UpdateEdgeScroll(Vector2 mouseWorldPos)
        {
            float localX = _trackContent.WorldToLocal(mouseWorldPos).x;
            float trackWidth = _trackContent.resolvedStyle.width;
            _lastEdgeScrollMouseX = localX;

            float scrollDir = 0f;
            if (localX < EdgeScrollMargin)
                scrollDir = -1f;
            else if (localX > trackWidth - EdgeScrollMargin)
                scrollDir = 1f;

            if (scrollDir != 0f)
            {
                if (_edgeScrollTimer == null)
                {
                    _edgeScrollTimer = _trackContent.schedule.Execute(() =>
                    {
                        float lx = _lastEdgeScrollMouseX;
                        float tw = _trackContent.resolvedStyle.width;
                        float dir = 0f;
                        if (lx < EdgeScrollMargin) dir = -1f;
                        else if (lx > tw - EdgeScrollMargin) dir = 1f;

                        if (dir == 0f) return;

                        float deltaPx = dir * EdgeScrollSpeed * (EdgeScrollIntervalMs / 1000f);
                        _scrollOffset = Mathf.Max(0f, _scrollOffset + deltaPx);

                        // During scrub, also update the seek position
                        if (_dragMode == DragMode.Scrub)
                        {
                            float scrubOrigin = _layer?.TimeOrigin ?? 0f;
                            float time = (lx + _scrollOffset) / _pixelsPerSecond - scrubOrigin;
                            OnSeekRequested?.Invoke(Mathf.Max(0f, time));
                        }

                        UpdateAllBlockPositions();
                        UpdatePlayhead();
                        _rulerArea.MarkDirtyRepaint();
                    }).Every(EdgeScrollIntervalMs);
                }
            }
            else
            {
                StopEdgeScroll();
            }
        }

        private void StopEdgeScroll()
        {
            if (_edgeScrollTimer != null)
            {
                _edgeScrollTimer.Pause();
                _edgeScrollTimer = null;
            }
        }

        // ─── Zoom ───

        private void OnWheel(WheelEvent e)
        {
            float zoomFactor = e.delta.y > 0 ? 0.85f : 1.18f;
            float oldPPS = _pixelsPerSecond;
            _pixelsPerSecond = Mathf.Clamp(_pixelsPerSecond * zoomFactor, MinPPS, MaxPPS);

            var localX = _trackContent.WorldToLocal(e.mousePosition).x;
            float timeAtMouse = (localX + _scrollOffset) / oldPPS;
            _scrollOffset = timeAtMouse * _pixelsPerSecond - localX;
            _scrollOffset = Mathf.Max(0f, _scrollOffset);

            UpdateAllBlockPositions();
            UpdatePlayhead();
            _rulerArea.MarkDirtyRepaint();
            e.StopPropagation();
        }

        // ─── Context Menu ───

        private void OnTrackRightClick(MouseDownEvent e)
        {
            if (e.button != 1) return;
            if (_layer == null) return;

            ShowContextMenu(e.mousePosition, _currentPlayTime);
            e.StopPropagation();
            e.PreventDefault();
        }

        private void ShowContextMenu(Vector2 screenPos, float atTime)
        {
            var entries = _layer.GetContextMenuEntries(atTime, _selectedBlock);
            if (entries == null || entries.Count == 0) return;

            var menu = new VisualElement();
            menu.style.position = Position.Absolute;
            menu.style.left = screenPos.x;
            menu.style.top = screenPos.y;
            menu.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.98f);
            menu.style.borderTopWidth = menu.style.borderBottomWidth =
                menu.style.borderLeftWidth = menu.style.borderRightWidth = 1;
            menu.style.borderTopColor = menu.style.borderBottomColor =
                menu.style.borderLeftColor = menu.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            menu.style.paddingTop = menu.style.paddingBottom = 4;
            menu.style.paddingLeft = menu.style.paddingRight = 2;
            menu.style.borderTopLeftRadius = menu.style.borderTopRightRadius =
                menu.style.borderBottomLeftRadius = menu.style.borderBottomRightRadius = 3;

            var dismiss = new VisualElement();
            dismiss.style.position = Position.Absolute;
            dismiss.style.left = dismiss.style.top = 0;
            dismiss.style.right = dismiss.style.bottom = 0;
            dismiss.RegisterCallback<MouseDownEvent>(evt =>
            {
                dismiss.RemoveFromHierarchy();
                menu.RemoveFromHierarchy();
                evt.StopPropagation();
            });

            void CloseMenu()
            {
                menu.RemoveFromHierarchy();
                dismiss.RemoveFromHierarchy();
            }

            foreach (var entry in entries)
            {
                var capturedEntry = entry;
                var btn = new Button(() =>
                {
                    CloseMenu();
                    capturedEntry.Action?.Invoke();
                })
                { text = capturedEntry.Label };
                btn.style.backgroundColor = Color.clear;
                btn.style.color = capturedEntry.Label.Contains("Delete")
                    ? new Color(1f, 0.5f, 0.5f)
                    : new Color(0.9f, 0.9f, 0.9f);
                btn.style.borderTopWidth = btn.style.borderBottomWidth =
                    btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
                btn.SetEnabled(capturedEntry.Enabled);
                menu.Add(btn);
            }

            Root.panel.visualTree.Add(dismiss);
            Root.panel.visualTree.Add(menu);
            TimelineEditorView.RegisterThemeOverride(menu);
            TimelineEditorView.ClampPopupToScreen(menu);
        }

        // ─── Legacy Bridge Methods ───
        // These methods are kept during migration so TimelineEditorView can still call them.
        // They will be removed once TimelineEditorView is fully refactored in step 1d.

        public void AddEvent(TimelineEvent evt)
        {
            // Find the MidStageLayer's segment to add to
            var segment = (_layer as MidStageLayer)?.Segment;
            if (segment == null) return;

            string desc = evt is SpawnWaveEvent ? "Add Wave Event" : "Add Pattern Event";
            var cmd = ListCommand<TimelineEvent>.Add(
                segment.Events, evt, -1, desc);
            _commandStack.Execute(cmd);

            RebuildBlocks();
            SelectEvent(evt);
            OnEventsChanged?.Invoke();
            OnBlocksChanged?.Invoke();
        }

        public void DeleteSelectedEvent()
        {
            var segment = (_layer as MidStageLayer)?.Segment;
            var selectedEvt = _selectedBlock?.DataSource as TimelineEvent;
            if (segment == null || selectedEvt == null) return;

            int index = segment.Events.IndexOf(selectedEvt);
            if (index < 0) return;

            var cmd = ListCommand<TimelineEvent>.Remove(
                segment.Events, index, "Delete Event");
            _commandStack.Execute(cmd);

            _selectedBlock = null;
            RebuildBlocks();
            OnEventSelected?.Invoke(null);
            OnBlockSelected?.Invoke(null);
            OnEventsChanged?.Invoke();
            OnBlocksChanged?.Invoke();
        }

        // ─── Ruler Scrub ───

        private void OnRulerMouseDown(MouseDownEvent e)
        {
            if (e.button != 0) return;

            _dragMode = DragMode.Scrub;
            _hoverTimeLabel.style.display = DisplayStyle.None; // hide during scrub
            SeekFromRuler(e.mousePosition);

            _rulerArea.RegisterCallback<MouseMoveEvent>(OnRulerMouseMove);
            _rulerArea.RegisterCallback<MouseUpEvent>(OnRulerMouseUp);
            _rulerArea.CaptureMouse();
            e.StopPropagation();
        }

        private void OnRulerMouseMove(MouseMoveEvent e)
        {
            if (_dragMode != DragMode.Scrub) return;
            SeekFromRuler(e.mousePosition);
            UpdateEdgeScroll(e.mousePosition);
        }

        private void OnRulerMouseUp(MouseUpEvent e)
        {
            if (_dragMode != DragMode.Scrub) return;

            _rulerArea.UnregisterCallback<MouseMoveEvent>(OnRulerMouseMove);
            _rulerArea.UnregisterCallback<MouseUpEvent>(OnRulerMouseUp);
            _rulerArea.ReleaseMouse();
            _dragMode = DragMode.None;
            StopEdgeScroll();
        }

        private void SeekFromRuler(Vector2 mousePosition)
        {
            var localX = _rulerArea.WorldToLocal(mousePosition).x;
            float origin = _layer?.TimeOrigin ?? 0f;
            float time = (localX + _scrollOffset) / _pixelsPerSecond - origin;
            OnSeekRequested?.Invoke(Mathf.Max(0f, time));
        }

        // ─── Layout Helpers ───

        /// <summary>
        /// Update visual positions of all blocks without rebuilding the block list.
        /// Use this for property changes (undo/redo) that don't alter the block structure.
        /// For sequential layers, recalculates StartTime from accumulated Duration.
        /// </summary>
        public void RefreshBlockPositions()
        {
            RecalcSequentialLayoutIfNeeded();
            UpdateAllBlockPositions();
        }

        /// <summary>
        /// If the current layer is sequential, recalculate all block StartTimes
        /// from their Durations (so transitions stay aligned after resize).
        /// </summary>
        private void RecalcSequentialLayoutIfNeeded()
        {
            if (_layer == null || !_layer.IsSequential) return;

            float timeOffset = 0f;
            var allBlocks = _layer.GetAllBlocks();
            for (int i = 0; i < allBlocks.Count; i++)
            {
                allBlocks[i].StartTime = timeOffset;
                timeOffset += allBlocks[i].Duration;
            }
        }

        private void UpdateAllBlockPositions()
        {
            float origin = _layer?.TimeOrigin ?? 0f;
            foreach (var info in _blocks)
            {
                float left = (info.Block.StartTime + origin) * _pixelsPerSecond - _scrollOffset;
                float width = info.Block.Duration * _pixelsPerSecond;

                info.Element.style.left = left;
                info.Element.style.top = TrackPadding + info.Row * TrackRowHeight;
                info.Element.style.width = Mathf.Max(width, 4f);
            }
            UpdateParentDurationLine();
        }

        private void UpdateParentDurationLine()
        {
            if (_parentDuration < 0f) return;
            float origin = _layer?.TimeOrigin ?? 0f;
            float x = (_parentDuration + origin) * _pixelsPerSecond - _scrollOffset;
            _parentDurationLine.style.left = x;
        }

        private void UpdatePlayhead()
        {
            float origin = _layer?.TimeOrigin ?? 0f;
            float displayTime = _currentPlayTime + origin;
            float x = displayTime * _pixelsPerSecond - _scrollOffset;
            _playheadLine.style.left = x;
            _playheadRulerMarker.style.left = x - 4;

            // Playhead time bubble
            _playheadTimeLabel.text = FormatTime(displayTime);
            _playheadTimeLabel.style.left = x + 5;
        }

        private Dictionary<ITimelineBlock, int> AssignRows(IReadOnlyList<ITimelineBlock> blocks)
        {
            var result = new Dictionary<ITimelineBlock, int>();
            var rowEnds = new List<float>();

            var sorted = new List<ITimelineBlock>(blocks);
            sorted.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            foreach (var blk in sorted)
            {
                int assignedRow = -1;
                for (int r = 0; r < rowEnds.Count; r++)
                {
                    if (blk.StartTime >= rowEnds[r])
                    {
                        assignedRow = r;
                        rowEnds[r] = blk.StartTime + blk.Duration;
                        break;
                    }
                }

                if (assignedRow < 0)
                {
                    assignedRow = rowEnds.Count;
                    rowEnds.Add(blk.StartTime + blk.Duration);
                }

                result[blk] = assignedRow;
            }

            return result;
        }

        // ─── Ruler Drawing ───

        private void DrawRuler(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            float width = _rulerArea.resolvedStyle.width;
            float height = _rulerArea.resolvedStyle.height;

            if (width <= 0 || height <= 0) return;

            float interval = CalculateTickInterval();
            float startTime = Mathf.Floor((_scrollOffset / _pixelsPerSecond) / interval) * interval;

            painter.strokeColor = new Color(0.4f, 0.4f, 0.4f);
            painter.lineWidth = 1f;

            for (float t = startTime; t * _pixelsPerSecond - _scrollOffset < width; t += interval)
            {
                float x = t * _pixelsPerSecond - _scrollOffset;
                if (x < 0) continue;

                bool isMajor = Mathf.Abs(t % (interval * 5)) < 0.001f || interval >= 5f;
                float tickHeight = isMajor ? height * 0.7f : height * 0.4f;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, height));
                painter.LineTo(new Vector2(x, height - tickHeight));
                painter.Stroke();
            }

            // Schedule label positioning AFTER the render pass completes
            _rulerArea.schedule.Execute(UpdateRulerLabels);
        }

        /// <summary>
        /// Position ruler tick labels. Called via schedule.Execute after DrawRuler
        /// to avoid modifying the visual tree during generateVisualContent.
        /// </summary>
        private void UpdateRulerLabels()
        {
            float width = _rulerArea.resolvedStyle.width;
            if (width <= 0) return;

            float origin = _layer?.TimeOrigin ?? 0f;
            float interval = CalculateTickInterval();
            float startTime = Mathf.Floor((_scrollOffset / _pixelsPerSecond) / interval) * interval;

            int labelIdx = 0;

            for (float t = startTime; t * _pixelsPerSecond - _scrollOffset < width; t += interval)
            {
                float x = t * _pixelsPerSecond - _scrollOffset;
                if (x < 0) continue;

                float displayTime = t + origin;
                bool isMajor = Mathf.Abs(t % (interval * 5)) < 0.001f || interval >= 5f;

                if (isMajor && labelIdx < _rulerLabelPool.Count)
                {
                    var lbl = _rulerLabelPool[labelIdx++];
                    lbl.text = FormatTime(displayTime);
                    lbl.style.left = x + 2;
                    lbl.style.display = DisplayStyle.Flex;
                }
            }

            // Hide unused labels
            for (int i = labelIdx; i < _rulerLabelPool.Count; i++)
                _rulerLabelPool[i].style.display = DisplayStyle.None;
        }

        private float CalculateTickInterval()
        {
            float minPixelsBetweenTicks = 40f;
            float[] intervals = { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };

            foreach (var interval in intervals)
            {
                if (interval * _pixelsPerSecond >= minPixelsBetweenTicks)
                    return interval;
            }
            return 60f;
        }

        // ─── Time Hints ───

        /// <summary>
        /// Format a time value for display on ruler labels and tooltips.
        /// Always uses m:ss or m:ss.f format: "0:00", "0:01", "0:12.5", "1:05", "2:30.0".
        /// </summary>
        private static string FormatTime(float t)
        {
            if (t < 0f) t = 0f;
            int min = (int)(t / 60f);
            float sec = t - min * 60f;
            return sec % 1f < 0.001f ? $"{min}:{sec:00}" : $"{min}:{sec:00.0}";
        }

        private void OnTrackMouseMove(MouseMoveEvent e)
        {
            // Don't show hover tooltip during drag
            if (_dragMode != DragMode.None) return;
            if (_layer == null) return;
            var localX = _trackContent.WorldToLocal(e.mousePosition).x;
            var localY = _trackContent.WorldToLocal(e.mousePosition).y;
            float time = (localX + _scrollOffset) / _pixelsPerSecond;
            _hoverTimeLabel.text = FormatTime(time);
            _hoverTimeLabel.style.left = localX + 12;
            _hoverTimeLabel.style.top = Mathf.Max(0f, localY - 20);
            _hoverTimeLabel.style.display = DisplayStyle.Flex;
            _hoverTimeLabel.BringToFront();
        }

        private void OnRulerHoverMove(MouseMoveEvent e)
        {
            // Don't show hover tooltip during scrub drag
            if (_dragMode == DragMode.Scrub) return;
            if (_layer == null) return;
            var localX = _rulerArea.WorldToLocal(e.mousePosition).x;
            float time = (localX + _scrollOffset) / _pixelsPerSecond;
            // Show hover label in track content area (below ruler)
            _hoverTimeLabel.text = FormatTime(time);
            _hoverTimeLabel.style.left = localX + 12;
            _hoverTimeLabel.style.top = 0;
            _hoverTimeLabel.style.display = DisplayStyle.Flex;
            _hoverTimeLabel.BringToFront();
        }

        // ─── Zoom to Fit ───

        public void ZoomToFit()
        {
            if (_layer == null || _layer.TotalDuration <= 0) return;

            float availableWidth = _trackContent.resolvedStyle.width;
            if (availableWidth <= 0) availableWidth = 600f;

            float origin = _layer.TimeOrigin;
            float visibleDuration = _layer.TotalDuration + origin;
            _pixelsPerSecond = Mathf.Clamp(availableWidth / visibleDuration * 0.9f, MinPPS, MaxPPS);
            _scrollOffset = 0f;

            UpdateAllBlockPositions();
            UpdatePlayhead();
            _rulerArea.MarkDirtyRepaint();
        }

        /// <summary>
        /// Test whether a block's Duration is writable.
        /// Uses a non-destructive check: all current block types have writable Duration,
        /// so this always returns true. The old probe-based approach caused side effects
        /// (SpellCard.TimeLimit mutation during RebuildBlocks).
        /// </summary>
        private static bool CanResizeDuration(ITimelineBlock blk)
        {
            // All block types in the project have writable Duration setters.
            // If a read-only block type is added in the future, add a check here
            // (e.g. via an interface marker or property).
            return true;
        }
    }
}
