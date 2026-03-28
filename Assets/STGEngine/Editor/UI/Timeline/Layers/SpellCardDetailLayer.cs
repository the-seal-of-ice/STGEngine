using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Timeline;
using STGEngine.Core.Serialization;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Runtime;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a SpellCardPattern within a SpellCard.
    /// Uses shared TrajectoryThumbnailRenderer for orthographic thumbnails.
    /// Implements IModifierThumbnailProvider for per-modifier icons.
    /// </summary>
    public class SpellCardPatternBlock : ITimelineBlock, IModifierThumbnailProvider
    {
        private readonly SpellCardPattern _pattern;
        private readonly BulletPattern _resolvedPattern;
        private bool _trajectoryComputed;

        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _emitterTrajectories;
        private List<List<TrajectoryThumbnailRenderer.TrajPoint[]>> _perModifierTrajectories;
        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _allModsTrajectories;
        private List<string> _modifierLabels;

        public SpellCardPatternBlock(SpellCardPattern pattern, BulletPattern resolvedPattern = null)
        {
            _pattern = pattern;
            _resolvedPattern = resolvedPattern;
        }

        public string Id => _pattern.PatternId;
        public string DisplayLabel => _resolvedPattern?.Name ?? _pattern.PatternId.Substring(0, Math.Min(8, _pattern.PatternId.Length));

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

        public bool CanMove => true;
        public float DesignEstimate { get => -1f; set { } }
        public object DataSource => _pattern;

        public bool IsModified => false;

        // ── ITimelineBlock Thumbnail (emitter only) ──

        public bool HasThumbnail
        {
            get
            {
                if (_resolvedPattern != null)
                {
                    EnsureComputed();
                    return _emitterTrajectories != null && _emitterTrajectories.Count > 0;
                }
                return false;
            }
        }

        public bool ThumbnailInline => true;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_emitterTrajectories == null || _emitterTrajectories.Count == 0) return;
            TrajectoryThumbnailRenderer.Draw(painter, blockWidth, blockHeight, _emitterTrajectories);
        }

        // ── IModifierThumbnailProvider ──

        public bool HasModifierThumbnails =>
            _perModifierTrajectories != null && _perModifierTrajectories.Count > 0;

        public int ModifierThumbnailCount =>
            _perModifierTrajectories?.Count ?? 0;

        public void DrawModifierThumbnail(Painter2D painter, float w, float h, int modifierIndex)
        {
            if (_perModifierTrajectories == null || modifierIndex < 0 ||
                modifierIndex >= _perModifierTrajectories.Count) return;
            var trajs = _perModifierTrajectories[modifierIndex];
            if (trajs != null && trajs.Count > 0)
                TrajectoryThumbnailRenderer.Draw(painter, w, h, trajs);
        }

        public string GetModifierLabel(int modifierIndex)
        {
            if (_modifierLabels == null || modifierIndex < 0 ||
                modifierIndex >= _modifierLabels.Count) return "?";
            return _modifierLabels[modifierIndex];
        }

        public bool HasAllBulletsThumbnail =>
            _allModsTrajectories != null && _allModsTrajectories.Count > 0;

        public void DrawAllBulletsThumbnail(Painter2D painter, float w, float h)
        {
            if (_allModsTrajectories == null || _allModsTrajectories.Count == 0) return;
            TrajectoryThumbnailRenderer.Draw(painter, w, h, _allModsTrajectories);
        }

        public void InvalidateThumbnailCache()
        {
            _trajectoryComputed = false;
            _emitterTrajectories = null;
            _perModifierTrajectories = null;
            _allModsTrajectories = null;
            _modifierLabels = null;
        }

        // ── Compute ──

        private void EnsureComputed()
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;
            if (_resolvedPattern == null) return;

            float sampleDuration = Mathf.Max(10f, (_pattern.Duration > 0f ? _pattern.Duration : 5f) * 3f);
            float modSampleDuration = Mathf.Max(2f, _pattern.Duration > 0f ? _pattern.Duration : 3f);

            _emitterTrajectories = TrajectoryThumbnailRenderer.ComputeEmitterOnly(_resolvedPattern, sampleDuration);

            if (_resolvedPattern.Modifiers != null && _resolvedPattern.Modifiers.Count > 0)
            {
                _perModifierTrajectories = new List<List<TrajectoryThumbnailRenderer.TrajPoint[]>>();
                _modifierLabels = new List<string>();

                foreach (var mod in _resolvedPattern.Modifiers)
                {
                    var trajs = TrajectoryThumbnailRenderer.ComputeSingleBulletWithModifier(
                        _resolvedPattern, mod, modSampleDuration);
                    _perModifierTrajectories.Add(trajs);
                    _modifierLabels.Add(mod.TypeName);
                }

                _allModsTrajectories = TrajectoryThumbnailRenderer.ComputeAllBulletsAllModifiers(
                    _resolvedPattern, modSampleDuration);
            }
        }
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
        private readonly string _contextId;
        private readonly STGCatalog _catalog;
        private readonly List<SpellCardPatternBlock> _blocks = new();

        public SpellCardDetailLayer(SpellCard spellCard, string spellCardId, PatternLibrary library, string contextId = null, STGCatalog catalog = null)
        {
            _spellCard = spellCard;
            _spellCardId = spellCardId;
            _library = library;
            _contextId = contextId;
            _catalog = catalog;
            RebuildBlockList();
        }

        // ── Identity ──

        public string LayerId => $"spellcard:{_spellCardId}";
        public string DisplayName => !string.IsNullOrEmpty(_spellCard.Name) ? _spellCard.Name : _spellCardId;

        /// <summary>Context ID for override resolution (= "{segmentId}/{spellCardId}").</summary>
        public string ContextId => _contextId;

        // ── Block data ──

        public int BlockCount => _blocks.Count;
        public ITimelineBlock GetBlock(int index) => _blocks[index];

        public IReadOnlyList<ITimelineBlock> GetAllBlocks()
        {
            return _blocks;
        }

        /// <summary>Force rebuild of block list from SpellCard data.</summary>
        public void InvalidateBlocks()
        {
            RebuildBlockList();
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
            if (block is SpellCardPatternBlock scpBlock && scpBlock.DataSource is SpellCardPattern scp)
            {
                BulletPattern resolved = null;

                // Try loading override version first
                if (!string.IsNullOrEmpty(_contextId) && _catalog != null)
                {
                    var overridePath = OverrideManager.ResolvePatternPath(_catalog, _contextId, scp.PatternId);
                    if (overridePath != null && OverrideManager.HasOverride(_contextId, scp.PatternId))
                    {
                        try
                        {
                            resolved = YamlSerializer.DeserializeFromFile(overridePath);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning($"[SpellCardDetailLayer] Failed to load pattern override '{scp.PatternId}': {e.Message}");
                        }
                    }
                }

                // Fallback to library clone
                if (resolved == null)
                    resolved = _library?.ResolveClone(scp.PatternId);

                if (resolved != null)
                    return new PatternLayer(resolved, scp.PatternId);

                Debug.LogWarning($"[SpellCardDetailLayer] Cannot resolve pattern '{scp.PatternId}' — library missing or pattern not found.");
            }
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
            if (playback == null || _spellCard == null) return;

            var tempSegment = new TimelineSegment
            {
                Id = $"_spellcard_{_spellCardId}",
                Name = _spellCard.Name,
                Type = SegmentType.MidStage,
                Duration = _spellCard.TimeLimit
            };

            foreach (var scp in _spellCard.Patterns)
            {
                var pattern = _library?.Resolve(scp.PatternId);
                if (pattern == null) continue;

                var bossPos = TimelineEditorView.EvaluateBossPath(_spellCard.BossPath, scp.Delay);

                tempSegment.Events.Add(new SpawnPatternEvent
                {
                    Id = $"_sc_evt_{scp.PatternId}_{scp.Delay:F0}",
                    StartTime = scp.Delay,
                    Duration = scp.Duration,
                    PatternId = scp.PatternId,
                    SpawnPosition = bossPos + scp.Offset,
                    ResolvedPattern = pattern
                });
            }

            playback.LoadSegment(tempSegment);
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
                var resolved = _library?.Resolve(pattern.PatternId);
                _blocks.Add(new SpellCardPatternBlock(pattern, resolved));
            }
        }
    }
}
