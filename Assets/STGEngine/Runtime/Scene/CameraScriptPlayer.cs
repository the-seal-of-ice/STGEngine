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
    [DefaultExecutionOrder(100)] // 在 PlayerCamera (默认 0) 之后执行
    [AddComponentMenu("STGEngine/Scene/CameraScriptPlayer")]
    public class CameraScriptPlayer : MonoBehaviour
    {
        private enum State { Idle, BlendIn, Playing, BlendOut, PersistBlend }

        private State _state = State.Idle;
        private ICameraFrameProvider _frameProvider;
        private Camera _camera;

        // 可选：per-keyframe frame provider 解析回调
        private System.Func<CameraKeyframe, ICameraFrameProvider> _perKeyframeResolver;

        // 可选：通过 ID 查找目标 Transform（用于 AimMode 的 LockBoss/LockEnemy）
        private System.Func<string, Transform> _aimTargetLookup;

        // 可选：查找最近的有效目标（ID 为空时使用）
        private System.Func<PlayerAimMode, Vector3, Transform> _aimNearestLookup;

        // 当前 persist 的 AimMode（用于目标丢失时重新查找）
        private PlayerAimMode _currentAimMode = PlayerAimMode.Default;

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

        /// <summary>
        /// 设置 per-keyframe frame provider 解析器。
        /// 当关键帧有 ReferenceOverride 时，通过此回调获取对应的 provider。
        /// </summary>
        public void SetPerKeyframeResolver(System.Func<CameraKeyframe, ICameraFrameProvider> resolver)
        {
            _perKeyframeResolver = resolver;
        }

        /// <summary>
        /// 设置目标查找回调（通过 ID 查找 Transform）。
        /// 用于 AimMode 的 LockBoss/LockEnemy。
        /// </summary>
        public void SetAimTargetLookup(System.Func<string, Transform> lookup)
        {
            _aimTargetLookup = lookup;
        }

        /// <summary>
        /// 设置最近目标查找回调。参数：(AimMode, 玩家世界位置) → 最近的有效 Transform。
        /// ID 为空时或目标丢失时自动调用。
        /// </summary>
        public void SetAimNearestLookup(System.Func<PlayerAimMode, Vector3, Transform> lookup)
        {
            _aimNearestLookup = lookup;
        }

        /// <summary>开始播放关键帧序列。</summary>
        public void Play(CameraScriptParams scriptParams)
        {
            if (scriptParams == null || scriptParams.Keyframes.Count == 0) return;

            // 检查是否为纯保持型脚本（所有关键帧都是 Persist 或 Revert）
            if (IsPersistOnly(scriptParams))
            {
                ApplyPersistScript(scriptParams);
                return;
            }

            PlayInternal(scriptParams);
        }

        /// <summary>开始播放关键帧序列，使用指定的 frame provider。</summary>
        public void Play(CameraScriptParams scriptParams, ICameraFrameProvider overrideProvider)
        {
            if (overrideProvider != null)
                _frameProvider = overrideProvider;
            Play(scriptParams);
        }

        /// <summary>内部播放逻辑（普通 Normal 关键帧演出）。</summary>
        private void PlayInternal(CameraScriptParams scriptParams)
        {
            if (scriptParams == null || scriptParams.Keyframes.Count == 0) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

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

                // Idle 状态下也检查锁定目标丢失（persist 结束后仍在锁定）
                if (_persistTarget != null && _persistTarget.AimLockTargetLost)
                {
                    _persistTarget.AimLockTargetLost = false;
                    if (_currentAimMode == PlayerAimMode.LockBoss || _currentAimMode == PlayerAimMode.LockEnemy)
                    {
                        _persistTarget.AimLockTarget = FindAimTarget(_currentAimMode, "", _persistTarget);
                    }
                }

                return;
            }

            // 保持型 blend：只改 PlayerCamera 属性，PlayerCamera 自己驱动相机
            if (_state == State.PersistBlend)
            {
                _persistBlendElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_persistBlendElapsed / _persistBlendDuration);
                ApplyPersistValues(t);

                // 检查锁定目标是否丢失，自动重新查找
                if (_persistTarget != null && _persistTarget.AimLockTargetLost)
                {
                    _persistTarget.AimLockTargetLost = false;
                    if (_currentAimMode == PlayerAimMode.LockBoss || _currentAimMode == PlayerAimMode.LockEnemy)
                    {
                        _persistTarget.AimLockTarget = FindAimTarget(_currentAimMode, "", _persistTarget);
                    }
                }

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
                    var provider0 = GetProviderForKeyframe(firstFrame.segIndex);
                    var worldTarget = LocalToWorld(firstFrame.pos, firstFrame.rot, provider0);
                    float easedT = ApplyMotionTransition(t, _params.MotionTransition);

                    // 用 PlayerCamera 的实时目标位置作为 blend 起点（跟随玩家移动）
                    var blendFrom = GetLivePlayerCameraPose();
                    targetPos = Vector3.Lerp(blendFrom.pos, worldTarget.pos, easedT);
                    targetRot = Quaternion.Slerp(blendFrom.rot, worldTarget.rot, easedT);
                    targetFov = Mathf.Lerp(blendFrom.fov, firstFrame.fov, easedT);

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
                    var provider = GetProviderForKeyframe(frame.segIndex);
                    var world = LocalToWorld(frame.pos, frame.rot, provider);
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
                            // 不在这里 RestoreController——BlendOut 期间仍需 suppress
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

                    // 用 PlayerCamera 的实时目标位置作为 blend 终点（跟随玩家移动）
                    var blendTo = GetLivePlayerCameraPose();
                    targetPos = Vector3.Lerp(_snapshotPos, blendTo.pos, SmoothStep(t));
                    targetRot = Quaternion.Slerp(_snapshotRot, blendTo.rot, SmoothStep(t));
                    targetFov = Mathf.Lerp(_snapshotFov, blendTo.fov, SmoothStep(t));

                    if (_elapsed >= _params.BlendOut)
                    {
                        RestoreController();
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

        /// <summary>
        /// 获取 PlayerCamera 当前帧应该产生的相机位姿（实时跟随玩家）。
        /// 用于 BlendIn/BlendOut 期间作为动态起点/终点，避免冻结快照导致跳变。
        /// </summary>
        private (Vector3 pos, Quaternion rot, float fov) GetLivePlayerCameraPose()
        {
            var playerCam = _camera != null
                ? _camera.GetComponent<STGEngine.Runtime.Player.PlayerCamera>()
                : null;
            if (playerCam != null)
            {
                var (pos, rot) = playerCam.ComputeGoalPose();
                return (pos, rot, playerCam.FOV);
            }
            // fallback：用当前相机状态
            return (_camera.transform.position, _camera.transform.rotation, _camera.fieldOfView);
        }

        // ── 关键帧插值 ──

        private (Vector3 pos, Quaternion rot, float fov, int segIndex) EvaluateKeyframes(float time)
        {
            var kfs = _params.Keyframes;
            if (kfs.Count == 1 || time <= kfs[0].Time)
                return (kfs[0].PositionOffset, kfs[0].Rotation, kfs[0].FOV, 0);

            if (time >= kfs[kfs.Count - 1].Time)
            {
                var last = kfs[kfs.Count - 1];
                return (last.PositionOffset, last.Rotation, last.FOV, kfs.Count - 1);
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
                    Quaternion rot = Quaternion.Slerp(kfs[i].Rotation, kfs[i + 1].Rotation, easedT);
                    float fov = Mathf.Lerp(kfs[i].FOV, kfs[i + 1].FOV, easedT);
                    return (pos, rot, fov, i);
                }
            }

            var fallback = kfs[kfs.Count - 1];
            return (fallback.PositionOffset, fallback.Rotation, fallback.FOV, kfs.Count - 1);
        }

        /// <summary>获取指定关键帧索引对应的 frame provider（支持 per-keyframe 覆盖）。</summary>
        private ICameraFrameProvider GetProviderForKeyframe(int keyframeIndex)
        {
            if (_params == null || _perKeyframeResolver == null) return _frameProvider;
            var kfs = _params.Keyframes;
            if (keyframeIndex < 0 || keyframeIndex >= kfs.Count) return _frameProvider;
            var kf = kfs[keyframeIndex];
            if (!kf.ReferenceOverride.HasValue) return _frameProvider;
            var resolved = _perKeyframeResolver(kf);
            return resolved ?? _frameProvider;
        }

        // ── 局部 → 世界坐标转换 ──

        private (Vector3 pos, Quaternion rot) LocalToWorld(Vector3 localOffset, Quaternion localRot, ICameraFrameProvider provider = null)
        {
            var fp = provider ?? _frameProvider;
            Vector3 origin = fp.PlayerWorldPosition;
            Vector3 right = fp.FrameRight;
            Vector3 up = fp.FrameUp;
            Vector3 forward = fp.FrameForward;

            Vector3 worldPos = origin
                + right   * localOffset.x
                + up      * localOffset.y
                + forward * localOffset.z;

            Quaternion frameRot = Quaternion.LookRotation(forward, up);
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

            // PlayerCamera：不禁用，改为抑制（保持鼠标输入更新）
            var playerCam = _camera.GetComponent<STGEngine.Runtime.Player.PlayerCamera>();
            if (playerCam != null && playerCam.enabled)
            {
                playerCam.Suppressed = true;
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
                if (_disabledController is STGEngine.Runtime.Player.PlayerCamera playerCam)
                {
                    // 取消抑制，PlayerCamera 下一帧自然接管
                    playerCam.Suppressed = false;
                }
                else
                {
                    _disabledController.enabled = true;
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
        /// 判断 persist 脚本的有效参考对象是否为 Player。
        /// 只有 Player 参考时才走 PlayerCamera 属性修改路径。
        /// </summary>
        private static CameraReferenceTarget GetEffectiveTarget(CameraScriptParams scriptParams)
        {
            var lastKf = scriptParams.Keyframes[scriptParams.Keyframes.Count - 1];
            return lastKf.ReferenceOverride ?? scriptParams.ReferenceTarget;
        }

        /// <summary>
        /// 启动保持型脚本。所有 persist/revert 都通过 PersistBlend 修改 PlayerCamera 参数，
        /// PlayerCamera 始终驱动相机，不接管 transform，零跳变。
        /// </summary>
        private void ApplyPersistScript(CameraScriptParams scriptParams)
        {
            if (_camera == null) _camera = Camera.main;
            var playerCam = _camera != null
                ? _camera.GetComponent<STGEngine.Runtime.Player.PlayerCamera>()
                : null;
            if (playerCam == null) return;

            var lastKf = scriptParams.Keyframes[scriptParams.Keyframes.Count - 1];
            var effectiveTarget = GetEffectiveTarget(scriptParams);

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
                // 回归时关闭偏移移动模式和直接鼠标控制
                playerCam.UseOffsetForMovement = false;
                playerCam.DirectMouseControl = false;
                playerCam.AimLockTarget = null;
                playerCam.AimLockPoint = null;
                playerCam.AimScreenCenter = false;
            }
            else if (effectiveTarget == CameraReferenceTarget.Player)
            {
                // Player 参考 Persist：终点 = 当前值 + 关键帧偏移
                _persistEndDistance = _persistStartDistance + lastKf.PositionOffset.z;
                _persistEndHeight = _persistStartHeight + lastKf.PositionOffset.y;
                _persistEndFov = lastKf.FOV > 0f ? lastKf.FOV : _persistStartFov;
                _persistEndPitchOff = _persistStartPitchOff + lastKf.RotationEuler.x;
                _persistEndYawOff = _persistStartYawOff + lastKf.RotationEuler.y;
            }
            else
            {
                // 非 Player 参考 Persist：计算目标世界位置，反推为 PlayerCamera 参数变化量
                var targetWorld = ComputePersistWorldTarget(scriptParams, lastKf);
                ComputePlayerCameraParamsForTarget(playerCam, targetWorld.pos, targetWorld.rot, lastKf.FOV);
                // 让玩家移动方向跟随相机朝向（含 YawOffset）
                playerCam.UseOffsetForMovement = true;
                // 启用鼠标直接控制玩家朝向（与相机解耦）
                playerCam.DirectMouseControl = true;
            }

            // 应用玩家朝向模式
            ApplyAimMode(playerCam, lastKf);

            // blend 时长 = BlendIn + BlendOut
            _persistBlendDuration = scriptParams.BlendIn + scriptParams.BlendOut;
            _persistBlendElapsed = 0f;

            if (_persistBlendDuration <= 0f)
            {
                ApplyPersistValues(1f);
                return;
            }

            _state = State.PersistBlend;
        }

        /// <summary>用 frame provider 计算 persist 关键帧的世界位置。</summary>
        private (Vector3 pos, Quaternion rot) ComputePersistWorldTarget(CameraScriptParams scriptParams, CameraKeyframe kf)
        {
            // 尝试用 per-keyframe resolver
            ICameraFrameProvider provider = _frameProvider;
            if (kf.ReferenceOverride.HasValue && _perKeyframeResolver != null)
            {
                var resolved = _perKeyframeResolver(kf);
                if (resolved != null) provider = resolved;
            }
            if (provider == null) provider = _frameProvider;
            if (provider == null) return (Vector3.zero, Quaternion.identity);

            return LocalToWorld(kf.PositionOffset, kf.Rotation, provider);
        }

        /// <summary>
        /// 从目标世界位姿反推 PlayerCamera 参数终点值。
        /// 不修改 _yaw/_pitch（影响玩家移动方向），用 Offset 补偿。
        /// </summary>
        private void ComputePlayerCameraParamsForTarget(
            STGEngine.Runtime.Player.PlayerCamera playerCam,
            Vector3 worldPos, Quaternion worldRot, float fov)
        {
            if (playerCam.Target == null) return;

            Vector3 playerPos = playerCam.Target.position;
            Vector3 delta = worldPos - playerPos;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            float dist = Mathf.Max(0.1f, horizontal.magnitude);

            // 需要的 yaw
            float neededYaw = horizontal.magnitude > 0.01f
                ? Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg + 180f
                : playerCam.Yaw;

            // 需要的 pitch
            float neededPitch = worldRot.eulerAngles.x;
            if (neededPitch > 180f) neededPitch -= 360f;

            // 用 offset 补偿，不改 _yaw/_pitch
            float yawOff = Mathf.DeltaAngle(playerCam.Yaw, neededYaw);
            float pitchOff = neededPitch - playerCam.Pitch;

            // heightOffset 扣除 backDir 垂直分量
            float effPitch = playerCam.Pitch + pitchOff;
            float effYaw = playerCam.Yaw + yawOff;
            var backDir = Quaternion.Euler(effPitch, effYaw, 0f) * Vector3.back;
            float heightOff = delta.y - backDir.y * dist;

            _persistEndDistance = dist;
            _persistEndHeight = heightOff;
            _persistEndFov = fov > 0f ? fov : _persistStartFov;
            _persistEndPitchOff = pitchOff;
            _persistEndYawOff = yawOff;
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

        /// <summary>根据关键帧的 AimMode 设置 PlayerCamera 的朝向锁定。</summary>
        private void ApplyAimMode(STGEngine.Runtime.Player.PlayerCamera playerCam, CameraKeyframe kf)
        {
            // 先清除
            playerCam.AimLockTarget = null;
            playerCam.AimLockPoint = null;
            playerCam.AimScreenCenter = false;
            playerCam.AimLockTargetLost = false;
            _currentAimMode = kf.AimMode;

            switch (kf.AimMode)
            {
                case PlayerAimMode.Default:
                case PlayerAimMode.FreeMouse:
                    break;

                case PlayerAimMode.ScreenCenter:
                    playerCam.AimScreenCenter = true;
                    break;

                case PlayerAimMode.LockPoint:
                    playerCam.AimLockPoint = kf.AimTargetPosition;
                    break;

                case PlayerAimMode.LockBoss:
                case PlayerAimMode.LockEnemy:
                    playerCam.AimLockTarget = FindAimTarget(kf.AimMode, kf.AimTargetId, playerCam);
                    break;
            }
        }

        /// <summary>
        /// 查找 aim 锁定目标。有 ID 时按 ID 查找，无 ID 时查找最近的有效目标。
        /// </summary>
        private Transform FindAimTarget(PlayerAimMode mode, string targetId,
            STGEngine.Runtime.Player.PlayerCamera playerCam)
        {
            // 有 ID 时按 ID 查找
            if (!string.IsNullOrEmpty(targetId) && _aimTargetLookup != null)
            {
                var t = _aimTargetLookup(targetId);
                if (t != null) return t;
            }

            // 无 ID 或 ID 查找失败：查找最近的有效目标
            if (_aimNearestLookup != null)
            {
                Vector3 playerPos = playerCam.Target != null
                    ? playerCam.Target.position : playerCam.transform.position;
                return _aimNearestLookup(mode, playerPos);
            }

            return null;
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
