using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Runtime;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a SpellCardPattern within a SpellCard.
    /// </summary>
    public class SpellCardPatternBlock : ITimelineBlock
    {
        private readonly SpellCardPattern _pattern;

        public SpellCardPatternBlock(SpellCardPattern pattern)
        {
            _pattern = pattern;
        }

        public string Id => _pattern.PatternId;

        public string DisplayLabel => _pattern.PatternId;

        public float StartTime
        {
            get => _pattern.Delay;
            set => _pattern.Delay = Mathf.Max(0f, value);
        }

        public float Duration
        {
            get => _pattern.Duration;
            set => _pattern.Duration = Mathf.Max(0.1f, value);
        }

        public Color BlockColor
        {
            get
            {
                int hash = _pattern.PatternId?.GetHashCode() ?? 0;
                float hue = Mathf.Abs(hash % 360) / 360f;
                return Color.HSVToRGB(hue, 0.5f, 0.6f);
            }
        }

        public bool CanMove => true; // Free-form within spell card timeline

        public float DesignEstimate { get => -1f; set { } } // ComputedEstimate in Step 2

        public object DataSource => _pattern;
    }

    /// <summary>
    /// Timeline layer for a SpellCard's internal pattern timeline.
    /// Blocks = SpellCardPattern (free-form by Delay, can overlap).
    /// Double-click a pattern → PatternLayer (step 1g).
    /// </summary>
    public class SpellCardDetailLayer : ITimelineLayer
    {
        private readonly SpellCard _spellCard;
        private readonly string _spellCardId;
        private readonly PatternLibrary _library;
        private readonly List<SpellCardPatternBlock> _blocks = new();

        public SpellCardDetailLayer(SpellCard spellCard, string spellCardId, PatternLibrary library)
        {
            _spellCard = spellCard;
            _spellCardId = spellCardId;
            _library = library;
            RebuildBlockList();
        }

        // ── Identity ──

        public string LayerId => $"spellcard:{_spellCardId}";
        public string DisplayName => !string.IsNullOrEmpty(_spellCard.Name) ? _spellCard.Name : _spellCardId;

        // ── Block data ──

        public int BlockCount => _blocks.Count;
        public ITimelineBlock GetBlock(int index) => _blocks[index];

        public IReadOnlyList<ITimelineBlock> GetAllBlocks()
        {
            RebuildBlockList();
            return _blocks;
        }

        // ── Timeline parameters ──

        public float TotalDuration => _spellCard.TimeLimit;

        public bool IsSequential => false;

        // ── Interaction ──

        public bool CanAddBlock => true;

        public bool CanDoubleClickEnter(ITimelineBlock block)
        {
            return block is SpellCardPatternBlock;
        }

        public ITimelineLayer CreateChildLayer(ITimelineBlock block)
        {
            // PatternLayer will be implemented in step 1g
            return null;
        }

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            var entries = new List<ContextMenuEntry>
            {
                new("Add Pattern", () => OnAddPatternRequested?.Invoke(time))
            };

            if (selectedBlock != null)
            {
                entries.Add(new ContextMenuEntry("Delete Selected Pattern",
                    () => OnDeletePatternRequested?.Invoke(selectedBlock), true));
            }

            return entries;
        }

        // ── Properties panel ──

        public void BuildPropertiesPanel(VisualElement container, ITimelineBlock block)
        {
            if (block == null)
            {
                var label = new Label($"SpellCard: {DisplayName}\n" +
                    $"TimeLimit: {_spellCard.TimeLimit:F1}s\n" +
                    $"Health: {_spellCard.Health:F0}\n" +
                    $"Patterns: {_spellCard.Patterns.Count}");
                label.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(label);
                return;
            }

            if (block.DataSource is SpellCardPattern scp)
            {
                var info = new Label($"Pattern: {scp.PatternId}\n" +
                    $"Delay: {scp.Delay:F1}s\n" +
                    $"Duration: {scp.Duration:F1}s\n" +
                    $"Offset: ({scp.Offset.x:F1}, {scp.Offset.y:F1}, {scp.Offset.z:F1})");
                info.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(info);
            }
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            // Preview will be handled by TimelineEditorView's LoadSpellCardPreview
            playback?.LoadSegment(null);
        }

        // ── Layer-specific events ──

        public Action<float> OnAddPatternRequested;
        public Action<ITimelineBlock> OnDeletePatternRequested;

        // ── Data access ──

        public SpellCard SpellCard => _spellCard;
        public string SpellCardId => _spellCardId;

        // ── Internal ──

        private void RebuildBlockList()
        {
            _blocks.Clear();
            if (_spellCard?.Patterns == null) return;

            foreach (var pattern in _spellCard.Patterns)
            {
                _blocks.Add(new SpellCardPatternBlock(pattern));
            }
        }
    }
}
