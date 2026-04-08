using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家摄像头：从动于玩家球体。
    /// 
    /// 相机位置 = 玩家后方 _distance 处 + 世界 Y 轴上移 _heightOffset。
    /// 相机朝向 = LookAt 玩家位置。
    /// 玩家自然出现在屏幕中心偏下（因为相机在上方看下来）。
    /// 
    /// 鼠标控制视角（yaw/pitch），相机围绕玩家旋转但距离固定。
    /// </summary>
    [AddComponentMenu("STGEngine/Player Camera")]
    public class PlayerCamera : MonoBehaviour
    {
        [Header("视角")]
        [SerializeField] private float _mouseSensitivity = 2f;
        [SerializeField] private float _minPitch = -89.9f;
        [SerializeField] private float _maxPitch = 89.9f;

        [Header("摄像头与球体的位置关系")]
        [Tooltip("摄像头距离球体的固定距离")]
        [SerializeField] private float _distance = 12f;
        [Tooltip("摄像头相对球体的垂直偏移（米）。正值 = 摄像头在球体上方")]
        [SerializeField] private float _heightOffset = 3f;

        private float _yaw;
        private float _pitch = 20f;
        private Transform _target;
        private bool _cursorLocked;

        // ── 默认属性快照（用于"回归"） ──
        private float _defaultDistance;
        private float _defaultHeightOffset;
        private float _defaultFov;
        private bool _defaultsCaptured;

        // ── 外部可写属性 ──

        /// <summary>摄像头距离球体的距离。</summary>
        public float Distance
        {
            get => _distance;
            set => _distance = value;
        }

        /// <summary>摄像头相对球体的垂直偏移。</summary>
        public float HeightOffset
        {
            get => _heightOffset;
            set => _heightOffset = value;
        }

        /// <summary>视野角度（直接读写主相机 FOV）。</summary>
        public float FOV
        {
            get => Camera.main != null ? Camera.main.fieldOfView : 60f;
            set { if (Camera.main != null) Camera.main.fieldOfView = value; }
        }

        /// <summary>Pitch 偏移（由保持型镜头叠加）。</summary>
        public float PitchOffset { get; set; }

        /// <summary>Yaw 偏移（由保持型镜头叠加）。</summary>
        public float YawOffset { get; set; }

        /// <summary>视角方向的前方（水平投影，Y=0 归一化）。用于玩家相对移动。</summary>
        public Vector3 ViewForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        /// <summary>视角方向的右方。</summary>
        public Vector3 ViewRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
        /// <summary>视角方向的上方（世界 Up）。</summary>
        public Vector3 ViewUp => Vector3.up;

        /// <summary>相机实际朝向（LookAt 玩家后的 forward）。用于射击方向。</summary>
        public Vector3 AimForward => transform.forward;
        /// <summary>相机实际右方向。用于浮游炮定位。</summary>
        public Vector3 AimRight => transform.right;
        /// <summary>相机实际上方向。用于浮游炮定位。</summary>
        public Vector3 AimUp => transform.up;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>绑定跟随目标（玩家球体）。</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null)
            {
                var euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x;
                if (_pitch > 180f) _pitch -= 360f;
                CaptureDefaults();
                ApplyTransform();
            }
        }

        public void SetCursorLock(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>捕获当前属性作为默认值（首次 SetTarget 时自动调用）。</summary>
        public void CaptureDefaults()
        {
            _defaultDistance = _distance;
            _defaultHeightOffset = _heightOffset;
            _defaultFov = Camera.main != null ? Camera.main.fieldOfView : 60f;
            _defaultsCaptured = true;
        }

        /// <summary>将 distance/heightOffset/FOV/偏移 重置回默认值。</summary>
        public void RevertToDefaults()
        {
            if (!_defaultsCaptured) return;
            _distance = _defaultDistance;
            _heightOffset = _defaultHeightOffset;
            if (Camera.main != null) Camera.main.fieldOfView = _defaultFov;
            PitchOffset = 0f;
            YawOffset = 0f;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            if (_cursorLocked)
            {
                _yaw += Input.GetAxis("Mouse X") * _mouseSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * _mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            }

            ApplyTransform();
        }

        /// <summary>
        /// 相机位置 = 玩家位置 + 沿 pitch/yaw 反方向退 _distance + 世界 Y 轴上移 _heightOffset。
        /// 相机朝向 = LookAt 玩家位置。
        /// </summary>
        private void ApplyTransform()
        {
            if (_target == null) return;

            // 叠加保持型镜头的偏移
            float effectivePitch = _pitch + PitchOffset;
            float effectiveYaw = _yaw + YawOffset;

            // 从玩家出发，沿 pitch/yaw 反方向退 _distance
            var backDir = Quaternion.Euler(effectivePitch, effectiveYaw, 0f) * Vector3.back;
            var camPos = _target.position + backDir * _distance;

            // 垂直偏移：相机在玩家上方
            camPos.y += _heightOffset;

            transform.position = camPos;

            // LookAt 玩家
            var lookDir = _target.position - camPos;
            if (lookDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(lookDir);
        }

        private void OnDisable()
        {
            if (_cursorLocked)
                SetCursorLock(false);
        }
    }
}
