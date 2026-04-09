using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家摄像头：从动于玩家球体。
    /// 
    /// 相机位置 = 玩家后方 _distance 处 + 世界 Y 轴上移 _heightOffset。
    /// 相机朝向 = LookAt 玩家位置。
    /// 
    /// 鼠标控制视角（yaw/pitch），相机围绕玩家旋转但距离固定。
    /// 
    /// Suppressed 模式：CameraScriptPlayer 普通演出期间设为 true，
    /// PlayerCamera 继续处理鼠标输入（保持 yaw/pitch 更新），
    /// 但跳过写入 transform——由 CameraScriptPlayer 覆盖。
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

        /// <summary>当前跟随目标。</summary>
        public Transform Target => _target;

        /// <summary>
        /// 为 true 时 PlayerCamera 继续处理鼠标输入，但不写入 camera transform。
        /// CameraScriptPlayer 普通演出期间设为 true，结束后设回 false。
        /// </summary>
        public bool Suppressed { get; set; }

        // ── 外部可写属性（保持型镜头用） ──

        /// <summary>摄像头距离球体的距离。</summary>
        public float Distance { get => _distance; set => _distance = value; }

        /// <summary>摄像头相对球体的垂直偏移。</summary>
        public float HeightOffset { get => _heightOffset; set => _heightOffset = value; }

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

        // ── 只读属性 ──

        /// <summary>
        /// 为 true 时，鼠标输入驱动玩家自身朝向（独立于相机），
        /// ViewForward/ViewRight 基于玩家朝向而非相机 yaw。
        /// 非 Player 参考的 persist 镜头期间启用。
        /// </summary>
        public bool DirectMouseControl { get; set; }

        // 玩家自身朝向（DirectMouseControl 模式用）
        private float _playerYaw;
        private float _playerPitch;

        /// <summary>
        /// 为 true 时，ViewForward/ViewRight 使用 effectiveYaw（含 YawOffset），
        /// 使玩家移动方向与相机画面朝向一致。非 Player 参考的 persist 镜头时启用。
        /// </summary>
        public bool UseOffsetForMovement { get; set; }

        /// <summary>视角方向的前方（水平投影，Y=0 归一化）。用于玩家相对移动。</summary>
        public Vector3 ViewForward
        {
            get
            {
                float yaw = DirectMouseControl ? _playerYaw
                          : UseOffsetForMovement ? _yaw + YawOffset
                          : _yaw;
                return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            }
        }

        /// <summary>视角方向的右方。</summary>
        public Vector3 ViewRight
        {
            get
            {
                float yaw = DirectMouseControl ? _playerYaw
                          : UseOffsetForMovement ? _yaw + YawOffset
                          : _yaw;
                return Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            }
        }
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
            UseOffsetForMovement = false;
            DirectMouseControl = false;
            _playerYaw = _yaw;
            _playerPitch = _pitch;
        }

        /// <summary>
        /// 计算当前参数下相机应该在的位置和旋转（不写入 transform）。
        /// 用于 CameraScriptPlayer 在 blend 期间获取实时的"回归目标"。
        /// </summary>
        public (Vector3 position, Quaternion rotation) ComputeGoalPose()
        {
            if (_target == null) return (transform.position, transform.rotation);

            float effectivePitch = _pitch + PitchOffset;
            float effectiveYaw = _yaw + YawOffset;

            var backDir = Quaternion.Euler(effectivePitch, effectiveYaw, 0f) * Vector3.back;
            var camPos = _target.position + backDir * _distance;
            camPos.y += _heightOffset;

            var lookDir = _target.position - camPos;
            var camRot = lookDir.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookDir)
                : transform.rotation;

            return (camPos, camRot);
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            if (_cursorLocked)
            {
                float mx = Input.GetAxis("Mouse X") * _mouseSensitivity;
                float my = Input.GetAxis("Mouse Y") * _mouseSensitivity;

                if (DirectMouseControl)
                {
                    // 鼠标直接驱动玩家自身朝向，相机 yaw/pitch 不变
                    _playerYaw += mx;
                    _playerPitch -= my;
                    _playerPitch = Mathf.Clamp(_playerPitch, _minPitch, _maxPitch);
                }
                else
                {
                    // 正常模式：鼠标驱动相机绕玩家旋转
                    _yaw += mx;
                    _pitch -= my;
                    _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
                }
            }

            // 被抑制时不写 transform——由 CameraScriptPlayer 覆盖
            if (Suppressed) return;

            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (_target == null) return;

            float effectivePitch = _pitch + PitchOffset;
            float effectiveYaw = _yaw + YawOffset;

            var backDir = Quaternion.Euler(effectivePitch, effectiveYaw, 0f) * Vector3.back;
            var camPos = _target.position + backDir * _distance;
            camPos.y += _heightOffset;

            transform.position = camPos;

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
