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
        /// 朝向锁定目标（世界坐标）。设为非 null 时，ViewForward 指向该点。
        /// 优先级最高，覆盖 DirectMouseControl 和 UseOffsetForMovement。
        /// </summary>
        public Transform AimLockTarget { get; set; }

        /// <summary>
        /// 朝向锁定固定点（世界坐标）。AimLockTarget 为 null 时使用。
        /// 设为非 null 时，ViewForward 指向该点。
        /// </summary>
        public Vector3? AimLockPoint { get; set; }

        /// <summary>
        /// 为 true 时，ViewForward/ViewRight 使用相机的实际朝向（屏幕中心射击）。
        /// </summary>
        public bool AimScreenCenter { get; set; }

        /// <summary>
        /// 为 true 时，ViewForward/ViewRight 使用 effectiveYaw（含 YawOffset），
        /// 使玩家移动方向与相机画面朝向一致。非 Player 参考的 persist 镜头时启用。
        /// </summary>
        public bool UseOffsetForMovement { get; set; }

        /// <summary>视角方向的前方（水平投影，Y=0 归一化）。用于玩家相对移动和射击方向。</summary>
        public Vector3 ViewForward
        {
            get
            {
                // 优先级：锁定目标 > 锁定固定点 > 屏幕中心 > 直接鼠标 > 偏移模式 > 默认
                Vector3? lockDir = GetAimLockDirection();
                if (lockDir.HasValue)
                {
                    var flat = new Vector3(lockDir.Value.x, 0f, lockDir.Value.z);
                    return flat.sqrMagnitude > 0.001f ? flat.normalized : Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                }
                if (AimScreenCenter) return transform.forward;
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
                Vector3? lockDir = GetAimLockDirection();
                if (lockDir.HasValue)
                {
                    var flat = new Vector3(lockDir.Value.x, 0f, lockDir.Value.z);
                    if (flat.sqrMagnitude > 0.001f)
                        return Vector3.Cross(Vector3.up, flat.normalized).normalized;
                }
                if (AimScreenCenter) return transform.right;
                float yaw = DirectMouseControl ? _playerYaw
                          : UseOffsetForMovement ? _yaw + YawOffset
                          : _yaw;
                return Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            }
        }

        /// <summary>获取朝向锁定方向（从玩家到目标），无锁定或目标失效时返回 null。</summary>
        private Vector3? GetAimLockDirection()
        {
            Vector3 playerPos = _target != null ? _target.position : transform.position;

            // 检查动态目标是否仍然有效
            if (AimLockTarget != null)
            {
                // Unity 的 == null 检查包含 destroyed 对象
                if (AimLockTarget == null)
                {
                    AimLockTarget = null; // 清除已销毁的引用
                }
                else
                {
                    // 检查是否是 BossPlaceholder 或 EnemyPlaceholder，验证可见性
                    bool valid = true;
                    var boss = AimLockTarget.GetComponent<STGEngine.Runtime.Preview.BossPlaceholder>();
                    if (boss != null) valid = boss.IsVisible;
                    else
                    {
                        var enemy = AimLockTarget.GetComponent<STGEngine.Runtime.Preview.EnemyPlaceholder>();
                        if (enemy != null) valid = enemy.IsVisible;
                    }

                    if (valid)
                    {
                        var dir = AimLockTarget.position - playerPos;
                        return dir.sqrMagnitude > 0.001f ? dir.normalized : (Vector3?)null;
                    }
                    else
                    {
                        // 目标不再有效，清除并触发重新查找
                        AimLockTarget = null;
                        AimLockTargetLost = true;
                    }
                }
            }

            if (AimLockPoint.HasValue)
            {
                var dir = AimLockPoint.Value - playerPos;
                return dir.sqrMagnitude > 0.001f ? dir.normalized : (Vector3?)null;
            }
            return null;
        }

        /// <summary>当锁定目标丢失时设为 true，由 CameraScriptPlayer 检测并重新查找。</summary>
        public bool AimLockTargetLost { get; set; }
        /// <summary>视角方向的上方（世界 Up）。</summary>
        public Vector3 ViewUp => Vector3.up;

        /// <summary>
        /// 射击/瞄准方向。有锁定目标时指向目标，否则使用相机朝向。
        /// </summary>
        public Vector3 AimForward
        {
            get
            {
                Vector3? lockDir = GetAimLockDirection();
                return lockDir ?? transform.forward;
            }
        }

        /// <summary>射击右方向（基于 AimForward）。</summary>
        public Vector3 AimRight
        {
            get
            {
                Vector3? lockDir = GetAimLockDirection();
                if (lockDir.HasValue)
                    return Vector3.Cross(Vector3.up, lockDir.Value).normalized;
                return transform.right;
            }
        }

        /// <summary>射击上方向（基于 AimForward）。</summary>
        public Vector3 AimUp
        {
            get
            {
                Vector3? lockDir = GetAimLockDirection();
                if (lockDir.HasValue)
                    return Vector3.Cross(lockDir.Value, AimRight).normalized;
                return transform.up;
            }
        }

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
            AimLockTarget = null;
            AimLockPoint = null;
            AimScreenCenter = false;
            AimLockTargetLost = false;
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
