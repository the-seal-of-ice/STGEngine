using UnityEngine;
using UnityEngine.UIElements;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Simple orbit/fly camera for sandbox preview.
    /// Right-drag to orbit, scroll to zoom, middle-drag to pan.
    /// </summary>
    [AddComponentMenu("STGEngine/Free Camera")]
    public class FreeCameraController : MonoBehaviour
    {
        [SerializeField] private float _orbitSpeed = 3f;
        [SerializeField] private float _zoomSpeed = 5f;
        [SerializeField] private float _panSpeed = 0.01f;
        [SerializeField] private float _minDistance = 2f;
        [SerializeField] private float _maxDistance = 50f;

        private float _distance = 15f;
        private float _yaw;
        private float _pitch = 30f;
        private Vector3 _pivot = Vector3.zero;

        private UIDocument _uiDocument;

        /// <summary>
        /// Tracks whether the mouse-down that started a drag was over UI.
        /// Prevents camera from responding when user drags from UI into 3D area.
        /// </summary>
        private bool _dragStartedOverUI;

        private void Start()
        {
            _distance = Vector3.Distance(transform.position, _pivot);
            if (_distance < 0.1f) _distance = 15f;
            ApplyOrbit();

            _uiDocument = FindAnyObjectByType<UIDocument>();
        }

        private void LateUpdate()
        {
            bool overUI = IsPointerOverUI();

            // Lock drag origin: if any mouse button was pressed this frame while
            // over UI, mark the entire drag as "UI drag" until all buttons release.
            bool anyDown = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            bool anyHeld = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

            if (anyDown && overUI)
                _dragStartedOverUI = true;
            if (!anyHeld)
                _dragStartedOverUI = false;

            bool blocked = overUI || _dragStartedOverUI;

            // Right mouse: orbit
            if (Input.GetMouseButton(1) && !blocked)
            {
                _yaw += Input.GetAxis("Mouse X") * _orbitSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * _orbitSpeed;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            }

            // Middle mouse: pan
            if (Input.GetMouseButton(2) && !blocked)
            {
                float dx = -Input.GetAxis("Mouse X") * _panSpeed * _distance;
                float dy = -Input.GetAxis("Mouse Y") * _panSpeed * _distance;
                _pivot += transform.right * dx + transform.up * dy;
            }

            // Scroll: zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && !blocked)
            {
                _distance -= scroll * _zoomSpeed;
                _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
            }

            ApplyOrbit();
        }

        /// <summary>
        /// Detect whether the mouse is over any UI Toolkit element.
        ///
        /// Input.mousePosition uses screen coordinates (origin bottom-left, Y up),
        /// while UI Toolkit panel coordinates have origin top-left, Y down.
        /// RuntimePanelUtils.ScreenToPanel handles scaling but NOT the Y-flip,
        /// so we flip Y manually before calling it.
        /// </summary>
        private bool IsPointerOverUI()
        {
            if (_uiDocument == null) return false;
            var root = _uiDocument.rootVisualElement;
            if (root == null || root.panel == null) return false;

            // Flip Y: Input.mousePosition is bottom-left origin,
            // ScreenToPanel expects top-left origin.
            Vector2 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, mousePos);

            var picked = root.panel.Pick(panelPos);
            return picked != null && picked != root;
        }

        private void ApplyOrbit()
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = _pivot + rotation * (Vector3.back * _distance);
            transform.LookAt(_pivot);
        }
    }
}
