using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家摄像头：鼠标控制视角（俯仰+航向）。
    /// 
    /// 核心设计：摄像头是主体，玩家球体的世界位置由摄像头推算。
    /// 球体始终位于屏幕上的固定位置（视角中心向下偏一定角度、固定距离处）。
    /// 类似传统 STG 的"自机固定在画面下方"，但扩展到 3D。
    /// 
    /// 移动 WASD 实际上是在移动摄像头，球体跟着摄像头走。
    /// </summary>
    [AddComponentMenu("STGEngine/Player Camera")]
    public class PlayerCamera : MonoBehaviour
    {
        [Header("视角")]
        [SerializeField] private float _mouseSensitivity = 2f;
        [SerializeField] private float _minPitch = -60f;
        [SerializeField] private float _maxPitch = 75f;

        [Header("自机屏幕位置")]
        [Tooltip("自机相对视角中心向下偏的角度（度）")]
        [SerializeField] private float _playerDownAngle = 15f;
        [Tooltip("自机距离摄像头的固定距离")]
        [SerializeField] private float _playerDistance = 12f;

        private float _yaw;
        private float _pitch = 20f;
        private bool _cursorLocked;

        // 摄像头自身的世界位置（由 PlayerController 的移动驱动）
        private Vector3 _cameraWorldPos;

        /// <summary>视角方向的前方（水平投影，Y=0 归一化）。用于玩家相对移动。</summary>
        public Vector3 ViewForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        /// <summary>视角方向的右方。用于玩家相对移动。</summary>
        public Vector3 ViewRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
        /// <summary>视角方向的上方（世界 Up）。用于玩家相对移动。</summary>
        public Vector3 ViewUp => Vector3.up;
        /// <summary>摄像头实际朝向（含俯仰）。</summary>
        public Vector3 LookDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>
        /// 计算玩家球体应该在的世界位置。
        /// = 摄像头位置 + 沿(视角中心向下偏 _playerDownAngle 度)方向 * _playerDistance。
        /// </summary>
        public Vector3 ComputePlayerWorldPos()
        {
            var camRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            // 从视角中心向下偏一定角度
            var playerDir = camRotation * Quaternion.Euler(_playerDownAngle, 0f, 0f) * Vector3.forward;
            return _cameraWorldPos + playerDir * _playerDistance;
        }

        /// <summary>初始化摄像头位置和朝向。</summary>
        public void Initialize(Vector3 startPos)
        {
            _cameraWorldPos = startPos;
            var euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
            if (_pitch > 180f) _pitch -= 360f;
            ApplyTransform();
        }

        /// <summary>移动摄像头（由 PlayerController 调用，WASD 驱动的是摄像头位移）。</summary>
        public void MoveCamera(Vector3 worldDelta)
        {
            _cameraWorldPos += worldDelta;
        }

        /// <summary>锁定/解锁鼠标光标。</summary>
        public void SetCursorLock(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void LateUpdate()
        {
            if (_cursorLocked)
            {
                _yaw += Input.GetAxis("Mouse X") * _mouseSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * _mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            }

            ApplyTransform();
        }

        private void ApplyTransform()
        {
            transform.position = _cameraWorldPos;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void OnDisable()
        {
            if (_cursorLocked)
                SetCursorLock(false);
        }
    }
}
