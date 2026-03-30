using System;
using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for an ActionEvent.
    /// Adapts ActionEvent data to the block interface for timeline rendering.
    /// Supports two visual forms:
    ///   - Block (Duration > 0 or Timeout > 0): normal rectangular block
    ///   - Marker (Duration = 0 and Timeout = 0): zero-width diamond marker
    /// Blocking events get hatched fill + ⏸ icon overlay.
    /// </summary>
    public class ActionBlock : ITimelineBlock
    {
        private readonly ActionEvent _event;

        public ActionBlock(ActionEvent evt)
        {
            _event = evt;
        }

        public string Id => _event.Id;

        public string DisplayLabel
        {
            get
            {
                var label = GetActionLabel(_event.ActionType);
                return IsBlocking ? $"|| {label}" : label;
            }
        }

        public float StartTime
        {
            get => _event.StartTime;
            set => _event.StartTime = value;
        }

        public float Duration
        {
            get => _event.Duration;
            set => _event.Duration = value;
        }

        public Color BlockColor => GetActionColor(_event.ActionType);

        public bool CanMove => true;

        public float DesignEstimate { get; set; } = -1f;

        public object DataSource => _event;

        public bool IsModified => false;

        // ── Thumbnail (not used for actions) ──

        public bool HasThumbnail => false;
        public bool ThumbnailInline => false;
        public void DrawThumbnail(UnityEngine.UIElements.Painter2D painter, float w, float h) { }

        // ── Blocking / Marker ──

        /// <summary>Whether this block represents a blocking (timeline-freezing) event.</summary>
        public bool IsBlocking => _event.Blocking;

        /// <summary>
        /// Whether this block should render as a zero-width marker instead of a rectangular block.
        /// True when Duration=0 (instant event or infinite-wait blocker).
        /// </summary>
        public bool IsMarker => _event.Duration <= 0f;

        /// <summary>The underlying ActionEvent data.</summary>
        public ActionEvent ActionEvent => _event;

        // ── Static helpers ──

        private static string GetActionLabel(ActionType type) => type switch
        {
            ActionType.ShowTitle        => "TITLE",
            ActionType.ScreenEffect     => "FX",
            ActionType.BgmControl       => "BGM",
            ActionType.SePlay           => "SE",
            ActionType.BackgroundSwitch => "BG",
            ActionType.BulletClear      => "CLEAR",
            ActionType.ItemDrop         => "ITEM",
            ActionType.AutoCollect      => "COLLECT",
            ActionType.ScoreTally       => "TALLY",
            ActionType.WaitCondition    => "WAIT",
            ActionType.BranchJump       => "BRANCH",
            _ => "ACTION"
        };

        internal static Color GetActionColor(ActionType type) => type switch
        {
            // Presentation — purple/blue
            ActionType.ShowTitle        => HexColor(0x9B59B6),
            ActionType.ScreenEffect     => HexColor(0x8E44AD),
            ActionType.BgmControl       => HexColor(0x2980B9),
            ActionType.SePlay           => HexColor(0x3498DB),
            ActionType.BackgroundSwitch => HexColor(0x1ABC9C),
            // Game logic — orange/yellow
            ActionType.BulletClear      => HexColor(0xE67E22),
            ActionType.ItemDrop         => HexColor(0xF39C12),
            ActionType.AutoCollect      => HexColor(0xD35400),
            ActionType.ScoreTally       => HexColor(0xF1C40F),
            // Flow control — teal/green
            ActionType.WaitCondition    => HexColor(0x16A085),
            ActionType.BranchJump       => HexColor(0x27AE60),
            _ => new Color(0.4f, 0.4f, 0.4f)
        };

        private static Color HexColor(int hex)
        {
            float r = ((hex >> 16) & 0xFF) / 255f;
            float g = ((hex >> 8) & 0xFF) / 255f;
            float b = (hex & 0xFF) / 255f;
            return new Color(r, g, b);
        }
    }
}
