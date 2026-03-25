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

        private float _pixelsPerSecond = 60f;
        private float _scrollOffset;
        private const float MinPPS = 15f;
        private const float MaxPPS = 300f;

        private readonly List<BlockInfo> _blocks = new();
        private ITimelineBlock _selectedBlock;

        private enum DragMode { None, Move, Resize, Scrub }
        private DragMode _dragMode;
        private BlockInfo _dragBlockInfo;
        private float _dragStartMouseX;
        private float _dragStartValue;

        private const float TrackRowHeight = 34f;
        private const float TrackPadding = 4f;
        private float _currentPlayTime;

        public float SnapPlayheadThreshold { get; set; }
        public float SnapGridSize { get; set; }

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

            TimelineEditorView.RegisterThemeOverride(Root);
        }

        // ─── Layer Binding ───

        public void SetLayer(ITimelineLayer layer)
        {
            _layer = layer;
            _selectedBlock = null;
            RebuildBlocks();
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
        }

        public float PixelsPerSecond => _pixelsPerSecond;
        public ITimelineBlock SelectedBlock => _selectedBlock;

        /// <summary>Legacy accessor.</summary>
        public TimelineEvent SelectedEvent => _selectedBlock?.DataSource as TimelineEvent;

        public ITimelineLayer CurrentLayer => _layer;

        public void RebuildBlocks()
        {
            var toRemove = new List<VisualElement>();
            foreach (var child in _trackContent.Children())
            {
                if (child != _playheadLine)
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
                foreach (var b in _blocks)
                {
                    if (b.Block.Id == _selectedBlock.Id)
                    {
                        ApplySelectionStyle(b.Element, true);
                        break;
                    }
                }
            }

            UpdateAllBlockPositions();
            UpdatePlayhead();
            _rulerArea.MarkDirtyRepaint();
        }

        public void Dispose()
        {
            _blocks.Clear();
        }

        // ─── Block Creation ───

        private void CreateBlock(ITimelineBlock blk, int row)
        {
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.height = TrackRowHeight - 6;
            element.style.backgroundColor = blk.BlockColor;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 3;
            element.style.borderTopWidth = element.style.borderBottomWidth =
                element.style.borderLeftWidth = element.style.borderRightWidth = 1;
            element.style.borderTopColor = element.style.borderBottomColor =
                element.style.borderLeftColor = element.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            element.style.overflow = Overflow.Hidden;
            element.style.paddingLeft = 4;
            element.style.justifyContent = Justify.Center;

            var label = new Label(blk.DisplayLabel);
            label.style.color = new Color(0.9f, 0.9f, 0.9f);
            label.style.fontSize = 10;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            element.Add(label);

            // DesignEstimate green line (drawn via generateVisualContent)
            float estimate = blk.DesignEstimate;
            if (estimate >= 0f && estimate < blk.Duration)
            {
                element.generateVisualContent += ctx => DrawDesignEstimateLine(ctx, blk);
            }

            // Resize handle
            var resizeHandle = new VisualElement();
            resizeHandle.style.position = Position.Absolute;
            resizeHandle.style.right = 0;
            resizeHandle.style.top = 0;
            resizeHandle.style.bottom = 0;
            resizeHandle.style.width = 6;
            resizeHandle.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
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
                    // Double-click detection
                    if (e.clickCount == 2)
                    {
                        OnBlockDoubleClicked?.Invoke(blk);
                        e.StopPropagation();
                        return;
                    }

                    SelectBlock(blk);
                    if (blk.CanMove)
                    {
                        StartDrag(DragMode.Move, info, e.mousePosition.x);
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
                float rawStart = Mathf.Max(0f, _dragStartValue + deltaTime);
                float duration = blk.Duration;
                float rawEnd = rawStart + duration;

                float snappedStart = SnapTime(rawStart);
                float snappedEnd = SnapTime(rawEnd);

                bool startSnapped = Mathf.Abs(snappedStart - rawStart) > 0.0001f;
                bool endSnapped = Mathf.Abs(snappedEnd - rawEnd) > 0.0001f;

                float newStart;
                if (startSnapped && endSnapped)
                {
                    newStart = Mathf.Abs(snappedStart - rawStart) <= Mathf.Abs(snappedEnd - rawEnd)
                        ? snappedStart
                        : Mathf.Max(0f, snappedEnd - duration);
                }
                else if (startSnapped)
                    newStart = snappedStart;
                else if (endSnapped)
                    newStart = Mathf.Max(0f, snappedEnd - duration);
                else
                    newStart = rawStart;

                blk.StartTime = newStart;
            }
            else if (_dragMode == DragMode.Resize)
            {
                float rawDuration = Mathf.Max(0.5f, _dragStartValue + deltaTime);
                float startTime = blk.StartTime;
                float snappedEnd = SnapTime(startTime + rawDuration);
                float newDuration = Mathf.Max(0.5f, snappedEnd - startTime);
                blk.Duration = newDuration;
            }

            UpdateAllBlockPositions();
            OnBlockValuesChanged?.Invoke(blk);
            // Legacy bridge
            if (blk.DataSource is TimelineEvent te)
                OnEventValuesChanged?.Invoke(te);
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
                    OnBlocksChanged?.Invoke();
                    OnEventsChanged?.Invoke();
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
                    OnBlocksChanged?.Invoke();
                    OnEventsChanged?.Invoke();
                }
            }

            _dragMode = DragMode.None;
            _dragBlockInfo = default;
            UpdateAllBlockPositions();
        }

        // ─── Selection ───

        public void SelectBlock(ITimelineBlock blk)
        {
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

            float time = ((e.localMousePosition.x + _scrollOffset) / _pixelsPerSecond);
            time = Mathf.Max(0f, time);

            ShowContextMenu(e.mousePosition, time);
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
        }

        private void OnRulerMouseUp(MouseUpEvent e)
        {
            if (_dragMode != DragMode.Scrub) return;

            _rulerArea.UnregisterCallback<MouseMoveEvent>(OnRulerMouseMove);
            _rulerArea.UnregisterCallback<MouseUpEvent>(OnRulerMouseUp);
            _rulerArea.ReleaseMouse();
            _dragMode = DragMode.None;
        }

        private void SeekFromRuler(Vector2 mousePosition)
        {
            var localX = _rulerArea.WorldToLocal(mousePosition).x;
            float time = (localX + _scrollOffset) / _pixelsPerSecond;
            OnSeekRequested?.Invoke(Mathf.Max(0f, time));
        }

        // ─── Layout Helpers ───

        private void UpdateAllBlockPositions()
        {
            foreach (var info in _blocks)
            {
                float left = info.Block.StartTime * _pixelsPerSecond - _scrollOffset;
                float width = info.Block.Duration * _pixelsPerSecond;

                info.Element.style.left = left;
                info.Element.style.top = TrackPadding + info.Row * TrackRowHeight;
                info.Element.style.width = Mathf.Max(width, 4f);
            }
        }

        private void UpdatePlayhead()
        {
            float x = _currentPlayTime * _pixelsPerSecond - _scrollOffset;
            _playheadLine.style.left = x;
            _playheadRulerMarker.style.left = x - 4;
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
            float startTime = Mathf.Floor(_scrollOffset / _pixelsPerSecond / interval) * interval;

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

        // ─── Zoom to Fit ───

        public void ZoomToFit()
        {
            if (_layer == null || _layer.TotalDuration <= 0) return;

            float availableWidth = _trackContent.resolvedStyle.width;
            if (availableWidth <= 0) availableWidth = 600f;

            _pixelsPerSecond = Mathf.Clamp(availableWidth / _layer.TotalDuration * 0.9f, MinPPS, MaxPPS);
            _scrollOffset = 0f;

            UpdateAllBlockPositions();
            UpdatePlayhead();
            _rulerArea.MarkDirtyRepaint();
        }
    }
}
