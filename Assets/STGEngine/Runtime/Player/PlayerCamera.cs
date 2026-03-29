using UnityEngine;

namespace STGEngine.Runtime.Player
{
    /// <summary>
    /// 玩家摄像头：鼠标控制视角（俯仰+航向），跟随玩家位置。
    /// 提供 Forward/Right/Up 向量供 PlayerController 做相对移动。
    /// 
    /// 设计参考：传统指向式（FPS 习惯，仅俯仰和航向，无横滚）。
    /// </summary>
    [AddComponentMenu("STGEngine/Player Camera")]
    public class PlayerCamera : MonoBehaviour
    {
        [Header("跟随")]
        [SerializeField] private Vector3 _followOffset = new(0f, 3f, -8f);
        [SerializeField] private float _followSmoothing = 10f;

        [Header("视角")]
        [SerializeField] private float _mouseSensitivity = 2f;
        [SerializeField] private float _minPitch = -60f;
        [SerializeField] private float _maxPitch = 75f;

        private float _yaw;
        private float _pitch = 20f;
        private Transform _target;
        private bool _cursorLocked;

        /// <summary>视角方向的前方（水平投影，Y=0 归一化）。</summary>
        public Vector3 ViewForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        /// <summary>视角方向的右方。</summary>
        public Vector3 ViewRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
        /// <summary>视角方向的上方（世界 Up）。</summary>
        public Vector3 ViewUp => Vector3.up;
        /// <summary>摄像头实际朝向（含俯仰）。</summary>
        public Vector3 LookDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>设置跟随目标。</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null)
            {
                // 从当前摄像头朝向初始化 yaw/pitch
                var euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x;
                if (_pitch > 180f) _pitch -= 360f;
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

            // 鼠标视角控制（仅在光标锁定时）
            if (_cursorLocked)
            {
                _yaw += Input.GetAxis("Mouse X") * _mouseSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * _mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            }

            // 计算目标位置：玩家位置 + 旋转后的偏移
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var desiredPos = _target.position + rotation * _followOffset;

            // 平滑跟随
            transform.position = Vector3.Lerp(transform.position, desiredPos,
                _followSmoothing * Time.deltaTime);
            transform.LookAt(_target.position + Vector3.up * 0.5f);
        }

        private void OnDisable()
        {
            // 确保禁用时释放光标
            if (_cursorLocked)
                SetCursorLock(false);
        }
    }
}
