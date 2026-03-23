using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Serialization;
using STGEngine.Editor.UI;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.Scene
{
    /// <summary>
    /// Bootstraps the PatternSandbox scene: creates bullet visuals,
    /// wires PatternPreviewer + UIDocument + PatternEditorView,
    /// configures camera and boundary, and loads a default pattern.
    /// Attach to an empty GameObject in PatternSandbox.unity.
    /// </summary>
    [AddComponentMenu("STGEngine/Pattern Sandbox Setup")]
    public class PatternSandboxSetup : MonoBehaviour
    {
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

        private PatternEditorView _editorView;

        private void Awake()
        {
            EnsureBulletVisuals();
            EnsurePreviewer();
            EnsureUIDocument();
            EnsureCamera();
            EnsureBoundary();
        }

        private void Start()
        {
            // Build editor view and attach to UIDocument
            _editorView = new PatternEditorView(_previewer);

            var root = _uiDocument.rootVisualElement;

            // Editor panel on the right side
            var panel = _editorView.Root;
            panel.style.position = Position.Absolute;
            panel.style.right = 0;
            panel.style.top = 0;
            panel.style.bottom = 0;
            root.Add(panel);

            // Load default pattern
            if (_loadDemoYaml)
                LoadDemoPattern();
        }

        private void OnDestroy()
        {
            _editorView?.Dispose();
        }

        // ─── Setup Helpers ───

        private void EnsureBulletVisuals()
        {
            if (_bulletMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _bulletMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }

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
                // Try the serialized reference first, then fallback to Resources
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
                        // Last resort: create at runtime (will lack theme)
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

            // Add orbit camera if not present
            if (cam.GetComponent<FreeCameraController>() == null)
                cam.gameObject.AddComponent<FreeCameraController>();

            // Position camera to see the origin
            cam.transform.position = new Vector3(0f, 8f, -15f);
            cam.transform.LookAt(Vector3.zero);
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void EnsureBoundary()
        {
            // Add boundary visualization if not present in scene
            if (FindAnyObjectByType<SandboxBoundary>() == null)
            {
                var go = new GameObject("SandboxBoundary");
                go.AddComponent<SandboxBoundary>();
            }
        }

        private void LoadDemoPattern()
        {
            var yamlAsset = Resources.Load<TextAsset>("DefaultPatterns/demo_ring_wave");
            if (yamlAsset != null)
            {
                try
                {
                    var pattern = YamlSerializer.Deserialize(yamlAsset.text);
                    _editorView.SetPattern(pattern);
                    Debug.Log("[SandboxSetup] Loaded demo pattern from Resources.");
                    return;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SandboxSetup] Failed to load demo YAML: {e.Message}");
                }
            }

            Debug.Log("[SandboxSetup] No demo YAML found, using code-generated default.");
        }
    }
}
