using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;
using STGEngine.Editor.UI;
using STGEngine.Editor.UI.Timeline;
using STGEngine.Runtime;
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
            }
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
        }

        private void OnDestroy()
        {
            _editorView?.Dispose();
            _timelineView?.Dispose();
            _previewerPool?.Dispose();
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
            _timelineView = new TimelineEditorView(_timelinePlayback, _patternLibrary, _previewer);
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

            // Timeline editor as bottom panel (top half remains 3D viewport).
            // Use top=50% + bottom=0 instead of height=50%, because percentage
            // heights don't resolve correctly when the parent (TemplateContainer)
            // has auto height.
            var panel = _timelineView.Root;
            panel.style.position = Position.Absolute;
            panel.style.left = 0;
            panel.style.right = 0;
            panel.style.top = Length.Percent(50);
            panel.style.bottom = 0;
            root.Add(panel);

            // Force theme override after Unity Runtime Theme has been applied
            StartCoroutine(ForceTimelineTheme());
        }

        private IEnumerator ForceTimelineTheme()
        {
            // Unity Runtime Theme applies over multiple frames; keep overriding
            yield return null;                     // frame 1
            _timelineView.ForceApplyTheme();
            yield return null;                     // frame 2
            _timelineView.ForceApplyTheme();
            yield return new WaitForSeconds(0.1f); // 100ms
            _timelineView.ForceApplyTheme();
            yield return new WaitForSeconds(0.3f); // 300ms
            _timelineView.ForceApplyTheme();
            yield return new WaitForSeconds(0.5f); // 500ms — final pass
            _timelineView.ForceApplyTheme();
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
                    Debug.Log($"[SandboxSetup] Loaded demo pattern: {patternName}");
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
    }
}
