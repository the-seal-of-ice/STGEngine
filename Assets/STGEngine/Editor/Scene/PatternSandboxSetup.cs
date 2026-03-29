using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;
using STGEngine.Editor.UI;
using STGEngine.Editor.UI.AssetLibrary;
using STGEngine.Editor.UI.FileManager;
using STGEngine.Editor.UI.Settings;
using STGEngine.Editor.UI.Timeline;
using STGEngine.Runtime;
using STGEngine.Runtime.Player;
using STGEngine.Runtime.Preview;
using STGEngine.Runtime.Rendering;

namespace STGEngine.Editor.Scene
{
    /// <summary>
    /// Editor mode: single pattern editing or timeline editing.
    /// </summary>
    public enum EditorMode
    {
        PatternEdit,
        TimelineEdit
    }

    /// <summary>
    /// Bootstraps the PatternSandbox scene. Supports two modes:
    /// - PatternEdit: single pattern preview + editor (Phase 1/2 behavior)
    /// - TimelineEdit: multi-pattern timeline editing (Phase 3)
    /// </summary>
    [AddComponentMenu("STGEngine/Pattern Sandbox Setup")]
    public class PatternSandboxSetup : MonoBehaviour
    {
        [Header("Editor Mode")]
        [SerializeField] private EditorMode _editorMode = EditorMode.PatternEdit;

        [Header("References (auto-created if null)")]
        [SerializeField] private PatternPreviewer _previewer;
        [SerializeField] private UIDocument _uiDocument;

        [Header("UI")]
        [Tooltip("PanelSettings asset with theme. If null, loads from Resources.")]
        [SerializeField] private PanelSettings _panelSettings;

        [Header("Bullet Visuals")]
        [Tooltip("Bullet mesh. If null, a sphere primitive is created at runtime.")]
        [SerializeField] private Mesh _bulletMesh;
        [Tooltip("Bullet material. If null, created from STGEngine/BulletInstanced shader.")]
        [SerializeField] private Material _bulletMaterial;

        [Header("Default Pattern")]
        [Tooltip("Load demo YAML from Resources on start.")]
        [SerializeField] private bool _loadDemoYaml = true;
        [Tooltip("Default demo pattern to load.")]
        [SerializeField] private string _defaultDemoPattern = "demo_sphere_homing";

        // Pattern Edit mode
        private PatternEditorView _editorView;

        // Timeline Edit mode
        private TimelineEditorView _timelineView;
        private TimelinePlaybackController _timelinePlayback;
        private PreviewerPool _previewerPool;
        private PatternLibrary _patternLibrary;

        // Timeline layout
        private float _timelineTopPercent;
        private VisualElement _timelinePanel;
        private VisualElement _propertyFloatPanel;
        private VisualElement _uiRoot; // UIDocument root for height calculations
        private bool _isDragging;
        private const float HandleHeight = 6f;
        private const float ToolbarHeight = 30f;
        private const float MinTopPercent = 15f;

        // Asset library
        private AssetLibraryPanel _assetLibrary;
        private STGCatalog _catalog;

        // Boss placeholder
        private BossPlaceholder _bossPlaceholder;
        private SpellCard _activeBossSpellCard;

        // Enemy placeholders
        private readonly List<EnemyPlaceholder> _enemyPlaceholders = new();

        // Pause menu + Settings
        private PauseMenuPanel _pauseMenu;
        private SettingsPanel _settingsPanel;
        private bool _wasPlayingBeforePause;
        private List<WavePlaceholderData> _activeWaves;

        // Player mode
        private PlayerController _playerController;
        private SimulatedPlayer _simulatedPlayer;
        private IPlayerProvider _activePlayer; // 当前活跃的玩家（真人或 AI）
        private PlayerCamera _playerCamera;
        private bool _playerModeActive;
        private bool _playerModeIsAI;
        private Label _playerHudLabel;

        /// <summary>Current editor mode.</summary>
        public EditorMode CurrentMode => _editorMode;

        /// <summary>
        /// Static override: survives scene reload so mode switch actually works.
        /// Null means "use the serialized _editorMode value".
        /// </summary>
        private static EditorMode? _pendingModeOverride;

        /// <summary>All available demo pattern names in Resources/DefaultPatterns.</summary>
        private static readonly string[] DemoPatterns = new[]
        {
            "demo_ring_wave",
            "demo_sphere_homing",
            "demo_cone_bounce",
            "demo_ring_split",
        };

        private void Awake()
        {
            // Load global settings (gameplay + editor prefs)
            EngineSettingsManager.Load();

            // Apply initial editor FPS limit
            var editorPrefs = EngineSettingsManager.Editor;
            Application.targetFrameRate = editorPrefs.PreviewFpsLimit > 0 ? editorPrefs.PreviewFpsLimit : -1;

            // Apply pending mode override from previous scene load
            if (_pendingModeOverride.HasValue)
            {
                _editorMode = _pendingModeOverride.Value;
                _pendingModeOverride = null;
            }

            EnsureBulletVisuals();
            EnsureUIDocument();
            EnsureCamera();
            EnsureBoundary();

            if (_editorMode == EditorMode.PatternEdit)
            {
                EnsurePreviewer();
            }
            else
            {
                // Timeline mode: create previewer pool + playback controller
                EnsurePreviewer(); // Keep one for property panel editing
                _patternLibrary = new PatternLibrary();
                _previewerPool = new PreviewerPool(transform, _bulletMesh, _bulletMaterial, 6);
                _timelinePlayback = new TimelinePlaybackController();
                _timelinePlayback.Initialize(_previewerPool, _patternLibrary);

                // Boss placeholder (hidden until spell card editing)
                var bossGo = new GameObject("BossPlaceholder");
                _bossPlaceholder = bossGo.AddComponent<BossPlaceholder>();

                // Apply initial tick rate from settings
                ApplyTickRate(EngineSettingsManager.Gameplay.SimulationTickRate);
            }

            // Subscribe to settings changes for live updates
            EngineSettingsManager.OnSettingsChanged += OnSettingsChanged;
        }

        private void Start()
        {
            var root = _uiDocument.rootVisualElement;
            // TemplateContainer has no intrinsic height by default, which breaks
            // percentage-based child positioning (top/height %).
            root.style.width  = Length.Percent(100);
            root.style.height = Length.Percent(100);

            if (_editorMode == EditorMode.PatternEdit)
            {
                StartPatternEditMode(root);
            }
            else
            {
                StartTimelineEditMode(root);
            }
        }

        private void Update()
        {
            if (_editorMode == EditorMode.TimelineEdit && _timelinePlayback != null)
            {
                _timelinePlayback.Tick(Time.deltaTime);
            }

            // Drive Boss placeholder along path — synced to playback time
            if (_bossPlaceholder != null && _bossPlaceholder.IsVisible && _activeBossSpellCard != null
                && _timelinePlayback != null)
            {
                _bossPlaceholder.SetTime(_timelinePlayback.CurrentTime);
            }

            // Drive Enemy placeholders along paths — synced to playback time
            if (_activeWaves != null && _timelinePlayback != null)
            {
                float t = _timelinePlayback.CurrentTime;
                foreach (var ep in _enemyPlaceholders)
                    ep.SetTime(t);
            }

            // ── Player mode update (takes priority over all editor shortcuts) ──
            if (_playerModeActive)
            {
                UpdatePlayerMode();
                if (_playerModeIsAI && _simulatedPlayer != null)
                    _simulatedPlayer.FixedTick(Time.deltaTime);
                else if (!_playerModeIsAI && _playerController != null)
                    _playerController.FixedTick(Time.deltaTime);
                return; // Skip all editor shortcuts while in player mode
            }

            // Global keyboard shortcuts — works even when scene viewport has focus
            if (_editorMode == EditorMode.TimelineEdit && _timelineView != null)
            {
                // Escape key: toggle pause menu / close settings
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_settingsPanel != null && _settingsPanel.IsOpen)
                    {
                        _settingsPanel.Close();
                        _pauseMenu?.Open();
                    }
                    else if (_pauseMenu != null)
                    {
                        if (!_pauseMenu.IsOpen)
                        {
                            // Pause playback and open menu
                            _wasPlayingBeforePause = _timelinePlayback?.IsPlaying ?? false;
                            _timelinePlayback?.Pause();
                        }
                        _pauseMenu.Toggle();
                    }
                }
                // Other shortcuts only when menus are closed
                else if (_pauseMenu == null || !_pauseMenu.IsOpen)
                {
                    if (_settingsPanel == null || !_settingsPanel.IsOpen)
                    {
                        PollGlobalShortcuts();
                    }
                }
            }
        }

        /// <summary>
        /// Poll Unity Input for keyboard shortcuts and forward to TimelineEditorView.
        /// This ensures shortcuts like Space (play/pause), Ctrl+Z (undo), etc. work
        /// even when the UI Toolkit panel does not have focus (e.g. user clicked the scene).
        /// Only fires on GetKeyDown (single press), not held keys.
        /// Skips when a text input field has focus to avoid interfering with typing.
        /// </summary>
        private void PollGlobalShortcuts()
        {
            // Skip if a UI Toolkit element has focus — the OnKeyDown callback handles it
            var focused = _uiRoot?.panel?.focusController?.focusedElement;
            if (focused != null)
                return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                     || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Check each shortcut key
            KeyCode[] shortcutKeys = {
                KeyCode.Space, KeyCode.Delete, KeyCode.Backspace,
                KeyCode.Z, KeyCode.Y, KeyCode.S, KeyCode.C, KeyCode.V, KeyCode.D
            };

            foreach (var key in shortcutKeys)
            {
                if (Input.GetKeyDown(key))
                {
                    _timelineView.HandleKeyboardShortcut(key, ctrl, shift);
                    break; // one key per frame is enough
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up player mode if active
            if (_playerModeActive)
                ExitPlayerMode();

            EngineSettingsManager.OnSettingsChanged -= OnSettingsChanged;
            _editorView?.Dispose();
            _timelineView?.Dispose();
            _previewerPool?.Dispose();
            ClearEnemyPlaceholders();
        }

        // ─── Settings Live Apply ───

        private void OnSettingsChanged()
        {
            var gameplay = EngineSettingsManager.Gameplay;
            var editor = EngineSettingsManager.Editor;

            // [GAMEPLAY] Tick rate → SimulationLoop.FixedDt on all controllers
            ApplyTickRate(gameplay.SimulationTickRate);

            // [EDITOR] Preview FPS limit → Application.targetFrameRate
            Application.targetFrameRate = editor.PreviewFpsLimit > 0 ? editor.PreviewFpsLimit : -1;
        }

        private void ApplyTickRate(int tickRate)
        {
            float fixedDt = 1f / Mathf.Max(1, tickRate);

            // Timeline playback controller (outer loop + all active previewers)
            if (_timelinePlayback != null)
                _timelinePlayback.FixedDt = fixedDt;

            // Single previewer (pattern edit mode / property panel preview)
            if (_previewer != null)
                _previewer.Playback.FixedDt = fixedDt;
        }

        // ─── Pattern Edit Mode ───

        private void StartPatternEditMode(VisualElement root)
        {
            _editorView = new PatternEditorView(_previewer);
            _editorView.OnMeshTypeChanged += mt =>
            {
                EnsureBulletVisuals(mt);
                _previewer.SetBulletVisuals(_bulletMesh, _bulletMaterial);
            };

            // Mode switch button at top-left
            BuildModeSwitch(root);

            // Demo pattern selector
            BuildDemoSelector(root);

            // Editor panel on the right side
            var panel = _editorView.Root;
            panel.style.position = Position.Absolute;
            panel.style.right = 0;
            panel.style.top = 0;
            panel.style.bottom = 0;
            root.Add(panel);

            if (_loadDemoYaml)
                LoadDemoPattern();
        }

        // ─── Timeline Edit Mode ───

        private void StartTimelineEditMode(VisualElement root)
        {
            _catalog = STGCatalog.Load();

            _timelineView = new TimelineEditorView(_timelinePlayback, _patternLibrary, _previewer);
            _timelineView.SetCatalog(_catalog);
            _timelineView.OnSpellCardEditingChanged += OnSpellCardEditingChanged;
            _timelineView.OnWaveEditingChanged += OnWaveEditingChanged;
            _timelineView.OnLayerChanged += () => _assetLibrary?.RefreshButtonStates();
            _timelineView.OnPlayerModeRequested += () => TogglePlayerMode(false);
            _timelineView.OnPlayerAIModeRequested += () => TogglePlayerMode(true);
            _timelineView.OnMeshTypeChanged += mt =>
            {
                EnsureBulletVisuals(mt);
                _previewer.SetBulletVisuals(_bulletMesh, _bulletMaterial);
                _previewerPool?.UpdateVisuals(_bulletMesh, _bulletMaterial);
            };

            // In Timeline mode the single previewer is only used as a data-binding
            // target for the property panel's PatternEditorView. Disable it so its
            // Update() loop doesn't run and it doesn't render bullets independently
            // — all bullet rendering is handled by the PreviewerPool.
            _previewer.enabled = false;

            // Mode switch button at top-left
            BuildModeSwitch(root);

            _uiRoot = root;
            _timelineTopPercent = 70f;

            // ── Drag handle (resize bar) ──
            var handle = new VisualElement();
            handle.style.position = Position.Absolute;
            handle.style.left = 0;
            handle.style.right = 0;
            handle.style.height = HandleHeight;
            // Place handle just above the timeline panel
            handle.style.top = Length.Percent(_timelineTopPercent);
            handle.style.marginTop = -HandleHeight;
            handle.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            // Hover highlight
            handle.RegisterCallback<MouseEnterEvent>(_ =>
                handle.style.backgroundColor = new Color(0.5f, 0.7f, 1f, 0.8f));
            handle.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!_isDragging)
                    handle.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            });

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _isDragging = true;
                handle.CapturePointer(evt.pointerId);
                handle.style.backgroundColor = new Color(0.5f, 0.7f, 1f, 0.8f);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isDragging) return;
                var rootHeight = _uiRoot.resolvedStyle.height;
                if (rootHeight <= 0f) return;

                // Max top%: leave room for handle + toolbar
                float maxTopPercent = ((rootHeight - HandleHeight - ToolbarHeight) / rootHeight) * 100f;
                float newPercent = (evt.position.y / rootHeight) * 100f;
                newPercent = Mathf.Clamp(newPercent, MinTopPercent, maxTopPercent);

                ApplyTimelineTop(newPercent);
                // Move handle itself
                handle.style.top = Length.Percent(newPercent);
                handle.style.marginTop = -HandleHeight;

                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                handle.ReleasePointer(evt.pointerId);
                handle.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
                evt.StopPropagation();
            });
            root.Add(handle);

            // ── Timeline panel ──
            var panel = _timelineView.Root;
            panel.style.position = Position.Absolute;
            panel.style.left = 0;
            panel.style.right = 0;
            panel.style.top = Length.Percent(_timelineTopPercent);
            panel.style.bottom = 0;
            root.Add(panel);

            // Property panel: floating right-side overlay above the timeline
            var propPanel = _timelineView.PropertyPanel;
            propPanel.style.position = Position.Absolute;
            propPanel.style.right = 0;
            propPanel.style.top = 0;
            propPanel.style.bottom = Length.Percent(100f - _timelineTopPercent);
            root.Add(propPanel);

            _timelinePanel = panel;
            _propertyFloatPanel = propPanel;

            // ── Asset Library Panel (left side, above timeline) ──
            _assetLibrary = new AssetLibraryPanel();
            _assetLibrary.Refresh(_catalog);
            _assetLibrary.OnAssetSelected += OnAssetSelected;
            _assetLibrary.OnAssetAddRequested += OnAssetAddToTimeline;
            _assetLibrary.OnCatalogChanged += OnCatalogChanged;
            _assetLibrary.CanAddToTimeline = CanAddAssetToTimeline;

            var libraryPanel = _assetLibrary.Root;
            libraryPanel.style.position = Position.Absolute;
            libraryPanel.style.left = 0;
            libraryPanel.style.top = 0;
            libraryPanel.style.bottom = Length.Percent(100f - _timelineTopPercent);
            root.Add(libraryPanel);

            // Force theme override after Unity Runtime Theme has been applied
            StartCoroutine(ForceTimelineTheme());

            // Initial button state refresh (SetStage ran before _assetLibrary existed)
            _assetLibrary.RefreshButtonStates();

            // Initial placeholder notification (events are now subscribed, but SetStage
            // already ran during construction before subscription — trigger once now)
            _timelineView.NotifyWavePlaceholders();

            // ── Pause Menu + Settings (must be last — renders on top of everything) ──
            _settingsPanel = new SettingsPanel();
            _settingsPanel.OnBackRequested += () =>
            {
                _pauseMenu.Open(); // return to pause menu
            };
            root.Add(_settingsPanel.Root);

            _pauseMenu = new PauseMenuPanel();
            _pauseMenu.OnResumeRequested += () =>
            {
                // Resume playback if it was playing before pause
                if (_wasPlayingBeforePause && _timelinePlayback != null)
                    _timelinePlayback.Play();
            };
            _pauseMenu.OnSettingsRequested += () =>
            {
                _pauseMenu.Close(); // hide pause menu (don't resume playback)
                _settingsPanel.Open();
            };
            root.Add(_pauseMenu.Root);
        }

        /// <summary>
        /// Update timeline top position and sync property panel + minimize state.
        /// </summary>
        private void ApplyTimelineTop(float topPercent)
        {
            _timelineTopPercent = topPercent;
            _timelinePanel.style.top = Length.Percent(topPercent);
            _propertyFloatPanel.style.bottom = Length.Percent(100f - topPercent);

            // Sync asset library panel bottom
            if (_assetLibrary != null)
                _assetLibrary.Root.style.bottom = Length.Percent(100f - topPercent);

            // When nearly collapsed (< 8% remaining), minimize to toolbar-only
            float rootHeight = _uiRoot.resolvedStyle.height;
            float remainingPx = rootHeight * (1f - topPercent / 100f);
            bool minimized = remainingPx < HandleHeight + ToolbarHeight + 60f; // 60px threshold for breadcrumb+content
            _timelineView.SetMinimized(minimized);
        }

        private IEnumerator ForceTimelineTheme()
        {
            // Unity Runtime Theme applies over multiple frames; keep overriding
            yield return null;                     // frame 1
            _timelineView.ForceApplyTheme();
            _assetLibrary?.ForceApplyTheme();
            yield return null;                     // frame 2
            _timelineView.ForceApplyTheme();
            _assetLibrary?.ForceApplyTheme();
            yield return new WaitForSeconds(0.1f); // 100ms
            _timelineView.ForceApplyTheme();
            _assetLibrary?.ForceApplyTheme();
            yield return new WaitForSeconds(0.3f); // 300ms
            _timelineView.ForceApplyTheme();
            _assetLibrary?.ForceApplyTheme();
            yield return new WaitForSeconds(0.5f); // 500ms — final pass
            _timelineView.ForceApplyTheme();
            _assetLibrary?.ForceApplyTheme();
        }

        // ─── Mode Switch ───

        private void BuildModeSwitch(VisualElement root)
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = 10;
            bar.style.top = 10;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;

            var modeLabel = new Label("Mode:");
            modeLabel.style.color = Color.white;
            modeLabel.style.marginRight = 6;
            bar.Add(modeLabel);

            var modes = new List<string> { "Pattern", "Timeline" };
            var modeDropdown = new DropdownField(modes,
                _editorMode == EditorMode.PatternEdit ? 0 : 1);
            modeDropdown.style.width = 100;
            modeDropdown.RegisterValueChangedCallback(evt =>
            {
                var newMode = evt.newValue == "Timeline"
                    ? EditorMode.TimelineEdit
                    : EditorMode.PatternEdit;
                if (newMode != _editorMode)
                {
                    _pendingModeOverride = newMode;
                    // Reload scene to switch mode
                    UnityEngine.SceneManagement.SceneManager.LoadScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                }
            });
            bar.Add(modeDropdown);

            root.Add(bar);
        }

        // ─── Setup Helpers ───

        private void EnsureBulletVisuals()
        {
            EnsureBulletVisuals(MeshType.Sphere);
        }

        /// <summary>
        /// Create or update bullet visuals for the given mesh type.
        /// </summary>
        public void EnsureBulletVisuals(MeshType meshType)
        {
            _bulletMesh = BulletMeshFactory.Create(meshType);

            if (_bulletMaterial == null)
            {
                var shader = Shader.Find("STGEngine/BulletInstanced");
                if (shader == null)
                {
                    Debug.LogWarning("[SandboxSetup] Shader 'STGEngine/BulletInstanced' not found, " +
                        "falling back to URP/Lit.");
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }
                _bulletMaterial = new Material(shader);
                _bulletMaterial.enableInstancing = true;
            }
        }

        private void EnsurePreviewer()
        {
            if (_previewer == null)
            {
                _previewer = GetComponent<PatternPreviewer>();
                if (_previewer == null)
                    _previewer = gameObject.AddComponent<PatternPreviewer>();
            }

            _previewer.SetBulletVisuals(_bulletMesh, _bulletMaterial);
        }

        private void EnsureUIDocument()
        {
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
                if (_uiDocument == null)
                    _uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (_uiDocument.panelSettings == null)
            {
                if (_panelSettings != null)
                {
                    _uiDocument.panelSettings = _panelSettings;
                }
                else
                {
                    var loaded = Resources.Load<PanelSettings>("DefaultPanelSettings");
                    if (loaded != null)
                    {
                        _uiDocument.panelSettings = loaded;
                    }
                    else
                    {
                        var ps = ScriptableObject.CreateInstance<PanelSettings>();
                        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                        ps.referenceResolution = new Vector2Int(1920, 1080);
                        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                        ps.match = 0.5f;
                        _uiDocument.panelSettings = ps;
                        Debug.LogWarning("[SandboxSetup] No PanelSettings asset found. " +
                            "UI may not render correctly. Assign one in Inspector.");
                    }
                }
            }
        }

        private void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (cam.GetComponent<FreeCameraController>() == null)
                cam.gameObject.AddComponent<FreeCameraController>();

            cam.transform.position = new Vector3(0f, 8f, -15f);
            cam.transform.LookAt(Vector3.zero);
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void EnsureBoundary()
        {
            if (FindAnyObjectByType<SandboxBoundary>() == null)
            {
                var go = new GameObject("SandboxBoundary");
                go.AddComponent<SandboxBoundary>();
            }
        }

        // ─── Player Mode ───

        private void TogglePlayerMode(bool aiMode)
        {
            if (_playerModeActive)
            {
                ExitPlayerMode();
                return;
            }
            EnterPlayerMode(aiMode);
        }

        private void EnterPlayerMode(bool aiMode)
        {
            _playerModeActive = true;
            _playerModeIsAI = aiMode;

            // Manual mode: suppress shortcuts + disable UI (keys belong to player)
            // AI mode: keep UI fully interactive (user can edit while AI runs)
            if (!aiMode)
            {
                if (_timelineView != null) _timelineView.SuppressShortcuts = true;
                if (_uiRoot != null)
                {
                    (_uiRoot.focusController?.focusedElement as VisualElement)?.Blur();
                    _uiRoot.pickingMode = PickingMode.Ignore;
                    _uiRoot.SetEnabled(false);
                }
            }

            var cam = Camera.main;
            if (cam == null) return;

            // Disable free camera
            var freeCam = cam.GetComponent<FreeCameraController>();
            if (freeCam != null) freeCam.enabled = false;

            // Build bullet provider
            System.Func<IReadOnlyList<STGEngine.Runtime.Bullet.BulletState>> bulletProvider = null;
            if (_editorMode == EditorMode.PatternEdit && _previewer != null)
            {
                bulletProvider = () => _previewer.CurrentStates;
            }
            else if (_editorMode == EditorMode.TimelineEdit && _timelinePlayback != null)
            {
                bulletProvider = () =>
                {
                    var all = new List<STGEngine.Runtime.Bullet.BulletState>();
                    foreach (var ae in _timelinePlayback.ActiveEvents)
                    {
                        var states = ae.Previewer?.CurrentStates;
                        if (states != null) all.AddRange(states);
                    }
                    return all;
                };
            }

            if (aiMode)
            {
                // ── AI Simulated Player ──
                var playerGo = new GameObject("SimulatedPlayer");
                playerGo.transform.position = new Vector3(0f, 0f, -5f);
                _simulatedPlayer = playerGo.AddComponent<SimulatedPlayer>();

                var brain = new RandomWalkBrain
                {
                    Seed = UnityEngine.Random.Range(0, int.MaxValue),
                    WanderInterval = 1.5f,
                    SlowdownTendency = 0.3f,
                    BoundaryAvoidance = 0.6f
                };
                _simulatedPlayer.Initialize(brain, bulletProvider);
                _activePlayer = _simulatedPlayer;

                Debug.Log($"[PatternSandbox] AI Player mode ON — Seed: {brain.Seed}, ESC to exit");
            }
            else
            {
                // ── Manual Player ──
                var playerGo = new GameObject("Player");
                playerGo.transform.position = new Vector3(0f, 0f, -5f);

                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.transform.SetParent(playerGo.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localScale = Vector3.one * 0.4f;
                var col = visual.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                var renderer = visual.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color(0.3f, 0.8f, 1f);

                _playerController = playerGo.AddComponent<PlayerController>();

                // Setup camera
                _playerCamera = cam.gameObject.GetComponent<PlayerCamera>();
                if (_playerCamera == null)
                    _playerCamera = cam.gameObject.AddComponent<PlayerCamera>();
                _playerCamera.enabled = true;

                _playerController.Initialize(_playerCamera, bulletProvider);
                _playerCamera.SetCursorLock(true);
                _activePlayer = _playerController;

                Debug.Log("[PatternSandbox] Manual Player mode ON — WASD move, Mouse aim, Ctrl slow, ESC exit");
            }

            // HUD
            if (_playerHudLabel == null && _uiDocument != null)
            {
                _playerHudLabel = new Label();
                _playerHudLabel.style.position = Position.Absolute;
                _playerHudLabel.style.right = 10;
                _playerHudLabel.style.top = 10;
                _playerHudLabel.style.color = new Color(0.3f, 1f, 0.5f);
                _playerHudLabel.style.fontSize = 12;
                _playerHudLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _playerHudLabel.style.backgroundColor = new Color(0, 0, 0, 0.5f);
                _playerHudLabel.style.paddingLeft = 6;
                _playerHudLabel.style.paddingRight = 6;
                _playerHudLabel.style.paddingTop = 3;
                _playerHudLabel.style.paddingBottom = 3;
                _playerHudLabel.style.borderTopLeftRadius = _playerHudLabel.style.borderTopRightRadius =
                    _playerHudLabel.style.borderBottomLeftRadius = _playerHudLabel.style.borderBottomRightRadius = 4;
                _uiDocument.rootVisualElement.Add(_playerHudLabel);
            }
            if (_playerHudLabel != null)
            {
                _playerHudLabel.style.display = DisplayStyle.Flex;
                _playerHudLabel.SetEnabled(true);
            }
        }

        private void ExitPlayerMode()
        {
            _playerModeActive = false;
            _activePlayer = null;

            // Restore editor shortcuts + UI (only needed for manual mode)
            if (!_playerModeIsAI)
            {
                if (_timelineView != null) _timelineView.SuppressShortcuts = false;
                if (_uiRoot != null)
                {
                    _uiRoot.SetEnabled(true);
                    _uiRoot.pickingMode = PickingMode.Position;
                }
            }

            var cam = Camera.main;

            // Re-enable free camera
            if (cam != null)
            {
                var freeCam = cam.GetComponent<FreeCameraController>();
                if (freeCam != null) freeCam.enabled = true;
            }

            // Destroy player camera component
            if (_playerCamera != null)
            {
                _playerCamera.SetCursorLock(false);
                Destroy(_playerCamera);
                _playerCamera = null;
            }

            // Destroy manual player
            if (_playerController != null)
            {
                Destroy(_playerController.gameObject);
                _playerController = null;
            }

            // Destroy AI player
            if (_simulatedPlayer != null)
            {
                Destroy(_simulatedPlayer.gameObject);
                _simulatedPlayer = null;
            }

            // Remove HUD
            if (_playerHudLabel != null)
            {
                _playerHudLabel.RemoveFromHierarchy();
                _playerHudLabel = null;
            }

            Debug.Log("[PatternSandbox] Player mode OFF — player destroyed, free camera restored");
        }

        private void UpdatePlayerMode()
        {
            if (!_playerModeActive) return;

            // ESC exits player mode
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ExitPlayerMode();
                return;
            }

            // Update HUD
            if (_playerHudLabel != null && _activePlayer != null)
            {
                var s = _activePlayer.State;
                var modeTag = _playerModeIsAI ? "[AI]" : "[Manual]";
                _playerHudLabel.text = $"{modeTag}  Lives: {s.Lives}  Graze: {s.GrazeTotal}" +
                    (s.IsSlow ? "  [SLOW]" : "") +
                    (s.IsInvincible ? "  [INV]" : "");
            }
        }

        private void BuildDemoSelector(VisualElement root)
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = 170; // After mode switch
            bar.style.top = 10;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;

            var label = new Label("Demo:");
            label.style.color = Color.white;
            label.style.marginRight = 6;
            bar.Add(label);

            var dropdown = new DropdownField(
                new System.Collections.Generic.List<string>(DemoPatterns),
                System.Array.IndexOf(DemoPatterns, _defaultDemoPattern));
            dropdown.style.width = 200;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                LoadDemoPattern(evt.newValue);
            });
            bar.Add(dropdown);

            root.Add(bar);
        }

        private void LoadDemoPattern()
        {
            LoadDemoPattern(_defaultDemoPattern);
        }

        /// <summary>
        /// Load a named demo pattern from Resources/DefaultPatterns.
        /// </summary>
        public void LoadDemoPattern(string patternName)
        {
            var yamlAsset = Resources.Load<TextAsset>($"DefaultPatterns/{patternName}");
            if (yamlAsset != null)
            {
                try
                {
                    var pattern = YamlSerializer.Deserialize(yamlAsset.text);
                    _editorView?.SetPattern(pattern);

                    return;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SandboxSetup] Failed to load demo YAML '{patternName}': {e.Message}");
                }
            }

            Debug.Log($"[SandboxSetup] Demo pattern '{patternName}' not found in Resources.");
        }

        /// <summary>Get available demo pattern names for UI.</summary>
        public string[] GetDemoPatternNames() => DemoPatterns;

        // ─── Asset Library Callbacks ───

        private void OnSpellCardEditingChanged(SpellCard sc)
        {
            if (_bossPlaceholder == null) return;

            if (sc != null)
            {
                _activeBossSpellCard = sc;
                _bossPlaceholder.SetPath(sc.BossPath);
                _bossPlaceholder.SetTime(0f);
                _bossPlaceholder.Show();
            }
            else
            {
                _activeBossSpellCard = null;
                _bossPlaceholder.Hide();
            }
        }

        private void OnWaveEditingChanged(List<WavePlaceholderData> waves)
        {
            ClearEnemyPlaceholders();

            if (waves == null || waves.Count == 0)
            {
                _activeWaves = null;
                return;
            }

            _activeWaves = waves;

            foreach (var wd in waves)
            {
                if (wd.Wave?.Enemies == null) continue;

                foreach (var enemy in wd.Wave.Enemies)
                {
                    var go = new GameObject($"EnemyPlaceholder_{enemy.EnemyTypeId}");
                    var ep = go.AddComponent<EnemyPlaceholder>();

                    // Try to load EnemyType from catalog for visuals
                    var meshType = MeshType.Diamond;
                    var color = new Color(0.3f, 0.8f, 1f); // Default cyan
                    float scale = 0.8f;

                    if (_catalog != null)
                    {
                        var etPath = _catalog.GetEnemyTypePath(enemy.EnemyTypeId);
                        if (System.IO.File.Exists(etPath))
                        {
                            try
                            {
                                var et = YamlSerializer.DeserializeEnemyType(
                                    System.IO.File.ReadAllText(etPath));
                                meshType = et.MeshType;
                                color = et.Color;
                                scale = et.Scale * 0.8f;
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogWarning($"[SandboxSetup] Failed to load EnemyType '{enemy.EnemyTypeId}': {e.Message}");
                            }
                        }
                    }

                    ep.Setup(meshType, color, scale);
                    // Adjust spawnDelay by wave's time offset within the segment/stage
                    ep.SetPath(enemy.Path, enemy.SpawnDelay + wd.TimeOffset, wd.SpawnOffset);
                    ep.Show();
                    ep.SetTime(0f);

                    _enemyPlaceholders.Add(ep);
                }
            }
        }

        private void ClearEnemyPlaceholders()
        {
            foreach (var ep in _enemyPlaceholders)
            {
                if (ep != null) Destroy(ep.gameObject);
            }
            _enemyPlaceholders.Clear();
        }

        private void OnAssetSelected(AssetCategory category, string id)
        {
            // Selection is tracked in the panel; no action needed here yet
        }

        private void OnAssetAddToTimeline(AssetCategory category, string id)
        {
            if (_timelineView == null) return;

            switch (category)
            {
                case AssetCategory.Patterns:
                    _timelineView.AddPatternEventFromLibrary(id);
                    break;
                case AssetCategory.Waves:
                    _timelineView.AddWaveEventFromLibrary(id);
                    break;
                case AssetCategory.SpellCards:
                    _timelineView.AddSpellCardToCurrentBossFight(id);
                    break;
                default:
                    Debug.Log($"[AssetLibrary] Cannot add {category} directly to timeline.");
                    break;
            }
        }

        private bool CanAddAssetToTimeline(AssetCategory category)
        {
            if (_timelineView == null) return false;
            return _timelineView.CanAcceptAsset(category);
        }

        private void OnCatalogChanged()
        {
            // Reload catalog and refresh library + timeline
            _catalog = STGCatalog.Load();
            _patternLibrary = new PatternLibrary();
            _assetLibrary?.Refresh(_catalog);
            _timelineView?.SetCatalog(_catalog);
        }
    }
}
