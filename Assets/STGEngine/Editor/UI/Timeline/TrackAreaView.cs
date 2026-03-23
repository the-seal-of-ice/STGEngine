using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Timeline;
using STGEngine.Editor.Commands;

namespace STGEngine.Editor.UI.Timeline
{
    public class TrackAreaView : IDisposable
    {
        public VisualElement Root { get; }
        public event Action<SpawnPatternEvent> OnEventSelected;
        public event Action OnEventsChanged;

        private TimelineSegment _segment;
        private readonly CommandStack _commandStack;

        private readonly VisualElement _rulerArea;
        private readonly VisualElement _trackContent;
        private readonly VisualElement _playheadLine;
        private readonly VisualElement _playheadRulerMarker;

        private float _pixelsPerSecond = 60f;
        private float _scrollOffset;
        private const float MinPPS = 15f;
        private const float MaxPPS = 300f;

        private readonly List<EventBlockInfo> _blocks = new();
        private SpawnPatternEvent _selectedEvent;

        private enum DragMode { None, Move, Resize }
        private DragMode _dragMode;
        private EventBlockInfo _dragBlock;
        private float _dragStartMouseX;
        private float _dragStartValue;

        private const float TrackRowHeight = 34f;
        private const float TrackPadding = 4f;
        private float _currentPlayTime;

        private struct EventBlockInfo
        {
            public SpawnPatternEvent Event;
            public VisualElement Block;
            public VisualElement ResizeHandle;
            public int Row;
        }

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
            _rulerArea.RegisterCallback<ClickEvent>(OnRulerClick);
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
            _trackContent.RegisterCallback<ContextClickEvent>(OnTrackContextClick);
            Root.Add(_trackContent);

            // Playhead line in track
            _playheadLine = new VisualElement();
            _playheadLine.style.position = Position.Absolute;
            _playheadLine.style.width = 2;
            _playheadLine.style.top = 0;
            _playheadLine.style.bottom = 0;
            _playheadLine.style.backgroundColor = new Color(1f, 0.2f, 0.2f);
            _trackContent.Add(_playheadLine);

            // Register delayed theme override to survive Unity Runtime Theme
            TimelineEditorView.RegisterThemeOverride(Root);
        }

        public void SetSegment(TimelineSegment segment)
        {
            _segment = segment;
            _selectedEvent = null;
            RebuildBlocks();
        }

        public void SetPlayTime(float time)
        {
            _currentPlayTime = time;
            UpdatePlayhead();
        }

        public float PixelsPerSecond => _pixelsPerSecond;
        public SpawnPatternEvent SelectedEvent => _selectedEvent;

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

            if (_segment == null) return;

            var rows = AssignRows(_segment.Events);

            foreach (var evt in _segment.Events)
            {
                if (evt is SpawnPatternEvent spawnEvt)
                {
                    int row = rows.TryGetValue(evt, out var r) ? r : 0;
                    CreateEventBlock(spawnEvt, row);
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

        // ─── Event Block Creation ───

        private void CreateEventBlock(SpawnPatternEvent evt, int row)
        {
            var block = new VisualElement();
            block.style.position = Position.Absolute;
            block.style.height = TrackRowHeight - 6;
            block.style.backgroundColor = GetEventColor(evt);
            block.style.borderTopLeftRadius = block.style.borderTopRightRadius =
                block.style.borderBottomLeftRadius = block.style.borderBottomRightRadius = 3;
            block.style.borderTopWidth = block.style.borderBottomWidth =
                block.style.borderLeftWidth = block.style.borderRightWidth = 1;
            block.style.borderTopColor = block.style.borderBottomColor =
                block.style.borderLeftColor = block.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            block.style.overflow = Overflow.Hidden;
            block.style.paddingLeft = 4;
            block.style.justifyContent = Justify.Center;

            var label = new Label(evt.PatternId);
            label.style.color = new Color(0.9f, 0.9f, 0.9f);
            label.style.fontSize = 10;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            block.Add(label);

            // Resize handle
            var resizeHandle = new VisualElement();
            resizeHandle.style.position = Position.Absolute;
            resizeHandle.style.right = 0;
            resizeHandle.style.top = 0;
            resizeHandle.style.bottom = 0;
            resizeHandle.style.width = 6;
            resizeHandle.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            block.Add(resizeHandle);

            var info = new EventBlockInfo
            {
                Event = evt,
                Block = block,
                ResizeHandle = resizeHandle,
                Row = row
            };
            _blocks.Add(info);

            // Click to select
            block.RegisterCallback<ClickEvent>(e =>
            {
                SelectEvent(evt);
                e.StopPropagation();
            });

            // Drag to move
            block.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0 && !IsOverResizeHandle(e, block, resizeHandle))
                {
                    StartDrag(DragMode.Move, info, e.mousePosition.x);
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

            _trackContent.Add(block);
            _playheadLine.BringToFront();
        }

        private bool IsOverResizeHandle(MouseDownEvent e, VisualElement block, VisualElement handle)
        {
            var localPos = block.WorldToLocal(e.mousePosition);
            return localPos.x >= block.resolvedStyle.width - 8;
        }

        // ─── Drag Logic ───

        private void StartDrag(DragMode mode, EventBlockInfo info, float mouseX)
        {
            _dragMode = mode;
            _dragBlock = info;
            _dragStartMouseX = mouseX;
            _dragStartValue = mode == DragMode.Move ? info.Event.StartTime : info.Event.Duration;

            _trackContent.RegisterCallback<MouseMoveEvent>(OnDragMove);
            _trackContent.RegisterCallback<MouseUpEvent>(OnDragEnd);
            _trackContent.CaptureMouse();
        }

        private void OnDragMove(MouseMoveEvent e)
        {
            if (_dragMode == DragMode.None) return;

            float deltaX = e.mousePosition.x - _dragStartMouseX;
            float deltaTime = deltaX / _pixelsPerSecond;

            if (_dragMode == DragMode.Move)
            {
                float newStart = Mathf.Max(0f, _dragStartValue + deltaTime);
                _dragBlock.Event.StartTime = newStart;
            }
            else if (_dragMode == DragMode.Resize)
            {
                float newDuration = Mathf.Max(0.5f, _dragStartValue + deltaTime);
                _dragBlock.Event.Duration = newDuration;
            }

            UpdateAllBlockPositions();
        }

        private void OnDragEnd(MouseUpEvent e)
        {
            if (_dragMode == DragMode.None) return;

            _trackContent.UnregisterCallback<MouseMoveEvent>(OnDragMove);
            _trackContent.UnregisterCallback<MouseUpEvent>(OnDragEnd);
            _trackContent.ReleaseMouse();

            if (_dragMode == DragMode.Move)
            {
                float oldVal = _dragStartValue;
                float newVal = _dragBlock.Event.StartTime;
                if (Mathf.Abs(oldVal - newVal) > 0.001f)
                {
                    var evt = _dragBlock.Event;
                    evt.StartTime = oldVal;
                    var cmd = new PropertyChangeCommand<float>(
                        "Move Event",
                        () => evt.StartTime,
                        v => evt.StartTime = v,
                        newVal);
                    _commandStack.Execute(cmd);
                    OnEventsChanged?.Invoke();
                }
            }
            else if (_dragMode == DragMode.Resize)
            {
                float oldVal = _dragStartValue;
                float newVal = _dragBlock.Event.Duration;
                if (Mathf.Abs(oldVal - newVal) > 0.001f)
                {
                    var evt = _dragBlock.Event;
                    evt.Duration = oldVal;
                    var cmd = new PropertyChangeCommand<float>(
                        "Resize Event",
                        () => evt.Duration,
                        v => evt.Duration = v,
                        newVal);
                    _commandStack.Execute(cmd);
                    OnEventsChanged?.Invoke();
                }
            }

            _dragMode = DragMode.None;
            _dragBlock = default;
            UpdateAllBlockPositions();
        }

        // ─── Selection ───

        public void SelectEvent(SpawnPatternEvent evt)
        {
            _selectedEvent = evt;
            foreach (var b in _blocks)
            {
                if (b.Event == evt)
                {
                    b.Block.style.borderTopColor = b.Block.style.borderBottomColor =
                        b.Block.style.borderLeftColor = b.Block.style.borderRightColor =
                            new Color(0.3f, 0.6f, 1f);
                    b.Block.style.borderTopWidth = b.Block.style.borderBottomWidth =
                        b.Block.style.borderLeftWidth = b.Block.style.borderRightWidth = 2;
                }
                else
                {
                    b.Block.style.borderTopColor = b.Block.style.borderBottomColor =
                        b.Block.style.borderLeftColor = b.Block.style.borderRightColor =
                            new Color(0.4f, 0.4f, 0.4f);
                    b.Block.style.borderTopWidth = b.Block.style.borderBottomWidth =
                        b.Block.style.borderLeftWidth = b.Block.style.borderRightWidth = 1;
                }
            }
            OnEventSelected?.Invoke(evt);
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

        private void OnTrackContextClick(ContextClickEvent e)
        {
            if (_segment == null) return;

            var localPos = _trackContent.WorldToLocal(e.mousePosition);
            float clickTime = (localPos.x + _scrollOffset) / _pixelsPerSecond;

            ShowAddEventMenu(e.mousePosition, clickTime);
            e.StopPropagation();
        }

        private void ShowAddEventMenu(Vector2 screenPos, float atTime)
        {
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

            var addBtn = new Button(() =>
            {
                menu.RemoveFromHierarchy();
                AddEventAtTime(atTime);
            })
            { text = "Add Pattern Event" };
            addBtn.style.backgroundColor = Color.clear;
            addBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            addBtn.style.borderTopWidth = addBtn.style.borderBottomWidth =
                addBtn.style.borderLeftWidth = addBtn.style.borderRightWidth = 0;
            menu.Add(addBtn);

            if (_selectedEvent != null)
            {
                var delBtn = new Button(() =>
                {
                    menu.RemoveFromHierarchy();
                    DeleteSelectedEvent();
                })
                { text = "Delete Selected Event" };
                delBtn.style.backgroundColor = Color.clear;
                delBtn.style.color = new Color(1f, 0.5f, 0.5f);
                delBtn.style.borderTopWidth = delBtn.style.borderBottomWidth =
                    delBtn.style.borderLeftWidth = delBtn.style.borderRightWidth = 0;
                menu.Add(delBtn);
            }

            Root.panel.visualTree.Add(menu);
            // Theme override for dynamically added context menu
            TimelineEditorView.RegisterThemeOverride(menu);
            menu.focusable = true;
            menu.schedule.Execute(() => menu.Focus());
            menu.RegisterCallback<FocusOutEvent>(evt => menu.RemoveFromHierarchy());
        }

        public event Action<float> OnAddEventRequested;

        private void AddEventAtTime(float time)
        {
            OnAddEventRequested?.Invoke(Mathf.Max(0f, time));
        }

        public void AddEvent(SpawnPatternEvent evt)
        {
            if (_segment == null) return;

            var cmd = ListCommand<TimelineEvent>.Add(
                _segment.Events, evt, -1, "Add Pattern Event");
            _commandStack.Execute(cmd);

            RebuildBlocks();
            SelectEvent(evt);
            OnEventsChanged?.Invoke();
        }

        public void DeleteSelectedEvent()
        {
            if (_segment == null || _selectedEvent == null) return;

            int index = _segment.Events.IndexOf(_selectedEvent);
            if (index < 0) return;

            var cmd = ListCommand<TimelineEvent>.Remove(
                _segment.Events, index, "Delete Event");
            _commandStack.Execute(cmd);

            _selectedEvent = null;
            RebuildBlocks();
            OnEventSelected?.Invoke(null);
            OnEventsChanged?.Invoke();
        }

        // ─── Ruler Click → Seek ───

        private void OnRulerClick(ClickEvent e)
        {
            var localX = _rulerArea.WorldToLocal(e.position).x;
            float time = (localX + _scrollOffset) / _pixelsPerSecond;
            OnSeekRequested?.Invoke(Mathf.Max(0f, time));
        }

        public event Action<float> OnSeekRequested;

        // ─── Layout Helpers ───

        private void UpdateAllBlockPositions()
        {
            foreach (var info in _blocks)
            {
                float left = info.Event.StartTime * _pixelsPerSecond - _scrollOffset;
                float width = info.Event.Duration * _pixelsPerSecond;

                info.Block.style.left = left;
                info.Block.style.top = TrackPadding + info.Row * TrackRowHeight;
                info.Block.style.width = Mathf.Max(width, 4f);
            }
        }

        private void UpdatePlayhead()
        {
            float x = _currentPlayTime * _pixelsPerSecond - _scrollOffset;
            _playheadLine.style.left = x;
            _playheadRulerMarker.style.left = x - 4;
        }

        private Dictionary<TimelineEvent, int> AssignRows(List<TimelineEvent> events)
        {
            var result = new Dictionary<TimelineEvent, int>();
            var rowEnds = new List<float>();

            var sorted = new List<TimelineEvent>(events);
            sorted.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            foreach (var evt in sorted)
            {
                int assignedRow = -1;
                for (int r = 0; r < rowEnds.Count; r++)
                {
                    if (evt.StartTime >= rowEnds[r])
                    {
                        assignedRow = r;
                        rowEnds[r] = evt.EndTime;
                        break;
                    }
                }

                if (assignedRow < 0)
                {
                    assignedRow = rowEnds.Count;
                    rowEnds.Add(evt.EndTime);
                }

                result[evt] = assignedRow;
            }

            return result;
        }

        private Color GetEventColor(SpawnPatternEvent evt)
        {
            int hash = evt.PatternId?.GetHashCode() ?? 0;
            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.5f, 0.6f);
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

        public void ZoomToFit()
        {
            if (_segment == null || _segment.Duration <= 0) return;

            float availableWidth = _trackContent.resolvedStyle.width;
            if (availableWidth <= 0) availableWidth = 600f;

            _pixelsPerSecond = Mathf.Clamp(availableWidth / _segment.Duration * 0.9f, MinPPS, MaxPPS);
            _scrollOffset = 0f;

            UpdateAllBlockPositions();
            UpdatePlayhead();
            _rulerArea.MarkDirtyRepaint();
        }
    }
}
