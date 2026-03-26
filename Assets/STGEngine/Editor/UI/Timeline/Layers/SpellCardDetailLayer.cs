using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Timeline;
using STGEngine.Runtime;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a SpellCardPattern within a SpellCard.
    /// </summary>
    public class SpellCardPatternBlock : ITimelineBlock
    {
        private readonly SpellCardPattern _pattern;
        private readonly BulletPattern _resolvedPattern;

        // Cached trajectory data
        private List<Vector2[]> _trajectoryLines;
        private Rect _trajectoryBounds;
        private bool _trajectoryComputed;

        public SpellCardPatternBlock(SpellCardPattern pattern, BulletPattern resolvedPattern = null)
        {
            _pattern = pattern;
            _resolvedPattern = resolvedPattern;
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

        public bool CanMove => true;

        public float DesignEstimate { get => -1f; set { } }

        public object DataSource => _pattern;

        // ── Thumbnail ──

        public bool HasThumbnail
        {
            get
            {
                if (_resolvedPattern != null)
                {
                    EnsureTrajectoryComputed();
                    return _trajectoryLines != null && _trajectoryLines.Count > 0;
                }
                return false;
            }
        }

        public bool ThumbnailInline => true;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_trajectoryLines == null || _trajectoryLines.Count == 0) return;

            float margin = 2f;
            float drawW = blockWidth - margin * 2;
            float drawH = blockHeight - margin * 2;
            if (drawW < 4f || drawH < 4f) return;

            float bw = _trajectoryBounds.width;
            float bh = _trajectoryBounds.height;
            if (bw < 0.01f) bw = 1f;
            if (bh < 0.01f) bh = 1f;

            float scale = Mathf.Min(drawW / bw, drawH / bh);
            float offsetX = margin + (drawW - bw * scale) * 0.5f;
            float offsetY = margin + (drawH - bh * scale) * 0.5f;

            painter.strokeColor = new Color(0.9f, 0.9f, 0.9f, 0.7f);
            painter.lineWidth = 1f;

            foreach (var line in _trajectoryLines)
            {
                if (line.Length < 2) continue;
                painter.BeginPath();
                for (int i = 0; i < line.Length; i++)
                {
                    float x = offsetX + (line[i].x - _trajectoryBounds.xMin) * scale;
                    float y = offsetY + (line[i].y - _trajectoryBounds.yMin) * scale;
                    if (i == 0) painter.MoveTo(new Vector2(x, y));
                    else painter.LineTo(new Vector2(x, y));
                }
                painter.Stroke();
            }
        }

        private void EnsureTrajectoryComputed()
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;
            if (_resolvedPattern?.Emitter == null) return;

            int bulletCount = _resolvedPattern.Emitter.Count;
            int maxBullets = Mathf.Min(bulletCount, 12);
            int timeSteps = 8;
            float duration = _pattern.Duration > 0f ? _pattern.Duration : 5f;

            _trajectoryLines = new List<Vector2[]>(maxBullets);
            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;

            int step = Mathf.Max(1, bulletCount / maxBullets);

            for (int bi = 0; bi < bulletCount && _trajectoryLines.Count < maxBullets; bi += step)
            {
                var points = new Vector2[timeSteps];
                for (int ti = 0; ti < timeSteps; ti++)
                {
                    float t = (ti / (float)(timeSteps - 1)) * duration;
                    var states = BulletEvaluator.EvaluateAll(_resolvedPattern, t);
                    if (bi < states.Count)
                    {
                        var pos = states[bi].Position;
                        float px = pos.x;
                        float py = -pos.z;
                        points[ti] = new Vector2(px, py);
                        if (px < xMin) xMin = px;
                        if (px > xMax) xMax = px;
                        if (py < yMin) yMin = py;
                        if (py > yMax) yMax = py;
                    }
                    else
                    {
                        points[ti] = ti > 0 ? points[ti - 1] : Vector2.zero;
                    }
                }
                _trajectoryLines.Add(points);
            }

            float pad = 0.5f;
            _trajectoryBounds = new Rect(xMin - pad, yMin - pad,
                (xMax - xMin) + pad * 2, (yMax - yMin) + pad * 2);
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
