using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a SpellCard within a BossFight segment.
    /// StartTime is computed from sequential layout (sum of preceding TimeLimit + TransitionDuration).
    /// Duration = TimeLimit (HardLimit). DesignEstimate from SpellCard data.
    /// Thumbnail: draws sub-pattern color bars by Delay.
    /// </summary>
    public class SpellCardBlock : ITimelineBlock
    {
        private readonly SpellCard _spellCard;
        private readonly string _spellCardId;
        private float _startTime;
        private List<ThumbnailBar> _thumbnailBars;

        public SpellCardBlock(SpellCard spellCard, string spellCardId, float startTime)
        {
            _spellCard = spellCard;
            _spellCardId = spellCardId;
            _startTime = startTime;
            BuildThumbnailBars();
        }

        public string Id => _spellCardId;

        public string DisplayLabel => !string.IsNullOrEmpty(_spellCard.Name)
            ? _spellCard.Name
            : _spellCardId;

        public float StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        public float Duration
        {
            get => _spellCard.TimeLimit;
            set => _spellCard.TimeLimit = Mathf.Max(1f, value);
        }

        public Color BlockColor
        {
            get
            {
                int hash = _spellCardId?.GetHashCode() ?? 0;
                float hue = 0.75f + Mathf.Abs(hash % 60) / 360f;
                return Color.HSVToRGB(hue % 1f, 0.45f, 0.55f);
            }
        }

        public bool CanMove => false;

        public float DesignEstimate
        {
            get => _spellCard.DesignEstimate;
            set => _spellCard.DesignEstimate = value;
        }

        public object DataSource => _spellCard;

        /// <summary>The SpellCard ID (for catalog lookups).</summary>
        public string SpellCardId => _spellCardId;

        // ── Thumbnail ──

        public bool HasThumbnail => _thumbnailBars != null && _thumbnailBars.Count > 0;
        public bool ThumbnailInline => false;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_thumbnailBars == null || _thumbnailBars.Count == 0) return;

            float barAreaTop = 14f;
            float barAreaHeight = blockHeight - barAreaTop - 2f;
            if (barAreaHeight < 2f) return;

            // All patterns on row 0 (they can overlap in a spell card)
            float barH = Mathf.Min(barAreaHeight, 6f);

            foreach (var bar in _thumbnailBars)
            {
                float x = bar.NormalizedStart * blockWidth;
                float w = Mathf.Max(bar.NormalizedWidth * blockWidth, 2f);
                float y = barAreaTop + bar.Row * (barH + 1f);
                float h = barH;

                var color = bar.Color;
                color.a = 0.6f;
                painter.fillColor = color;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + w, y));
                painter.LineTo(new Vector2(x + w, y + h));
                painter.LineTo(new Vector2(x, y + h));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private void BuildThumbnailBars()
        {
            _thumbnailBars = new List<ThumbnailBar>();
            if (_spellCard?.Patterns == null || _spellCard.Patterns.Count == 0) return;

            float timeLimit = _spellCard.TimeLimit;
            if (timeLimit <= 0f) return;

            // Row assignment for overlapping patterns
            var sorted = new List<SpellCardPattern>(_spellCard.Patterns);
            sorted.Sort((a, b) => a.Delay.CompareTo(b.Delay));
            var rowEnds = new List<float>();

            foreach (var scp in sorted)
            {
                int row = -1;
                float end = scp.Delay + scp.Duration;
                for (int r = 0; r < rowEnds.Count; r++)
                {
                    if (scp.Delay >= rowEnds[r]) { row = r; rowEnds[r] = end; break; }
                }
                if (row < 0) { row = rowEnds.Count; rowEnds.Add(end); }

                int hash = scp.PatternId?.GetHashCode() ?? 0;
                float hue = Mathf.Abs(hash % 360) / 360f;
                var color = Color.HSVToRGB(hue, 0.5f, 0.6f);

                _thumbnailBars.Add(new ThumbnailBar
                {
                    NormalizedStart = Mathf.Clamp01(scp.Delay / timeLimit),
                    NormalizedWidth = Mathf.Clamp01(scp.Duration / timeLimit),
                    Color = color,
                    Row = row
                });
            }
        }
    }

    /// <summary>
    /// A narrow visual block representing the transition between spell cards
    /// (bullet-clear, boss reposition tween, etc.).
    /// Thumbnail: diagonal hatch lines.
    /// </summary>
    public class TransitionBlock : ITimelineBlock
    {
        private readonly SpellCard _ownerSpellCard;
        private readonly string _ownerId;
        private float _startTime;

        public TransitionBlock(SpellCard ownerSpellCard, string ownerId, float startTime)
        {
            _ownerSpellCard = ownerSpellCard;
            _ownerId = ownerId;
            _startTime = startTime;
        }

        public string Id => $"_transition_{_ownerId}";

        public string DisplayLabel => "\u2192"; // → arrow

        public float StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        public float Duration
        {
            get => _ownerSpellCard.TransitionDuration;
            set => _ownerSpellCard.TransitionDuration = Mathf.Max(0.1f, value);
        }

        public Color BlockColor => new Color(0.35f, 0.35f, 0.4f, 0.7f);

        public bool CanMove => false;

        public float DesignEstimate { get => -1f; set { } }

        public object DataSource => _ownerSpellCard;

        /// <summary>Whether this is a transition block (for special rendering).</summary>
        public bool IsTransition => true;

        // ── Thumbnail: diagonal hatch ──

        public bool HasThumbnail => true;
        public bool ThumbnailInline => false;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            painter.strokeColor = new Color(0.5f, 0.5f, 0.55f, 0.5f);
            painter.lineWidth = 1f;

            float spacing = 6f;
            for (float x = -blockHeight; x < blockWidth + blockHeight; x += spacing)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, blockHeight));
                painter.LineTo(new Vector2(x + blockHeight, 0));
                painter.Stroke();
            }
        }
    }
}
