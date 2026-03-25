using UnityEngine;
using STGEngine.Core.DataModel;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for a SpellCard within a BossFight segment.
    /// StartTime is computed from sequential layout (sum of preceding TimeLimit + TransitionDuration).
    /// Duration = TimeLimit (HardLimit). DesignEstimate from SpellCard data.
    /// </summary>
    public class SpellCardBlock : ITimelineBlock
    {
        private readonly SpellCard _spellCard;
        private readonly string _spellCardId;
        private float _startTime;

        public SpellCardBlock(SpellCard spellCard, string spellCardId, float startTime)
        {
            _spellCard = spellCard;
            _spellCardId = spellCardId;
            _startTime = startTime;
        }

        public string Id => _spellCardId;

        public string DisplayLabel => !string.IsNullOrEmpty(_spellCard.Name)
            ? _spellCard.Name
            : _spellCardId;

        public float StartTime
        {
            get => _startTime;
            set => _startTime = value; // Recalculated by BossFightLayer on reorder
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
                float hue = 0.75f + Mathf.Abs(hash % 60) / 360f; // Purple-ish range
                return Color.HSVToRGB(hue % 1f, 0.45f, 0.55f);
            }
        }

        public bool CanMove => false; // Sequential queue — drag reorders

        public float DesignEstimate
        {
            get => _spellCard.DesignEstimate;
            set => _spellCard.DesignEstimate = value;
        }

        public object DataSource => _spellCard;

        /// <summary>The SpellCard ID (for catalog lookups).</summary>
        public string SpellCardId => _spellCardId;
    }

    /// <summary>
    /// A narrow visual block representing the transition between spell cards
    /// (bullet-clear, boss reposition tween, etc.).
    /// Not selectable or double-clickable.
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
    }
}
