using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家摄像头：从动于玩家球体。
    /// 
    /// 球体是主体（WASD 移动球体），摄像头位置由球体反推：
    /// 摄像头位于球体沿视线反方向偏上一定角度、固定距离处，
    /// 使球体始终固定在屏幕中心偏下的位置。
    /// 
    /// 鼠标控制视角（yaw/pitch），摄像头围绕球体旋转但距离固定。
    /// </summary>
    [AddComponentMenu("STGEngine/Player Camera")]
    public class PlayerCamera : MonoBehaviour
    {
        [Header("视角")]
        [SerializeField] private float _mouseSensitivity = 2f;
        [SerializeField] private float _minPitch = -30f;
        [SerializeField] private float _maxPitch = 60f;

        [Header("摄像头与球体的位置关系")]
        [Tooltip("摄像头距离球体的固定距离")]
        [SerializeField] private float _distance = 12f;
        [Tooltip("摄像头相对球体的仰角偏移（度）。正值 = 摄像头在球体上方，球体显示在屏幕下方")]
        [SerializeField] private float _elevationOffset = 15f;

        private float _yaw;
        private float _pitch = 20f;
        private Transform _target;
        private bool _cursorLocked;

        /// <summary>视角方向的前方（水平投影，Y=0 归一化）。用于玩家相对移动。</summary>
        public Vector3 ViewForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        /// <summary>视角方向的右方。</summary>
        public Vector3 ViewRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
        /// <summary>视角方向的上方（世界 Up）。</summary>
        public Vector3 ViewUp => Vector3.up;

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
                ApplyTransform();
            }
        }

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

        /// <summary>
        /// 摄像头位置 = 球体位置 + 从球体出发、沿视线反方向偏上 _elevationOffset 度、距离 _distance 处。
        /// 摄像头朝向 = 看向球体（自然使球体固定在屏幕中心偏下）。
        /// </summary>
        private void ApplyTransform()
        {
            if (_target == null) return;

            // 摄像头在球体的"后上方"：视线方向取反，再向上偏 _elevationOffset 度
            var lookPitch = _pitch - _elevationOffset; // 摄像头实际俯仰比视角 pitch 更高
            var backDir = Quaternion.Euler(lookPitch, _yaw, 0f) * Vector3.back;

            transform.position = _target.position + backDir * _distance;
            // 直接看向球体，球体自然出现在屏幕中心偏下
            transform.LookAt(_target.position);
        }

        private void OnDisable()
        {
            if (_cursorLocked)
                SetCursorLock(false);
        }
    }
}
