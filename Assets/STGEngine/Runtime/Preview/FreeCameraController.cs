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

        private void Start()
        {
            // Initialize from current transform
            _distance = Vector3.Distance(transform.position, _pivot);
            if (_distance < 0.1f) _distance = 15f;
            ApplyOrbit();

            _uiDocument = FindAnyObjectByType<UIDocument>();
        }

        private void LateUpdate()
        {
            bool overUI = IsPointerOverUI();

            // Right mouse: orbit
            if (Input.GetMouseButton(1) && !overUI)
            {
                _yaw += Input.GetAxis("Mouse X") * _orbitSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * _orbitSpeed;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            }

            // Middle mouse: pan
            if (Input.GetMouseButton(2) && !overUI)
            {
                float dx = -Input.GetAxis("Mouse X") * _panSpeed * _distance;
                float dy = -Input.GetAxis("Mouse Y") * _panSpeed * _distance;
                _pivot += transform.right * dx + transform.up * dy;
            }

            // Scroll: zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && !overUI)
            {
                _distance -= scroll * _zoomSpeed;
                _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
            }

            ApplyOrbit();
        }

        /// <summary>
        /// 检测鼠标是否悬停在 UI Toolkit 面板上。
        /// 通过 panel.Pick 判断鼠标位置下是否有 VisualElement。
        /// </summary>
        private bool IsPointerOverUI()
        {
            if (_uiDocument == null) return false;
            var root = _uiDocument.rootVisualElement;
            if (root == null || root.panel == null) return false;

            // 将屏幕坐标转换为 UI Toolkit 面板坐标（Y 轴翻转）
            Vector2 mousePos = Input.mousePosition;
            Vector2 panelPos = new Vector2(mousePos.x, Screen.height - mousePos.y);

            // 考虑 PanelSettings 的缩放
            panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, mousePos);

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
