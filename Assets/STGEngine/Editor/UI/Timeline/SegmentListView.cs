using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Editor.Commands;

namespace STGEngine.Editor.UI.Timeline
{
    /// <summary>
    /// Left panel: displays the list of segments in a stage.
    /// Supports selection, add, delete, and trigger condition editing.
    /// </summary>
    public class SegmentListView : IDisposable
    {
        public VisualElement Root { get; }
        public event Action<TimelineSegment> OnSegmentSelected;
        public event Action OnStageChanged;

        private Stage _stage;
        private TimelineSegment _selectedSegment;
        private readonly CommandStack _commandStack;
        private readonly ScrollView _listScroll;
        private readonly List<VisualElement> _segmentItems = new();

        private static readonly Color LightText = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color PanelBg = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        private static readonly Color SelectedBg = new Color(0.2f, 0.35f, 0.55f);
        private static readonly Color HoverBg = new Color(0.22f, 0.22f, 0.28f);

        public TimelineSegment SelectedSegment => _selectedSegment;

        public SegmentListView(CommandStack commandStack)
        {
            _commandStack = commandStack;

            Root = new VisualElement();
            Root.style.width = 160;
            Root.style.backgroundColor = PanelBg;
            Root.style.borderRightWidth = 1;
            Root.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = 26;
            header.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            header.style.paddingLeft = 8;
            header.style.paddingRight = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

            var headerLabel = new Label("Segments");
            headerLabel.style.color = LightText;
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.flexGrow = 1;
            header.Add(headerLabel);

            var addBtn = new Button(OnAddSegment) { text = "+" };
            addBtn.style.width = 24;
            addBtn.style.height = 20;
            addBtn.style.color = LightText;
            addBtn.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            header.Add(addBtn);

            Root.Add(header);

            // Scrollable list
            _listScroll = new ScrollView(ScrollViewMode.Vertical);
            _listScroll.style.flexGrow = 1;
            Root.Add(_listScroll);

            // Register delayed theme override to survive Unity Runtime Theme
            TimelineEditorView.RegisterThemeOverride(Root);
        }

        public void SetStage(Stage stage)
        {
            _stage = stage;
            _selectedSegment = null;
            RebuildList();

            // Auto-select first segment
            if (_stage != null && _stage.Segments.Count > 0)
                SelectSegment(_stage.Segments[0]);
        }

        public void RebuildList()
        {
            _listScroll.Clear();
            _segmentItems.Clear();

            if (_stage == null) return;

            for (int i = 0; i < _stage.Segments.Count; i++)
            {
                var segment = _stage.Segments[i];
                var item = CreateSegmentItem(segment, i);
                _listScroll.Add(item);
                _segmentItems.Add(item);
            }
        }

        public void SelectSegment(TimelineSegment segment)
        {
            _selectedSegment = segment;
            UpdateSelection();
            OnSegmentSelected?.Invoke(segment);
        }

        public void Dispose()
        {
            _listScroll.Clear();
            _segmentItems.Clear();
        }

        private VisualElement CreateSegmentItem(TimelineSegment segment, int index)
        {
            var item = new VisualElement();
            item.style.paddingTop = 6;
            item.style.paddingBottom = 6;
            item.style.paddingLeft = 8;
            item.style.paddingRight = 8;
            item.style.borderBottomWidth = 1;
            item.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);

            var nameLabel = new Label(segment.Name);
            nameLabel.style.color = LightText;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.fontSize = 12;
            item.Add(nameLabel);

            var typeStr = segment.Type == SegmentType.MidStage ? "MidStage" : "BossFight";
            string countInfo;
            if (segment.Type == SegmentType.BossFight)
                countInfo = $"{segment.SpellCardIds.Count} spells";
            else
                countInfo = $"{segment.Events.Count} events";
            var infoLabel = new Label($"{typeStr} · {segment.Duration:F1}s · {countInfo}");
            infoLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            infoLabel.style.fontSize = 10;
            item.Add(infoLabel);

            // Trigger info
            if (segment.EntryTrigger != null && index > 0)
            {
                var triggerLabel = new Label($"Trigger: {segment.EntryTrigger.Type}");
                triggerLabel.style.color = new Color(0.5f, 0.7f, 1f);
                triggerLabel.style.fontSize = 10;
                item.Add(triggerLabel);
            }

            // Hover effect
            item.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (_selectedSegment != segment)
                    item.style.backgroundColor = HoverBg;
            });
            item.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (_selectedSegment != segment)
                    item.style.backgroundColor = Color.clear;
            });

            // Click to select
            item.RegisterCallback<ClickEvent>(evt => SelectSegment(segment));

            // Right-click context menu
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 1) return;
                ShowSegmentContextMenu(segment, index, evt.mousePosition);
                evt.StopPropagation();
                evt.PreventDefault();
            });

            return item;
        }

        private void UpdateSelection()
        {
            if (_stage == null) return;

            for (int i = 0; i < _stage.Segments.Count && i < _segmentItems.Count; i++)
            {
                var item = _segmentItems[i];
                if (_stage.Segments[i] == _selectedSegment)
                {
                    item.style.backgroundColor = SelectedBg;
                }
                else
                {
                    item.style.backgroundColor = Color.clear;
                }
            }
        }

        private void OnAddSegment()
        {
            if (_stage == null) return;
            AddSegmentOfType(SegmentType.MidStage);
        }

        /// <summary>Add a segment of the given type.</summary>
        public void AddSegmentOfType(SegmentType type)
        {
            if (_stage == null) return;

            var segment = new TimelineSegment
            {
                Id = $"segment_{_stage.Segments.Count + 1}",
                Name = type == SegmentType.BossFight
                    ? $"Boss {_stage.Segments.Count + 1}"
                    : $"Segment {_stage.Segments.Count + 1}",
                Type = type,
                Duration = type == SegmentType.BossFight ? 120f : 30f,
                EntryTrigger = _stage.Segments.Count > 0
                    ? new TriggerCondition { Type = TriggerType.Immediate }
                    : null
            };

            var cmd = ListCommand<TimelineSegment>.Add(
                _stage.Segments, segment, -1, "Add Segment");
            _commandStack.Execute(cmd);

            RebuildList();
            SelectSegment(segment);
            OnStageChanged?.Invoke();
        }

        private void ShowSegmentContextMenu(TimelineSegment segment, int index, Vector2 position)
        {
            var menu = new VisualElement();
            menu.style.position = Position.Absolute;
            menu.style.left = position.x;
            menu.style.top = position.y;
            menu.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.98f);
            menu.style.borderTopWidth = menu.style.borderBottomWidth =
                menu.style.borderLeftWidth = menu.style.borderRightWidth = 1;
            menu.style.borderTopColor = menu.style.borderBottomColor =
                menu.style.borderLeftColor = menu.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            menu.style.paddingTop = menu.style.paddingBottom = 4;
            menu.style.paddingLeft = menu.style.paddingRight = 2;
            menu.style.borderTopLeftRadius = menu.style.borderTopRightRadius =
                menu.style.borderBottomLeftRadius = menu.style.borderBottomRightRadius = 3;

            // Dismiss layer
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

            var deleteBtn = new Button(() =>
            {
                CloseMenu();
                DeleteSegment(segment, index);
            })
            { text = "Delete Segment" };
            deleteBtn.style.backgroundColor = Color.clear;
            deleteBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            deleteBtn.style.borderTopWidth = deleteBtn.style.borderBottomWidth =
                deleteBtn.style.borderLeftWidth = deleteBtn.style.borderRightWidth = 0;
            menu.Add(deleteBtn);

            // Toggle segment type
            var toggleTypeLabel = segment.Type == SegmentType.MidStage
                ? "Switch to BossFight"
                : "Switch to MidStage";
            var toggleTypeBtn = new Button(() =>
            {
                CloseMenu();
                ToggleSegmentType(segment);
            })
            { text = toggleTypeLabel };
            toggleTypeBtn.style.backgroundColor = Color.clear;
            toggleTypeBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            toggleTypeBtn.style.borderTopWidth = toggleTypeBtn.style.borderBottomWidth =
                toggleTypeBtn.style.borderLeftWidth = toggleTypeBtn.style.borderRightWidth = 0;
            menu.Add(toggleTypeBtn);

            // Trigger condition submenu
            if (index > 0)
            {
                var triggerBtn = new Button(() =>
                {
                    CloseMenu();
                    CycleTriggerType(segment);
                })
                { text = $"Trigger: {segment.EntryTrigger?.Type ?? TriggerType.Immediate}" };
                triggerBtn.style.backgroundColor = Color.clear;
                triggerBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
                triggerBtn.style.borderTopWidth = triggerBtn.style.borderBottomWidth =
                    triggerBtn.style.borderLeftWidth = triggerBtn.style.borderRightWidth = 0;
                menu.Add(triggerBtn);
            }

            Root.panel.visualTree.Add(dismiss);
            Root.panel.visualTree.Add(menu);
            TimelineEditorView.RegisterThemeOverride(menu);
        }

        private void DeleteSegment(TimelineSegment segment, int index)
        {
            if (_stage == null || _stage.Segments.Count <= 1) return;

            var cmd = ListCommand<TimelineSegment>.Remove(
                _stage.Segments, index, "Delete Segment");
            _commandStack.Execute(cmd);

            if (_selectedSegment == segment)
            {
                _selectedSegment = _stage.Segments.Count > 0 ? _stage.Segments[0] : null;
            }

            RebuildList();
            UpdateSelection();
            OnSegmentSelected?.Invoke(_selectedSegment);
            OnStageChanged?.Invoke();
        }

        private void CycleTriggerType(TimelineSegment segment)
        {
            if (segment.EntryTrigger == null)
                segment.EntryTrigger = new TriggerCondition();

            var types = (TriggerType[])Enum.GetValues(typeof(TriggerType));
            int current = Array.IndexOf(types, segment.EntryTrigger.Type);
            int next = (current + 1) % types.Length;

            var oldType = segment.EntryTrigger.Type;
            var newType = types[next];

            var cmd = new PropertyChangeCommand<TriggerType>(
                "Change Trigger Type",
                () => segment.EntryTrigger.Type,
                v => segment.EntryTrigger.Type = v,
                newType);
            _commandStack.Execute(cmd);

            RebuildList();
            UpdateSelection();
            OnStageChanged?.Invoke();
        }

        private void ToggleSegmentType(TimelineSegment segment)
        {
            var newType = segment.Type == SegmentType.MidStage
                ? SegmentType.BossFight
                : SegmentType.MidStage;

            var cmd = new PropertyChangeCommand<SegmentType>(
                "Toggle Segment Type",
                () => segment.Type,
                v => segment.Type = v,
                newType);
            _commandStack.Execute(cmd);

            RebuildList();
            UpdateSelection();
            OnSegmentSelected?.Invoke(segment);
            OnStageChanged?.Invoke();
        }
    }
}
