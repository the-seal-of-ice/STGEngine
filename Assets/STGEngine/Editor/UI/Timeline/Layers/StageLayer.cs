using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Core.Serialization;
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
        private STGCatalog _catalog;
        private readonly PatternLibrary _library;
        private readonly CommandStack _commandStack;
        private readonly List<SegmentBlock> _blocks = new();

        // Cache SpellCard TimeLimit to avoid repeated disk IO in BuildThumbnailBars
        private readonly Dictionary<string, (float timeLimit, float transition)> _spellCardCache = new();

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
            return _blocks;
        }

        /// <summary>Force rebuild of block list from stage segments.</summary>
        public void InvalidateBlocks()
        {
            _spellCardCache.Clear();
            RebuildBlockList();
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

        /// <summary>
        /// Recalculate StartTime for all segment blocks in sequential order.
        /// </summary>
        public void RecalcSequentialLayout()
        {
            float timeOffset = 0f;
            foreach (var blk in _blocks)
            {
                blk.StartTime = timeOffset;
                timeOffset += blk.Duration;
            }
        }

        /// <summary>Update catalog reference (may be null at construction, set later).</summary>
        public void SetCatalog(STGCatalog catalog) => _catalog = catalog;

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
                var block = new SegmentBlock(seg, timeOffset);
                block.SetThumbnailBars(BuildThumbnailBars(seg));
                _blocks.Add(block);
                timeOffset += seg.Duration;
            }
        }

        private List<ThumbnailBar> BuildThumbnailBars(TimelineSegment seg)
        {
            var bars = new List<ThumbnailBar>();
            float segDur = seg.Duration;
            if (segDur <= 0f) return bars;

            if (seg.Type == SegmentType.MidStage)
            {
                // Row assignment: greedy non-overlapping
                var sorted = new List<TimelineEvent>(seg.Events);
                sorted.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                var rowEnds = new List<float>();

                foreach (var evt in sorted)
                {
                    int row = -1;
                    for (int r = 0; r < rowEnds.Count; r++)
                    {
                        if (evt.StartTime >= rowEnds[r]) { row = r; rowEnds[r] = evt.EndTime; break; }
                    }
                    if (row < 0) { row = rowEnds.Count; rowEnds.Add(evt.EndTime); }

                    Color color;
                    if (evt is SpawnWaveEvent sw)
                    {
                        int hash = sw.WaveId?.GetHashCode() ?? 0;
                        float hue = 0.3f + Mathf.Abs(hash % 60) / 360f;
                        color = Color.HSVToRGB(hue, 0.5f, 0.55f);
                    }
                    else if (evt is SpawnPatternEvent sp)
                    {
                        int hash = sp.PatternId?.GetHashCode() ?? 0;
                        float hue = Mathf.Abs(hash % 360) / 360f;
                        color = Color.HSVToRGB(hue, 0.5f, 0.6f);
                    }
                    else
                    {
                        color = new Color(0.4f, 0.4f, 0.4f);
                    }

                    bars.Add(new ThumbnailBar
                    {
                        NormalizedStart = Mathf.Clamp01(evt.StartTime / segDur),
                        NormalizedWidth = Mathf.Clamp01(evt.Duration / segDur),
                        AbsoluteStart = evt.StartTime,
                        AbsoluteWidth = evt.Duration,
                        Color = color,
                        Row = row
                    });
                }
            }
            else if (seg.Type == SegmentType.BossFight && _catalog != null)
            {
                // Load SpellCard data (TimeLimit + TransitionDuration) for thumbnail layout
                var scData = new List<(float timeLimit, float transition)>();
                float actualDuration = 0f;
                for (int i = 0; i < seg.SpellCardIds.Count; i++)
                {
                    var data = GetCachedSpellCardData(seg.SpellCardIds[i], seg.Id, i);
                    scData.Add(data);
                    actualDuration += data.timeLimit;
                    if (i < seg.SpellCardIds.Count - 1)
                        actualDuration += data.transition;
                }

                // Use actual content length for thumbnail layout (don't modify seg.Duration)
                float thumbDur = actualDuration > 0f ? actualDuration : segDur;

                float scOffset = 0f;
                for (int i = 0; i < seg.SpellCardIds.Count; i++)
                {
                    var scId = seg.SpellCardIds[i];
                    float timeLimit = scData[i].timeLimit;
                    if (timeLimit <= 0f) continue;

                    int hash = scId?.GetHashCode() ?? 0;
                    float hue = 0.75f + Mathf.Abs(hash % 60) / 360f;
                    var color = Color.HSVToRGB(hue % 1f, 0.45f, 0.55f);

                    bars.Add(new ThumbnailBar
                    {
                        NormalizedStart = Mathf.Clamp01(scOffset / thumbDur),
                        NormalizedWidth = Mathf.Clamp01(timeLimit / thumbDur),
                        AbsoluteStart = scOffset,
                        AbsoluteWidth = timeLimit,
                        Color = color,
                        Row = 0
                    });

                    scOffset += timeLimit;
                    if (i < seg.SpellCardIds.Count - 1)
                        scOffset += scData[i].transition;
                }
            }

            return bars;
        }

        private (float timeLimit, float transition) GetCachedSpellCardData(string scId, string segmentId, int index)
        {
            var cacheKey = $"{segmentId}/sc_{index}/{scId}";
            if (_spellCardCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var contextId = OverrideManager.SpellCardInstanceContext(segmentId, index);
            var path = OverrideManager.ResolveSpellCardPath(_catalog, contextId, scId);
            if (!System.IO.File.Exists(path))
            {
                _spellCardCache[cacheKey] = (0f, 0f);
                return (0f, 0f);
            }

            try
            {
                var sc = YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path));
                var data = (sc.TimeLimit, sc.TransitionDuration);
                _spellCardCache[cacheKey] = data;
                return data;
            }
            catch
            {
                _spellCardCache[cacheKey] = (0f, 0f);
                return (0f, 0f);
            }
        }
    }
}
