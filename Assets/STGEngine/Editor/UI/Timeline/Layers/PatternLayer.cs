using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Timeline;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock representing the pattern itself on the PatternLayer timeline.
    /// Shows bullet trajectory thumbnail. Not movable or resizable (fills entire duration).
    /// </summary>
    public class PatternBlock : ITimelineBlock, IModifierThumbnailProvider
    {
        private readonly BulletPattern _pattern;
        private readonly string _patternId;
        private bool _trajectoryComputed;

        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _emitterTrajectories;
        private List<List<TrajectoryThumbnailRenderer.TrajPoint[]>> _perModifierTrajectories;
        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _allModsTrajectories;
        private List<string> _modifierLabels;

        public PatternBlock(BulletPattern pattern, string patternId)
        {
            _pattern = pattern;
            _patternId = patternId;
        }

        public string Id => _patternId;
        public string DisplayLabel => _patternId;

        public float StartTime { get => 0f; set { } }
        public float Duration
        {
            get => _pattern?.Duration > 0f ? _pattern.Duration : 10f;
            set { if (_pattern != null) _pattern.Duration = Mathf.Max(0.1f, value); }
        }

        public Color BlockColor
        {
            get
            {
                int hash = _patternId?.GetHashCode() ?? 0;
                float hue = Mathf.Abs(hash % 360) / 360f;
                return Color.HSVToRGB(hue, 0.5f, 0.6f);
            }
        }

        public bool CanMove => false;
        public float DesignEstimate { get => -1f; set { } }
        public object DataSource => _pattern;
        public bool IsModified => false;

        // ── Thumbnail (emitter only) ──

        public bool HasThumbnail
        {
            get
            {
                if (_pattern != null)
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

        private void EnsureComputed()
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;
            if (_pattern == null) return;

            float sampleDuration = Mathf.Max(10f, (_pattern.Duration > 0f ? _pattern.Duration : 5f) * 3f);
            float modSampleDuration = Mathf.Max(2f, _pattern.Duration > 0f ? _pattern.Duration : 3f);

            _emitterTrajectories = TrajectoryThumbnailRenderer.ComputeEmitterOnly(_pattern, sampleDuration);

            if (_pattern.Modifiers != null && _pattern.Modifiers.Count > 0)
            {
                _perModifierTrajectories = new List<List<TrajectoryThumbnailRenderer.TrajPoint[]>>();
                _modifierLabels = new List<string>();

                foreach (var mod in _pattern.Modifiers)
                {
                    var trajs = TrajectoryThumbnailRenderer.ComputeSingleBulletWithModifier(
                        _pattern, mod, modSampleDuration);
                    _perModifierTrajectories.Add(trajs);
                    _modifierLabels.Add(mod.TypeName);
                }

                _allModsTrajectories = TrajectoryThumbnailRenderer.ComputeAllBulletsAllModifiers(
                    _pattern, modSampleDuration);
            }
        }
    }

    /// <summary>
    /// Leaf timeline layer for a BulletPattern.
    /// Shows a single PatternBlock spanning the full duration with trajectory thumbnail.
    /// PatternEditorView is embedded by TimelineEditorView.
    /// </summary>
    public class PatternLayer : ITimelineLayer
    {
        private readonly BulletPattern _pattern;
        private readonly string _patternId;
        private readonly PatternBlock _block;

        public PatternLayer(BulletPattern pattern, string patternId)
        {
            _pattern = pattern;
            _patternId = patternId;
            _block = new PatternBlock(pattern, patternId);
        }

        // ── Identity ──

        public string LayerId => $"pattern:{_patternId}";
        public string DisplayName => _patternId;

        // ── Block data ──

        public int BlockCount => 1;
        public ITimelineBlock GetBlock(int index) => index == 0 ? _block : null;
        public IReadOnlyList<ITimelineBlock> GetAllBlocks() => new ITimelineBlock[] { _block };

        // ── Timeline parameters ──

        public float TotalDuration => _pattern?.Duration > 0f ? _pattern.Duration : 10f;

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
            // PatternEditorView integration is handled by TimelineEditorView.ShowLayerSummary
            var label = new Label($"Pattern: {_patternId}");
            label.style.color = new Color(0.8f, 0.8f, 0.8f);
            container.Add(label);
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            if (playback == null || _pattern == null)
            {
                playback?.LoadSegment(null);
                return;
            }

            float duration = _pattern.Duration > 0f ? _pattern.Duration : 10f;
            var tempSegment = new TimelineSegment
            {
                Id = $"_pattern_{_patternId}",
                Name = _patternId,
                Type = SegmentType.MidStage,
                Duration = duration
            };
            tempSegment.Events.Add(new SpawnPatternEvent
            {
                Id = $"_pat_evt_{_patternId}",
                StartTime = 0f,
                Duration = duration,
                PatternId = _patternId,
                SpawnPosition = Vector3.zero,
                ResolvedPattern = _pattern
            });

            playback.LoadSegment(tempSegment);
        }

        // ── Data access ──

        public BulletPattern Pattern => _pattern;
        public string PatternId => _patternId;

        /// <summary>Invalidate the block's thumbnail cache (e.g. after pattern edit).</summary>
        public void InvalidateThumbnails() => _block.InvalidateThumbnailCache();
    }
}
