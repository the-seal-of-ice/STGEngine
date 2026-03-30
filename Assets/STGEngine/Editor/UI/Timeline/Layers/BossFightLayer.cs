using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Core.Serialization;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Runtime;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Timeline layer for a BossFight segment.
    /// Blocks = SpellCard (sequential by TimeLimit) + TransitionBlock between them.
    /// Double-click a SpellCard → SpellCardDetailLayer.
    /// </summary>
    public class BossFightLayer : ITimelineLayer
    {
        private readonly TimelineSegment _segment;
        private readonly STGCatalog _catalog;
        private readonly PatternLibrary _library;
        private readonly string _contextId;
        private readonly List<ITimelineBlock> _blocks = new();
        private readonly List<SpellCard> _loadedSpellCards = new();
        private readonly List<string> _loadedSpellCardIds = new();

        public BossFightLayer(TimelineSegment segment, STGCatalog catalog, PatternLibrary library)
        {
            _segment = segment;
            _catalog = catalog;
            _library = library;
            _contextId = OverrideManager.SegmentContext(segment.Id);
            RebuildBlockList();
        }

        /// <summary>Context ID for override resolution (= segment ID).</summary>
        public string ContextId => _contextId;

        // ── Identity ──

        public string LayerId => $"bossfight:{_segment.Id}";
        public string DisplayName => _segment.Name;

        // ── Block data ──

        public int BlockCount => _blocks.Count;
        public ITimelineBlock GetBlock(int index) => _blocks[index];

        public IReadOnlyList<ITimelineBlock> GetAllBlocks()
        {
            return _blocks;
        }

        /// <summary>
        /// Force a full reload of SpellCard data from disk.
        /// Call this after structural changes (add/remove spell card) or override revert.
        /// </summary>
        public void InvalidateBlocks()
        {
            RebuildBlockList();
        }

        // ── Timeline parameters ──

        public float TotalDuration
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < _loadedSpellCards.Count; i++)
                {
                    total += _loadedSpellCards[i].TimeLimit;
                    if (i < _loadedSpellCards.Count - 1)
                        total += _loadedSpellCards[i].TransitionDuration;
                }
                return total > 0f ? total : _segment.Duration;
            }
        }

        public bool IsSequential => true;

        // ── Interaction ──

        public bool CanAddBlock => true;

        public bool CanDoubleClickEnter(ITimelineBlock block)
        {
            return block is SpellCardBlock;
        }

        public ITimelineLayer CreateChildLayer(ITimelineBlock block)
        {
            if (block is SpellCardBlock scBlock)
            {
                var sc = scBlock.DataSource as SpellCard;
                if (sc != null)
                {
                    var scContext = OverrideManager.SpellCardContext(
                        _segment.Id, scBlock.ListIndex, scBlock.SpellCardId);
                    return new SpellCardDetailLayer(sc, scBlock.SpellCardId, _library, scContext, _catalog);
                }
            }
            return null;
        }

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            var entries = new List<ContextMenuEntry>
            {
                new("Add Spell Card", () => OnAddSpellCardRequested?.Invoke())
            };

            if (selectedBlock is SpellCardBlock scBlock)
            {
                entries.Add(new ContextMenuEntry("Delete Selected Spell Card",
                    () => OnDeleteSpellCardRequested?.Invoke(selectedBlock), true));

                // Override operations — use per-instance context
                var instanceCtx = scBlock.InstanceContextId;
                if (scBlock.IsModified && !string.IsNullOrEmpty(instanceCtx))
                {
                    entries.Add(new ContextMenuEntry("Revert to Original", () =>
                    {
                        OverrideManager.DeleteOverride(instanceCtx, scBlock.SpellCardId);
                        OnOverrideChanged?.Invoke();
                    }));
                    entries.Add(new ContextMenuEntry("Save as New Template...", () =>
                    {
                        OnSaveAsNewTemplateRequested?.Invoke(scBlock.SpellCardId, "spellcard", instanceCtx);
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
                var label = new Label($"BossFight: {_segment.Name}\nSpell Cards: {_segment.SpellCardIds.Count}");
                label.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(label);
                return;
            }

            if (block is SpellCardBlock scBlock)
            {
                var sc = scBlock.DataSource as SpellCard;
                if (sc == null) return;

                var info = new Label($"Spell Card: {scBlock.DisplayLabel}\n" +
                    $"TimeLimit: {sc.TimeLimit:F1}s\n" +
                    $"Health: {sc.Health:F0}\n" +
                    $"Patterns: {sc.Patterns.Count}\n" +
                    $"Transition: {sc.TransitionDuration:F1}s");
                info.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(info);
            }
            else if (block is TransitionBlock)
            {
                var label = new Label("Transition\n(bullet-clear + boss reposition)");
                label.style.color = new Color(0.6f, 0.6f, 0.6f);
                container.Add(label);
            }
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            // Build combined temporary segment from all spell cards (same logic as LoadBossFightPreview)
            // This is a simplified version — full preview with BossPath is handled by TimelineEditorView
            playback?.LoadSegment(null);
        }

        // ── Layer-specific events ──

        public Action OnAddSpellCardRequested;
        public Action<ITimelineBlock> OnDeleteSpellCardRequested;

        /// <summary>Raised when an override is reverted (deleted). UI should refresh.</summary>
        public Action OnOverrideChanged;

        /// <summary>Raised when "Save as New Template" is selected. Args: resourceId, resourceType, instanceContextId.</summary>
        public Action<string, string, string> OnSaveAsNewTemplateRequested;

        // ── Data access ──

        public TimelineSegment Segment => _segment;
        public IReadOnlyList<SpellCard> LoadedSpellCards => _loadedSpellCards;
        public IReadOnlyList<string> LoadedSpellCardIds => _loadedSpellCardIds;

        /// <summary>
        /// Recalculate StartTime for all blocks in sequential order.
        /// Call after any Duration change to keep transitions aligned.
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

        // ── Internal ──

        private void RebuildBlockList()
        {
            _blocks.Clear();
            _loadedSpellCards.Clear();
            _loadedSpellCardIds.Clear();

            if (_segment?.SpellCardIds == null || _catalog == null) return;

            float timeOffset = 0f;

            for (int i = 0; i < _segment.SpellCardIds.Count; i++)
            {
                var scId = _segment.SpellCardIds[i];
                var instanceCtx = OverrideManager.SpellCardInstanceContext(_segment.Id, i);
                var path = OverrideManager.ResolveSpellCardPath(_catalog, instanceCtx, scId);
                if (!System.IO.File.Exists(path)) continue;

                SpellCard sc;
                try
                {
                    sc = YamlSerializer.DeserializeSpellCard(System.IO.File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BossFightLayer] Failed to load '{scId}': {e.Message}");
                    continue;
                }

                _loadedSpellCards.Add(sc);
                _loadedSpellCardIds.Add(scId);

                bool isModified = OverrideManager.HasOverride(instanceCtx, scId);

                // SpellCard block
                _blocks.Add(new SpellCardBlock(sc, scId, timeOffset, isModified, i, instanceCtx));
                timeOffset += sc.TimeLimit;

                // Transition block (between spell cards, not after the last one)
                if (i < _segment.SpellCardIds.Count - 1)
                {
                    _blocks.Add(new TransitionBlock(sc, scId, timeOffset));
                    timeOffset += sc.TransitionDuration;
                }
            }
        }
    }
}
