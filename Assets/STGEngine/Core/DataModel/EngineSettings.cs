// ═══════════════════════════════════════════════════════════════════════════
// EngineSettings — Global project configuration
// ═══════════════════════════════════════════════════════════════════════════
//
// ┌─────────────────────────────────────────────────────────────────────────┐
// │  ADDING A NEW CONFIGURATION ITEM? READ THIS FIRST.                     │
// │                                                                         │
// │  Every setting MUST be classified as [GAMEPLAY] or [EDITOR].            │
// │                                                                         │
// │  ── Decision rule ──                                                    │
// │  Ask: "If the editor uses value A and the runtime uses value B,         │
// │        will the same stage behave differently?"                         │
// │    YES → [GAMEPLAY]  → put in GameplaySettings (this file, Core layer)  │
// │    NO  → [EDITOR]    → put in EditorSettings (Editor layer only)        │
// │                                                                         │
// │  ── Extra checks for grey areas ──                                      │
// │  • Affects deterministic replay?       → GAMEPLAY                       │
// │  • Affects collision / graze / scoring? → GAMEPLAY                      │
// │  • Only affects visual presentation?   → EDITOR                         │
// │  • Only used in editor UI code?        → EDITOR                         │
// │                                                                         │
// │  ── Consequences of misclassification ──                                │
// │  GAMEPLAY in EDITOR → editor preview diverges from actual gameplay       │
// │                       ("what you see is NOT what you get")              │
// │  EDITOR in GAMEPLAY → harmless but pollutes runtime data model          │
// │                                                                         │
// │  ── Layer constraint ──                                                 │
// │  GameplaySettings lives in Core (Runtime can reference it).             │
// │  EditorSettings lives in Editor (Runtime CANNOT reference it).          │
// │  If Runtime code needs to read a value → it MUST be GAMEPLAY.           │
// │                                                                         │
// │  ── Storage ──                                                          │
// │  settings.yaml     → GAMEPLAY params → version-controlled (git track)   │
// │  editor_prefs.yaml → EDITOR params   → .gitignore                      │
// │                                                                         │
// │  ── UI ──                                                               │
// │  GAMEPLAY items show ⚙ icon + "Affects gameplay" tooltip in Settings.   │
// │  EDITOR items show 🖥 icon + "Editor only" tooltip.                     │
// │  GAMEPLAY section is displayed ABOVE EDITOR section.                    │
// └─────────────────────────────────────────────────────────────────────────┘
//
// ═══════════════════════════════════════════════════════════════════════════

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// [GAMEPLAY] settings — affect actual gameplay, must be identical between
    /// editor preview and runtime. Stored in settings.yaml (version-controlled).
    /// <para>
    /// When adding a new field:
    /// 1. Add the property with a sensible default.
    /// 2. Mark it with a [GAMEPLAY] XML comment explaining WHY it affects gameplay.
    /// 3. Ensure all consumers (SimulationLoop, Evaluators, etc.) read from here.
    /// 4. Add a UI control in SettingsPanel under the "Gameplay" section.
    /// </para>
    /// </summary>
    public class GameplaySettings
    {
        /// <summary>
        /// [GAMEPLAY] Simulation logic tick rate (ticks per second).
        /// Determines SimulationLoop.FixedDt = 1f / SimulationTickRate.
        /// Higher values improve accuracy of stateful modifiers (Homing, Bounce, Split)
        /// and reduce collision tunneling for fast bullets.
        /// Allowed values: 60, 120, 240, 480.
        /// Editor preview and runtime MUST use the same value.
        /// </summary>
        public int SimulationTickRate { get; set; } = 240;

        /// <summary>
        /// [GAMEPLAY] Maximum concurrent sound effect AudioSources.
        /// Higher values allow more simultaneous SE (bullet impacts, explosions, etc.)
        /// at the cost of CPU. When the limit is reached, the oldest SE is stolen.
        /// Affects audio fidelity during dense bullet patterns.
        /// Allowed values: 8, 16, 24, 32, 48, 64.
        /// </summary>
        public int MaxConcurrentSe { get; set; } = 16;

        // ── Future GAMEPLAY settings ──
        // Uncomment and implement when needed. Each MUST have a [GAMEPLAY] comment.
        //
        // /// <summary>
        // /// [GAMEPLAY] Maximum bullet lifetime in seconds. Bullets older than this
        // /// are destroyed. Affects bullet density, memory usage, and pattern design.
        // /// </summary>
        // public float BulletLifetime { get; set; } = 30f;
        //
        // /// <summary>
        // /// [GAMEPLAY] 3D playfield bounds. Bullets outside are destroyed,
        // /// player movement is clamped. Affects level design and difficulty.
        // /// </summary>
        // public Bounds PlayfieldBounds { get; set; } = new(Vector3.zero, new Vector3(40, 40, 40));
        //
        // /// <summary>
        // /// [GAMEPLAY] Graze detection distance. Bullets passing within this
        // /// distance of the player trigger graze scoring. Affects score system.
        // /// </summary>
        // public float GrazeDistance { get; set; } = 0.5f;
    }
}
