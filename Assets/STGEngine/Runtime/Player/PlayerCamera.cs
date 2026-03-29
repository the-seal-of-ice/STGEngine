using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家摄像头：鼠标控制视角（俯仰+航向），刚性绑定在玩家身上。
    /// 提供 Forward/Right/Up 向量供 PlayerController 做相对移动。
    /// 
    /// 摄像头作为玩家的子物体，位置由 localPosition 固定偏移决定，
    /// 仅 yaw 旋转偏移方向，朝向由 yaw+pitch 直接控制。
    /// 后期可在此基础上添加可选的惯性/弹簧效果。
    /// </summary>
    [AddComponentMenu("STGEngine/Player Camera")]
    public class PlayerCamera : MonoBehaviour
    {
        [Header("偏移")]
        [Tooltip("摄像头相对玩家的本地偏移（仅受 yaw 旋转）")]
        [SerializeField] private Vector3 _localOffset = new(0f, 3f, -8f);

        [Header("视角")]
        [SerializeField] private float _mouseSensitivity = 2f;
        [SerializeField] private float _minPitch = -60f;
        [SerializeField] private float _maxPitch = 75f;

        private float _yaw;
        private float _pitch = 20f;
        private Transform _target;
        private bool _cursorLocked;

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
        /// 绑定到玩家。摄像头脱离原父级，改为独立跟随（不做 SetParent，
        /// 因为摄像头旋转需要独立于玩家 Transform）。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null)
            {
                var euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x;
                if (_pitch > 180f) _pitch -= 360f;
                // 立即同步位置
                ApplyTransform();
            }
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
            if (_target == null) return;

            if (_cursorLocked)
            {
                _yaw += Input.GetAxis("Mouse X") * _mouseSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * _mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            }

            ApplyTransform();
        }

        /// <summary>刚性同步：位置 = 玩家位置 + yaw 旋转后的偏移，朝向 = yaw+pitch。</summary>
        private void ApplyTransform()
        {
            if (_target == null) return;
            var yawRot = Quaternion.Euler(0f, _yaw, 0f);
            transform.position = _target.position + yawRot * _localOffset;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void OnDisable()
        {
            if (_cursorLocked)
                SetCursorLock(false);
        }
    }
}
