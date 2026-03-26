using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Runtime;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Timeline layer for a MidStage segment.
    /// Blocks = SpawnPatternEvent / SpawnWaveEvent, freely positioned (can overlap).
    /// Double-click a pattern event → PatternLayer; wave event → WaveLayer.
    /// </summary>
    public class MidStageLayer : ITimelineLayer
    {
        private readonly TimelineSegment _segment;
        private readonly List<EventBlock> _blocks = new();

        /// <summary>Optional: set to enable double-click into Pattern/Wave layers.</summary>
        public PatternLibrary Library { get; set; }
        /// <summary>Optional: set to enable double-click into Wave layers.</summary>
        public STGEngine.Editor.UI.FileManager.STGCatalog Catalog { get; set; }
        /// <summary>Context ID for override resolution (= segment ID).</summary>
        public string ContextId { get; set; }

        public MidStageLayer(TimelineSegment segment)
        {
            _segment = segment;
            RebuildBlockList();
        }

        // ── Identity ──

        public string LayerId => $"segment:{_segment.Id}";
        public string DisplayName => _segment.Name;

        // ── Block data ──

        public int BlockCount => _blocks.Count;

        public ITimelineBlock GetBlock(int index) => _blocks[index];

        public IReadOnlyList<ITimelineBlock> GetAllBlocks()
        {
            RebuildBlockList();
            return _blocks;
        }

        // ── Timeline parameters ──

        public float TotalDuration => _segment.Duration;

        public bool IsSequential => false;

        // ── Interaction ──

        public bool CanAddBlock => true;

        public bool CanDoubleClickEnter(ITimelineBlock block)
        {
            // Pattern events → PatternLayer, Wave events → WaveLayer (both implemented in 1g)
            return block?.DataSource is SpawnPatternEvent or SpawnWaveEvent;
        }

        public ITimelineLayer CreateChildLayer(ITimelineBlock block)
        {
            if (block?.DataSource is SpawnPatternEvent sp && Library != null)
            {
                var pattern = Library.Resolve(sp.PatternId);
                if (pattern != null)
                    return new PatternLayer(pattern, sp.PatternId);
            }
            else if (block?.DataSource is SpawnWaveEvent sw && Catalog != null)
            {
                var path = !string.IsNullOrEmpty(ContextId)
                    ? OverrideManager.ResolveWavePath(Catalog, ContextId, sw.WaveId)
                    : Catalog.GetWavePath(sw.WaveId);
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        var wave = Core.Serialization.YamlSerializer.DeserializeWave(
                            System.IO.File.ReadAllText(path));
                        return new WaveLayer(wave, sw.WaveId);
                    }
                    catch { }
                }
            }
            return null;
        }

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            var entries = new List<ContextMenuEntry>
            {
                new("Add Pattern Event", () => OnAddPatternRequested?.Invoke(time)),
                new("Add Wave Event", () => OnAddWaveRequested?.Invoke(time))
            };

            if (selectedBlock != null)
            {
                entries.Add(new ContextMenuEntry("Delete Selected Event", () => OnDeleteRequested?.Invoke(selectedBlock), true));
            }

            return entries;
        }

        // ── Properties panel ──

        public void BuildPropertiesPanel(VisualElement container, ITimelineBlock block)
        {
            // Properties panel building will be migrated from TimelineEditorView in step 1d.
            // For now, show a minimal label.
            if (block == null)
            {
                container.Add(new Label("Select an event to view properties."));
                return;
            }

            var evt = block.DataSource as TimelineEvent;
            if (evt == null) return;

            var label = new Label($"{block.DisplayLabel}  (t={evt.StartTime:F1}s, dur={evt.Duration:F1}s)");
            label.style.color = new Color(0.8f, 0.8f, 0.8f);
            container.Add(label);
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            playback?.LoadSegment(_segment);
        }

        // ── Layer-specific events (consumed by TimelineEditorView) ──

        /// <summary>Raised when "Add Pattern Event" is selected from context menu.</summary>
        public System.Action<float> OnAddPatternRequested;

        /// <summary>Raised when "Add Wave Event" is selected from context menu.</summary>
        public System.Action<float> OnAddWaveRequested;

        /// <summary>Raised when "Delete Selected Event" is selected from context menu.</summary>
        public System.Action<ITimelineBlock> OnDeleteRequested;

        // ── Internal ──

        /// <summary>The underlying segment data.</summary>
        public TimelineSegment Segment => _segment;

        private void RebuildBlockList()
        {
            _blocks.Clear();
            if (_segment?.Events == null) return;

            foreach (var evt in _segment.Events)
            {
                _blocks.Add(new EventBlock(evt));
            }
        }
    }
}
