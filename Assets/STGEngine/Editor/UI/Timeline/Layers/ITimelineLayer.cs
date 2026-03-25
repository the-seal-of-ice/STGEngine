using System.Collections.Generic;
using UnityEngine.UIElements;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// Unified abstraction for a timeline layer.
    /// Each layer provides a list of blocks, interaction rules, and UI building callbacks.
    /// TrackAreaView renders any ITimelineLayer without knowing the concrete type.
    ///
    /// Implementations: StageLayer, MidStageLayer, BossFightLayer,
    ///                  SpellCardDetailLayer, WaveLayer, PatternLayer.
    /// </summary>
    public interface ITimelineLayer
    {
        // ── Identity ──

        /// <summary>
        /// Unique identifier for this layer instance.
        /// Examples: "stage:demo_stage", "segment:seg_01", "spellcard:spell_01".
        /// </summary>
        string LayerId { get; }

        /// <summary>Display name shown in the breadcrumb.</summary>
        string DisplayName { get; }

        // ── Block data ──

        /// <summary>Number of blocks in this layer.</summary>
        int BlockCount { get; }

        /// <summary>Get a block by index.</summary>
        ITimelineBlock GetBlock(int index);

        /// <summary>Get all blocks as a list (convenience).</summary>
        IReadOnlyList<ITimelineBlock> GetAllBlocks();

        // ── Timeline parameters ──

        /// <summary>Total duration of this layer's timeline (seconds).</summary>
        float TotalDuration { get; }

        /// <summary>
        /// If true, blocks are arranged in a sequential queue (no overlap, no free move).
        /// Dragging reorders blocks; StartTime is auto-computed.
        /// If false, blocks can overlap and be freely positioned.
        /// </summary>
        bool IsSequential { get; }

        // ── Interaction ──

        /// <summary>Whether new blocks can be added to this layer.</summary>
        bool CanAddBlock { get; }

        /// <summary>Whether the given block supports double-click to enter a child layer.</summary>
        bool CanDoubleClickEnter(ITimelineBlock block);

        /// <summary>
        /// Create the child layer for the given block (called on double-click).
        /// Returns null if the block has no child layer.
        /// </summary>
        ITimelineLayer CreateChildLayer(ITimelineBlock block);

        // ── Context menu ──

        /// <summary>
        /// Get context menu entries for a right-click at the given time position.
        /// The selected block (if any) is provided for context-sensitive entries.
        /// </summary>
        IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock);

        // ── Properties panel ──

        /// <summary>
        /// Build the properties panel UI for the selected block.
        /// Called when a block is selected; container is cleared before calling.
        /// Pass null block to show layer-level properties (or clear the panel).
        /// </summary>
        void BuildPropertiesPanel(VisualElement container, ITimelineBlock block);

        // ── Preview ──

        /// <summary>
        /// Load this layer's preview into the playback controller.
        /// Each layer decides how to map its data to the preview system.
        /// </summary>
        void LoadPreview(TimelinePlaybackController playback);
    }
}
