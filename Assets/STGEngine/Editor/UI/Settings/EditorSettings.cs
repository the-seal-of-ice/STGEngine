// ═══════════════════════════════════════════════════════════════════════════
// EditorSettings — Editor-only preferences (NOT shipped to players)
// ═══════════════════════════════════════════════════════════════════════════
//
// ┌─────────────────────────────────────────────────────────────────────────┐
// │  This file is for [EDITOR] settings ONLY.                               │
// │                                                                         │
// │  If a setting affects actual gameplay (collision, scoring, bullet        │
// │  trajectories, deterministic replay), it belongs in:                     │
// │    Core/DataModel/EngineSettings.cs → GameplaySettings                  │
// │                                                                         │
// │  See the classification rules in EngineSettings.cs header comment.      │
// │                                                                         │
// │  EditorSettings is in the Editor assembly — Runtime code CANNOT         │
// │  reference it. If you find yourself needing a value from here in        │
// │  Runtime code, that's a strong signal it should be GAMEPLAY instead.    │
// │                                                                         │
// │  Storage: editor_prefs.yaml (NOT version-controlled, in .gitignore)     │
// └─────────────────────────────────────────────────────────────────────────┘
//
// ═══════════════════════════════════════════════════════════════════════════

namespace STGEngine.Editor.UI.Settings
{
    /// <summary>
    /// [EDITOR] settings — affect only the editor experience, not shipped to players.
    /// Stored in editor_prefs.yaml (not version-controlled).
    /// <para>
    /// When adding a new field:
    /// 1. Confirm it does NOT affect gameplay (see rules in EngineSettings.cs).
    /// 2. Add the property with a sensible default.
    /// 3. Mark it with a [EDITOR] XML comment explaining what it controls.
    /// 4. Add a UI control in SettingsPanel under the "Editor" section.
    /// 5. If Runtime code needs this value, STOP — move it to GameplaySettings.
    /// </para>
    /// </summary>
    public class EditorSettings
    {
        /// <summary>
        /// [EDITOR] Preview rendering FPS limit. Caps the editor's render frame rate
        /// to reduce CPU/GPU load. Does NOT affect simulation tick rate (logic frames
        /// are decoupled from render frames via SimulationLoop).
        /// 0 = unlimited.
        /// </summary>
        public int PreviewFpsLimit { get; set; } = 60;

        /// <summary>
        /// [EDITOR] Initial size of the PatternPreviewer object pool.
        /// Controls how many patterns can be previewed simultaneously before
        /// dynamic allocation kicks in. Runtime has its own pool management.
        /// </summary>
        public int PreviewerPoolSize { get; set; } = 6;

        /// <summary>
        /// [EDITOR] Duration (seconds) used for trajectory thumbnail sampling.
        /// Longer = more detailed thumbnails but slower to compute.
        /// Only affects the visual quality of block thumbnails in the timeline.
        /// </summary>
        public float ThumbnailSampleDuration { get; set; } = 10f;

        // ── Future EDITOR settings ──
        // Uncomment and implement when needed. Each MUST have an [EDITOR] comment.
        //
        // /// <summary>
        // /// [EDITOR] Auto-save interval in seconds. 0 = disabled.
        // /// Only affects the editor's periodic save behavior.
        // /// </summary>
        // public float AutoSaveInterval { get; set; } = 60f;
        //
        // /// <summary>
        // /// [EDITOR] Whether to show modifier thumbnail popups on hover.
        // /// Pure UI preference, no gameplay impact.
        // /// </summary>
        // public bool ShowModifierThumbnails { get; set; } = true;
    }
}
