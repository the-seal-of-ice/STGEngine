using UnityEngine;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 普通障碍物交互。玩家擦过非危险障碍物时触发视觉反馈：
    /// Sway = 以地面接触点为轴心的摇晃动画，Nudge = 轻推玩家。
    /// 碰撞检测考虑玩家球体半径（直径 1.6m，半径 0.8m）。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/ObstacleInteraction")]
    public class ObstacleInteraction : MonoBehaviour
    {
        [Header("Interaction Settings")]
        [SerializeField, Tooltip("玩家球体半径（米），直径 1.6m")]
        private float _playerRadius = 0.8f;

        [SerializeField, Tooltip("Sway 触发距离（从障碍物表面算起）")]
        private float _swayRange = 1.5f;

        [SerializeField, Tooltip("Nudge 推力触发距离（从障碍物表面算起，比 sway 更近）")]
        private float _nudgeRange = 0.5f;

        [SerializeField, Tooltip("Nudge 推力强度")]
        private float _nudgeForce = 25f;

        [SerializeField, Tooltip("Sway 最大摇晃角度（度）")]
        private float _swayAngle = 12f;

        private PlayerAnchorController _player;
        private ChunkGenerator _generator;
        private bool _initialized;

        private readonly Dictionary<GameObject, SwayState> _swaying = new();

        public void Initialize(PlayerAnchorController player, ChunkGenerator generator)
        {
            _player = player;
            _generator = generator;
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            CheckProximity();
            UpdateSway();
        }

        private void CheckProximity()
        {
            Vector3 playerPos = _player.WorldPosition;

            foreach (var chunk in _generator.ActiveChunks)
            {
                if (!chunk.IsActive) continue;

                foreach (var obs in chunk.Obstacles)
                {
                    if (obs.Config == null || obs.Config.IsHazard) continue;
                    if (obs.Config.ContactResponse == Core.Scene.ContactResponse.None) continue;
                    if (obs.GameObject == null || !obs.GameObject.activeSelf) continue;

                    float obsRadius = GetObstacleRadius(obs.GameObject);
                    float horizDist = HorizontalDistance(playerPos, obs.GameObject.transform.position);
                    float surfaceDist = horizDist - obsRadius - _playerRadius;

                    // Sway：较远处触发，幅度随接近程度增大
                    if (surfaceDist < _swayRange && surfaceDist > -obsRadius)
                    {
                        float proximity = 1f - Mathf.Clamp01(surfaceDist / _swayRange);
                        TriggerSway(obs.GameObject, playerPos, proximity);
                    }

                    // Nudge 推力：更近处才触发，独立于 sway
                    if (surfaceDist < _nudgeRange && surfaceDist > -obsRadius)
                    {
                        float pushStrength = obs.Config.ContactResponse == Core.Scene.ContactResponse.Nudge ? 1f : 0.3f;
                        TriggerNudge(obs.GameObject, playerPos, pushStrength);
                    }
                }
            }
        }

        private float GetObstacleRadius(GameObject obj)
        {
            var renderer = obj.GetComponent<Renderer>();
            if (renderer == null) return 1f;
            // XZ 平面上的半径（取 X 和 Z extent 的最小值）
            var ext = renderer.bounds.extents;
            return Mathf.Min(ext.x, ext.z);
        }

        /// <summary>XZ 平面上两点的水平距离。</summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void TriggerSway(GameObject obj, Vector3 playerPos, float proximity)
        {
            if (_swaying.ContainsKey(obj))
            {
                // 已在摇晃中：更新幅度（如果更近了则加大）
                var existing = _swaying[obj];
                if (proximity > existing.Intensity)
                    existing.Intensity = proximity;
                return;
            }

            Vector3 pushDir = (obj.transform.position - playerPos).normalized;

            var renderer = obj.GetComponent<Renderer>();
            float bottomY = renderer != null ? renderer.bounds.min.y : obj.transform.position.y;
            Vector3 pivotPoint = new Vector3(obj.transform.position.x, bottomY, obj.transform.position.z);

            _swaying[obj] = new SwayState
            {
                OriginalRotation = obj.transform.rotation,
                OriginalPosition = obj.transform.position,
                PivotPoint = pivotPoint,
                PushDirection = pushDir,
                Intensity = proximity,
                Timer = 0f,
                Duration = 1.8f
            };
        }

        private void TriggerNudge(GameObject obj, Vector3 playerPos, float multiplier)
        {
            // XZ 平面上的推离方向，不影响 Y（防止上弹）
            Vector3 pushDir = playerPos - obj.transform.position;
            pushDir.y = 0f;
            if (pushDir.sqrMagnitude < 0.001f) return;
            pushDir.Normalize();

            float lateralPush = Vector3.Dot(pushDir, _player.CurrentAnchor.Normal);
            _player.LocalOffset += new Vector2(lateralPush, 0f) * _nudgeForce * multiplier * Time.deltaTime;
        }

        private void UpdateSway()
        {
            var toRemove = new List<GameObject>();

            foreach (var kvp in _swaying)
            {
                var obj = kvp.Key;
                var state = kvp.Value;

                if (obj == null || !obj.activeSelf)
                {
                    toRemove.Add(obj);
                    continue;
                }

                state.Timer += Time.deltaTime;
                float t = state.Timer / state.Duration;

                if (t >= 1f)
                {
                    // 恢复原始状态
                    obj.transform.rotation = state.OriginalRotation;
                    obj.transform.position = state.OriginalPosition;
                    toRemove.Add(obj);
                    continue;
                }

                // 阻尼摇晃：sin 衰减，幅度由 Intensity（接近程度）决定
                float swayAmount = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * _swayAngle * state.Intensity;
                Vector3 swayAxis = Vector3.Cross(Vector3.up, state.PushDirection).normalized;
                if (swayAxis.sqrMagnitude < 0.01f) swayAxis = Vector3.right;

                // 绕底部轴心旋转：先平移到轴心，旋转，再平移回来
                Quaternion swayRot = Quaternion.AngleAxis(swayAmount, swayAxis);
                Vector3 offset = state.OriginalPosition - state.PivotPoint;
                obj.transform.position = state.PivotPoint + swayRot * offset;
                obj.transform.rotation = swayRot * state.OriginalRotation;
            }

            foreach (var obj in toRemove)
                _swaying.Remove(obj);
        }

        private class SwayState
        {
            public Quaternion OriginalRotation;
            public Vector3 OriginalPosition;
            public Vector3 PivotPoint;
            public Vector3 PushDirection;
            public float Intensity; // 0~1，接近程度，控制摇晃幅度
            public float Timer;
            public float Duration;
        }
    }
}
