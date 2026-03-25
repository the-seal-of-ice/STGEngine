using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Editor.Commands;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Runtime;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Timeline layer for L0 Stage.
    /// Blocks = Segments laid out sequentially by Duration.
    /// Double-click a Segment → MidStageLayer or BossFightLayer.
    /// Replaces SegmentListView.
    /// </summary>
    public class StageLayer : ITimelineLayer
    {
        private readonly Stage _stage;
        private readonly STGCatalog _catalog;
        private readonly PatternLibrary _library;
        private readonly CommandStack _commandStack;
        private readonly List<SegmentBlock> _blocks = new();

        public StageLayer(Stage stage, STGCatalog catalog, PatternLibrary library, CommandStack commandStack)
        {
            _stage = stage;
            _catalog = catalog;
            _library = library;
            _commandStack = commandStack;
            RebuildBlockList();
        }

        // ── Identity ──

        public string LayerId => $"stage:{_stage.Id}";
        public string DisplayName => _stage.Name;

        // ── Block data ──

        public int BlockCount => _blocks.Count;
        public ITimelineBlock GetBlock(int index) => _blocks[index];

        public IReadOnlyList<ITimelineBlock> GetAllBlocks()
        {
            RebuildBlockList();
            return _blocks;
        }

        // ── Timeline parameters ──

        public float TotalDuration
        {
            get
            {
                float total = 0f;
                foreach (var seg in _stage.Segments)
                    total += seg.Duration;
                return total > 0f ? total : 30f;
            }
        }

        public bool IsSequential => true;

        // ── Interaction ──

        public bool CanAddBlock => true;

        public bool CanDoubleClickEnter(ITimelineBlock block)
        {
            return block is SegmentBlock;
        }

        public ITimelineLayer CreateChildLayer(ITimelineBlock block)
        {
            if (block is SegmentBlock sb && sb.DataSource is TimelineSegment segment)
            {
                if (segment.Type == SegmentType.BossFight)
                    return new BossFightLayer(segment, _catalog, _library);
                else
                    return new MidStageLayer(segment);
            }
            return null;
        }

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            var entries = new List<ContextMenuEntry>
            {
                new("Add MidStage Segment", () =>
                {
                    AddSegment(SegmentType.MidStage);
                }),
                new("Add BossFight Segment", () =>
                {
                    AddSegment(SegmentType.BossFight);
                })
            };

            if (selectedBlock is SegmentBlock sb && sb.DataSource is TimelineSegment seg)
            {
                // Toggle type
                var toggleLabel = seg.Type == SegmentType.MidStage
                    ? "Switch to BossFight"
                    : "Switch to MidStage";
                entries.Add(new ContextMenuEntry(toggleLabel, () =>
                {
                    ToggleSegmentType(seg);
                }));

                // Cycle trigger (not for first segment)
                int idx = _stage.Segments.IndexOf(seg);
                if (idx > 0)
                {
                    var trigLabel = $"Trigger: {seg.EntryTrigger?.Type ?? TriggerType.Immediate}";
                    entries.Add(new ContextMenuEntry(trigLabel, () =>
                    {
                        CycleTriggerType(seg);
                    }));
                }

                // Delete (only if more than 1 segment)
                if (_stage.Segments.Count > 1)
                {
                    entries.Add(new ContextMenuEntry("Delete Segment", () =>
                    {
                        OnDeleteSegmentRequested?.Invoke(selectedBlock);
                    }));
                }
            }

            return entries;
        }

        // ── Properties panel ──

        public void BuildPropertiesPanel(VisualElement container, ITimelineBlock block)
        {
            if (block == null)
            {
                var label = new Label($"Stage: {_stage.Name}\nSegments: {_stage.Segments.Count}\nSeed: {_stage.Seed}");
                label.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(label);
                return;
            }

            if (block is SegmentBlock sb && sb.DataSource is TimelineSegment seg)
            {
                var typeStr = seg.Type == SegmentType.MidStage ? "MidStage" : "BossFight";
                string countInfo = seg.Type == SegmentType.BossFight
                    ? $"{seg.SpellCardIds.Count} spell cards"
                    : $"{seg.Events.Count} events";

                var info = new Label($"Segment: {seg.Name}\n" +
                    $"Type: {typeStr}\n" +
                    $"Duration: {seg.Duration:F1}s\n" +
                    $"{countInfo}");
                info.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(info);

                if (seg.EntryTrigger != null)
                {
                    var trigLabel = new Label($"Trigger: {seg.EntryTrigger.Type}");
                    trigLabel.style.color = new Color(0.5f, 0.7f, 1f);
                    trigLabel.style.marginTop = 4;
                    container.Add(trigLabel);
                }
            }
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            // Stage level doesn't have a single preview — preview is per-segment
            playback?.LoadSegment(null);
        }

        // ── Layer-specific events ──

        /// <summary>Raised when a segment should be deleted.</summary>
        public Action<ITimelineBlock> OnDeleteSegmentRequested;

        /// <summary>Raised after any structural change (add/delete/reorder/type toggle).</summary>
        public Action OnStageStructureChanged;

        // ── Data access ──

        public Stage Stage => _stage;

        // ── Segment operations (with Undo/Redo) ──

        public void AddSegment(SegmentType type)
        {
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

            OnStageStructureChanged?.Invoke();
        }

        public void DeleteSegment(ITimelineBlock block)
        {
            if (block?.DataSource is not TimelineSegment seg) return;
            if (_stage.Segments.Count <= 1) return;

            int index = _stage.Segments.IndexOf(seg);
            if (index < 0) return;

            var cmd = ListCommand<TimelineSegment>.Remove(
                _stage.Segments, index, "Delete Segment");
            _commandStack.Execute(cmd);

            OnStageStructureChanged?.Invoke();
        }

        public void ToggleSegmentType(TimelineSegment segment)
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

            OnStageStructureChanged?.Invoke();
        }

        public void CycleTriggerType(TimelineSegment segment)
        {
            if (segment.EntryTrigger == null)
                segment.EntryTrigger = new TriggerCondition();

            var types = (TriggerType[])Enum.GetValues(typeof(TriggerType));
            int current = Array.IndexOf(types, segment.EntryTrigger.Type);
            int next = (current + 1) % types.Length;
            var newType = types[next];

            var cmd = new PropertyChangeCommand<TriggerType>(
                "Change Trigger Type",
                () => segment.EntryTrigger.Type,
                v => segment.EntryTrigger.Type = v,
                newType);
            _commandStack.Execute(cmd);

            OnStageStructureChanged?.Invoke();
        }

        // ── Internal ──

        private void RebuildBlockList()
        {
            _blocks.Clear();
            if (_stage?.Segments == null) return;

            float timeOffset = 0f;
            foreach (var seg in _stage.Segments)
            {
                _blocks.Add(new SegmentBlock(seg, timeOffset));
                timeOffset += seg.Duration;
            }
        }
    }
}
