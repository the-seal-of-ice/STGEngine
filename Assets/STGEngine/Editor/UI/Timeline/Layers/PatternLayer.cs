using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Leaf timeline layer for a BulletPattern.
    /// No blocks in TrackArea — this layer only provides a properties panel
    /// (PatternEditorView will be shown by TimelineEditorView).
    /// Future: PatternTimeline with firing rhythm keyframes.
    /// </summary>
    public class PatternLayer : ITimelineLayer
    {
        private readonly BulletPattern _pattern;
        private readonly string _patternId;

        public PatternLayer(BulletPattern pattern, string patternId)
        {
            _pattern = pattern;
            _patternId = patternId;
        }

        // ── Identity ──

        public string LayerId => $"pattern:{_patternId}";
        public string DisplayName => _patternId;

        // ── Block data (none — leaf layer) ──

        public int BlockCount => 0;
        public ITimelineBlock GetBlock(int index) => null;
        public IReadOnlyList<ITimelineBlock> GetAllBlocks() => Array.Empty<ITimelineBlock>();

        // ── Timeline parameters ──

        public float TotalDuration => 0f;

        public bool IsSequential => false;

        // ── Interaction ──

        public bool CanAddBlock => false;
        public bool CanDoubleClickEnter(ITimelineBlock block) => false;
        public ITimelineLayer CreateChildLayer(ITimelineBlock block) => null;

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            return Array.Empty<ContextMenuEntry>();
        }

        // ── Properties panel ──

        public void BuildPropertiesPanel(VisualElement container, ITimelineBlock block)
        {
            // PatternEditorView integration will be handled by TimelineEditorView
            var label = new Label($"Pattern: {_patternId}\n(Use the pattern editor panel)");
            label.style.color = new Color(0.8f, 0.8f, 0.8f);
            container.Add(label);
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            // Pattern preview is handled by the single previewer, not the timeline playback
            playback?.LoadSegment(null);
        }

        // ── Data access ──

        public BulletPattern Pattern => _pattern;
        public string PatternId => _patternId;
    }
}
