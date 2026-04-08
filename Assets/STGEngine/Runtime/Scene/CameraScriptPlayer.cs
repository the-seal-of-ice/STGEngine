using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Scene;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 演出镜头播放器。通过 ActionEvent 驱动，播放关键帧序列并接管相机。
    /// 支持 blendIn/blendOut 平滑过渡和独立的镜头震动。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/CameraScriptPlayer")]
    public class CameraScriptPlayer : MonoBehaviour
    {
        private enum State { Idle, BlendIn, Playing, BlendOut, PersistBlend }

        private State _state = State.Idle;
        private ICameraFrameProvider _frameProvider;
        private Camera _camera;

        // 当前演出数据
        private CameraScriptParams _params;
        private float _elapsed;
        private float _scriptDuration; // 最后一帧的 Time

        // BlendIn/Out 快照
        private Vector3 _snapshotPos;
        private Quaternion _snapshotRot;
        private float _snapshotFov;

        // 被接管的相机控制器
        private MonoBehaviour _disabledController;

        // ── 保持型镜头 blend 状态 ──
        private STGEngine.Runtime.Player.PlayerCamera _persistTarget;
        private float _persistBlendDuration;
        private float _persistBlendElapsed;
        // blend 起点
        private float _persistStartDistance;
        private float _persistStartHeight;
        private float _persistStartFov;
        private float _persistStartPitchOff;
        private float _persistStartYawOff;
        // blend 终点
        private float _persistEndDistance;
        private float _persistEndHeight;
        private float _persistEndFov;
        private float _persistEndPitchOff;
        private float _persistEndYawOff;

        // 震动状态（可独立于关键帧演出）
        private readonly List<ActiveShake> _activeShakes = new();

        /// <summary>当前是否在演出中（BlendIn/Playing/BlendOut 都算）。</summary>
        public bool IsActive => _state != State.Idle;

        /// <summary>初始化，注入坐标系提供者。</summary>
        public void Initialize(ICameraFrameProvider frameProvider)
        {
            _frameProvider = frameProvider;
            _camera = Camera.main;
        }

        /// <summary>开始播放关键帧序列。</summary>
        public void Play(CameraScriptParams scriptParams)
        {
            if (scriptParams == null || scriptParams.Keyframes.Count == 0) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            // 检查是否为纯保持型脚本（所有关键帧都是 Persist 或 Revert）
            if (IsPersistOnly(scriptParams))
            {
                ApplyPersistScript(scriptParams);
                return;
            }

            _params = scriptParams;
            _elapsed = 0f;
            _scriptDuration = scriptParams.Keyframes[scriptParams.Keyframes.Count - 1].Time;

            // 快照当前相机状态
            _snapshotPos = _camera.transform.position;
            _snapshotRot = _camera.transform.rotation;
            _snapshotFov = _camera.fieldOfView;

            // 禁用当前活跃的相机控制器
            DisableActiveController();

            _state = scriptParams.BlendIn > 0f ? State.BlendIn : State.Playing;
        }

        /// <summary>开始播放关键帧序列，使用指定的 frame provider。</summary>
        public void Play(CameraScriptParams scriptParams, ICameraFrameProvider overrideProvider)
        {
            if (overrideProvider != null)
                _frameProvider = overrideProvider;
            Play(scriptParams);
        }

        /// <summary>触发镜头震动（可与关键帧演出叠加，也可独立使用）。</summary>
        public void Shake(CameraShakePreset preset)
        {
            if (preset == null || preset.Duration <= 0f) return;
            _activeShakes.Add(new ActiveShake
            {
                Preset = preset,
                Elapsed = 0f,
                Seed = Random.value * 1000f
            });
        }

        /// <summary>立即停止演出，跳到 Idle。</summary>
        public void Stop()
        {
            if (_state == State.PersistBlend)
            {
                // 中断 persist blend 时，直接跳到终点值
                ApplyPersistValues(1f);
                _state = State.Idle;
                _params = null;
                return;
            }
            if (_state != State.Idle)
            {
                RestoreController();
                _state = State.Idle;
            }
            _params = null;
        }

        private void LateUpdate()
        {
            UpdateShakes();

            if (_state == State.Idle)
            {
                // 即使 Idle，如果有活跃震动也要叠加到相机上
                if (_activeShakes.Count > 0 && _camera != null)
                {
                    _camera.transform.position += ComputeShakeOffset();
                }
                return;
            }

            // 保持型 blend：不接管相机，只插值 PlayerCamera 属性
            if (_state == State.PersistBlend)
            {
                _persistBlendElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_persistBlendElapsed / _persistBlendDuration);
                ApplyPersistValues(t);
                if (t >= 1f)
                    _state = State.Idle;
                return;
            }

            if (_camera == null || _frameProvider == null) return;

            Vector3 targetPos;
            Quaternion targetRot;
            float targetFov;

            switch (_state)
            {
                case State.BlendIn:
                {
                    float t = Mathf.Clamp01(_elapsed / _params.BlendIn);
                    var firstFrame = EvaluateKeyframes(0f);
                    var worldTarget = LocalToWorld(firstFrame.pos, firstFrame.rot);
                    float easedT = ApplyMotionTransition(t, _params.MotionTransition);
                    targetPos = Vector3.Lerp(_snapshotPos, worldTarget.pos, easedT);
                    targetRot = Quaternion.Slerp(_snapshotRot, worldTarget.rot, easedT);
                    targetFov = Mathf.Lerp(_snapshotFov, firstFrame.fov, easedT);

                    if (_elapsed >= _params.BlendIn)
                    {
                        _elapsed -= _params.BlendIn;
                        _state = State.Playing;
                    }
                    break;
                }

                case State.Playing:
                {
                    var frame = EvaluateKeyframes(_elapsed);
                    var world = LocalToWorld(frame.pos, frame.rot);
                    targetPos = world.pos;
                    targetRot = world.rot;
                    targetFov = frame.fov;

                    if (_elapsed > _scriptDuration)
                    {
                        if (_params.BlendOut > 0f)
                        {
                            // 快照当前演出状态作为 BlendOut 起点
                            _snapshotPos = targetPos;
                            _snapshotRot = targetRot;
                            _snapshotFov = targetFov;
                            RestoreController();
                            _elapsed = 0f;
                            _state = State.BlendOut;
                        }
                        else
                        {
                            RestoreController();
                            _state = State.Idle;
                            _params = null;
                        }
                    }
                    break;
                }

                case State.BlendOut:
                {
                    float t = Mathf.Clamp01(_elapsed / _params.BlendOut);
                    // 目标：原控制器此刻应该产生的相机状态（它已被重新启用）
                    Vector3 restorePos = _camera.transform.position;
                    Quaternion restoreRot = _camera.transform.rotation;
                    float restoreFov = _camera.fieldOfView;

                    targetPos = Vector3.Lerp(_snapshotPos, restorePos, SmoothStep(t));
                    targetRot = Quaternion.Slerp(_snapshotRot, restoreRot, SmoothStep(t));
                    targetFov = Mathf.Lerp(_snapshotFov, restoreFov, SmoothStep(t));

                    if (_elapsed >= _params.BlendOut)
                    {
                        _state = State.Idle;
                        _params = null;
                        return; // 不再覆写相机
                    }
                    break;
                }

                default:
                    return;
            }

            // 叠加震动
            targetPos += ComputeShakeOffset();

            // 写入相机
            _camera.transform.position = targetPos;
            _camera.transform.rotation = targetRot;
            _camera.fieldOfView = targetFov;

            // 时钟推进放在写入之后，确保当前帧的关键帧值被应用
            _elapsed += Time.deltaTime;
        }

        // ── 关键帧插值 ──

        private (Vector3 pos, Vector3 rot, float fov) EvaluateKeyframes(float time)
        {
            var kfs = _params.Keyframes;
            if (kfs.Count == 1 || time <= kfs[0].Time)
                return (kfs[0].PositionOffset, kfs[0].Rotation, kfs[0].FOV);

            if (time >= kfs[kfs.Count - 1].Time)
            {
                var last = kfs[kfs.Count - 1];
                return (last.PositionOffset, last.Rotation, last.FOV);
            }

            // 找到当前区间
            for (int i = 0; i < kfs.Count - 1; i++)
            {
                if (time >= kfs[i].Time && time < kfs[i + 1].Time)
                {
                    float segLen = kfs[i + 1].Time - kfs[i].Time;
                    float localT = (time - kfs[i].Time) / segLen;
                    float easedT = ApplyEasing(localT, kfs[i].Easing);

                    Vector3 pos = Vector3.Lerp(kfs[i].PositionOffset, kfs[i + 1].PositionOffset, easedT);
                    Vector3 rot = LerpEuler(kfs[i].Rotation, kfs[i + 1].Rotation, easedT);
                    float fov = Mathf.Lerp(kfs[i].FOV, kfs[i + 1].FOV, easedT);
                    return (pos, rot, fov);
                }
            }

            var fallback = kfs[kfs.Count - 1];
            return (fallback.PositionOffset, fallback.Rotation, fallback.FOV);
        }

        private static Vector3 LerpEuler(Vector3 a, Vector3 b, float t)
        {
            // 通过四元数插值避免万向锁
            Quaternion qa = Quaternion.Euler(a);
            Quaternion qb = Quaternion.Euler(b);
            return Quaternion.Slerp(qa, qb, t).eulerAngles;
        }

        // ── 局部 → 世界坐标转换 ──

        private (Vector3 pos, Quaternion rot) LocalToWorld(Vector3 localOffset, Vector3 localEuler)
        {
            // 每帧实时从 ICameraFrameProvider 取玩家位置和标架
            Vector3 origin = _frameProvider.PlayerWorldPosition;
            Vector3 right = _frameProvider.FrameRight;
            Vector3 up = _frameProvider.FrameUp;
            Vector3 forward = _frameProvider.FrameForward;

            Vector3 worldPos = origin
                + right   * localOffset.x
                + up      * localOffset.y
                + forward * localOffset.z;

            Quaternion frameRot = Quaternion.LookRotation(forward, up);
            Quaternion localRot = Quaternion.Euler(localEuler);
            Quaternion worldRot = frameRot * localRot;

            return (worldPos, worldRot);
        }

        // ── 缓动函数 ──

        private static float ApplyEasing(float t, EasingType easing)
        {
            switch (easing)
            {
                case EasingType.EaseIn:    return t * t;
                case EasingType.EaseOut:   return 1f - (1f - t) * (1f - t);
                case EasingType.EaseInOut: return SmoothStep(t);
                case EasingType.Linear:
                default:                   return t;
            }
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float ApplyMotionTransition(float t, MotionTransitionType motionType)
        {
            switch (motionType)
            {
                case MotionTransitionType.Cut:
                    return 1f;
                case MotionTransitionType.SpeedRamp:
                    float t3 = t * t * t;
                    float t4 = t3 * t;
                    float t5 = t4 * t;
                    return 6f * t5 - 15f * t4 + 10f * t3;
                case MotionTransitionType.SmoothBlend:
                default:
                    return SmoothStep(t);
            }
        }

        // ── 震动 ──

        private void UpdateShakes()
        {
            for (int i = _activeShakes.Count - 1; i >= 0; i--)
            {
                _activeShakes[i] = new ActiveShake
                {
                    Preset = _activeShakes[i].Preset,
                    Elapsed = _activeShakes[i].Elapsed + Time.deltaTime,
                    Seed = _activeShakes[i].Seed
                };
                if (_activeShakes[i].Elapsed >= _activeShakes[i].Preset.Duration)
                    _activeShakes.RemoveAt(i);
            }
        }

        private Vector3 ComputeShakeOffset()
        {
            Vector3 offset = Vector3.zero;
            foreach (var shake in _activeShakes)
            {
                float progress = shake.Elapsed / shake.Preset.Duration;
                float decay = Mathf.Pow(1f - progress, shake.Preset.DecayRate);
                float amp = shake.Preset.Amplitude * decay;
                float freq = shake.Preset.Frequency;
                float t = shake.Elapsed;

                offset += new Vector3(
                    (Mathf.PerlinNoise(t * freq, shake.Seed) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(shake.Seed, t * freq) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(t * freq + shake.Seed, t * freq) - 0.5f) * 2f
                ) * amp;
            }
            return offset;
        }

        // ── 相机控制器管理 ──

        private void DisableActiveController()
        {
            if (_camera == null) return;

            // 按优先级检查：FreeCameraController > PlayerCamera > ChunkGenerator
            var freeCam = _camera.GetComponent<STGEngine.Runtime.Preview.FreeCameraController>();
            if (freeCam != null && freeCam.enabled)
            {
                freeCam.enabled = false;
                _disabledController = freeCam;
                return;
            }

            var playerCam = _camera.GetComponent<STGEngine.Runtime.Player.PlayerCamera>();
            if (playerCam != null && playerCam.enabled)
            {
                // PlayerCamera.OnDisable 会解锁鼠标，禁用后立即重新锁定
                playerCam.enabled = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _disabledController = playerCam;
                return;
            }

            var chunkGen = FindAnyObjectByType<ChunkGenerator>();
            if (chunkGen != null && chunkGen.enabled)
            {
                chunkGen.enabled = false;
                _disabledController = chunkGen;
            }
        }

        private void RestoreController()
        {
            if (_disabledController != null)
            {
                _disabledController.enabled = true;

                // PlayerCamera 被禁用时 OnDisable 会解锁鼠标，恢复后需要重新锁定
                if (_disabledController is STGEngine.Runtime.Player.PlayerCamera playerCam)
                {
                    playerCam.SetCursorLock(true);
                }

                _disabledController = null;
            }
        }

        // ── 保持型镜头 ──

        /// <summary>检查脚本是否为纯保持型（所有关键帧都是 Persist 或 Revert）。</summary>
        private static bool IsPersistOnly(CameraScriptParams scriptParams)
        {
            foreach (var kf in scriptParams.Keyframes)
            {
                if (kf.PersistMode == KeyframePersistMode.Normal)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 启动保持型脚本的渐变过渡：不接管相机，在 BlendIn 时间内
        /// 平滑插值 PlayerCamera 属性到目标值。PlayerCamera 继续运行。
        /// </summary>
        private void ApplyPersistScript(CameraScriptParams scriptParams)
        {
            if (_camera == null) _camera = Camera.main;
            var playerCam = _camera != null
                ? _camera.GetComponent<STGEngine.Runtime.Player.PlayerCamera>()
                : null;
            if (playerCam == null) return;

            var lastKf = scriptParams.Keyframes[scriptParams.Keyframes.Count - 1];

            // 记录起点（当前 PlayerCamera 状态）
            _persistTarget = playerCam;
            _persistStartDistance = playerCam.Distance;
            _persistStartHeight = playerCam.HeightOffset;
            _persistStartFov = playerCam.FOV;
            _persistStartPitchOff = playerCam.PitchOffset;
            _persistStartYawOff = playerCam.YawOffset;

            if (lastKf.PersistMode == KeyframePersistMode.Revert)
            {
                // 回归：终点 = 默认值
                // RevertToDefaults 会重置，先偷看默认值再恢复
                // 用一个临时 revert 来获取目标值
                playerCam.RevertToDefaults();
                _persistEndDistance = playerCam.Distance;
                _persistEndHeight = playerCam.HeightOffset;
                _persistEndFov = playerCam.FOV;
                _persistEndPitchOff = 0f;
                _persistEndYawOff = 0f;
                // 恢复回起点，让 blend 从当前状态开始
                playerCam.Distance = _persistStartDistance;
                playerCam.HeightOffset = _persistStartHeight;
                playerCam.FOV = _persistStartFov;
                playerCam.PitchOffset = _persistStartPitchOff;
                playerCam.YawOffset = _persistStartYawOff;
            }
            else
            {
                // Persist：终点 = 当前值 + 关键帧偏移
                _persistEndDistance = _persistStartDistance + lastKf.PositionOffset.z;
                _persistEndHeight = _persistStartHeight + lastKf.PositionOffset.y;
                _persistEndFov = lastKf.FOV > 0f ? lastKf.FOV : _persistStartFov;
                _persistEndPitchOff = _persistStartPitchOff + lastKf.Rotation.x;
                _persistEndYawOff = _persistStartYawOff + lastKf.Rotation.y;
            }

            // blend 时长 = BlendIn + BlendOut（两段合并为一次平滑过渡）
            _persistBlendDuration = scriptParams.BlendIn + scriptParams.BlendOut;
            _persistBlendElapsed = 0f;

            if (_persistBlendDuration <= 0f)
            {
                // 无 blend，瞬间应用
                ApplyPersistValues(1f);
                return;
            }

            _state = State.PersistBlend;
        }

        /// <summary>按 t (0~1) 插值 PlayerCamera 属性。</summary>
        private void ApplyPersistValues(float t)
        {
            if (_persistTarget == null) return;
            float s = SmoothStep(t);
            _persistTarget.Distance = Mathf.Lerp(_persistStartDistance, _persistEndDistance, s);
            _persistTarget.HeightOffset = Mathf.Lerp(_persistStartHeight, _persistEndHeight, s);
            _persistTarget.FOV = Mathf.Lerp(_persistStartFov, _persistEndFov, s);
            _persistTarget.PitchOffset = Mathf.Lerp(_persistStartPitchOff, _persistEndPitchOff, s);
            _persistTarget.YawOffset = Mathf.Lerp(_persistStartYawOff, _persistEndYawOff, s);
        }

        // ── 内部数据 ──

        private struct ActiveShake
        {
            public CameraShakePreset Preset;
            public float Elapsed;
            public float Seed;
        }
    }
}
