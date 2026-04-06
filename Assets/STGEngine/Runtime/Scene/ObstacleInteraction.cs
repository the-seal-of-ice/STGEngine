using UnityEngine;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 普通障碍物交互。玩家擦过非危险障碍物时触发视觉反馈：
    /// Sway = 摇晃动画（竹子/树木），Nudge = 轻推玩家。
    /// 不造成伤害，纯环境互动感。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/ObstacleInteraction")]
    public class ObstacleInteraction : MonoBehaviour
    {
        [Header("Interaction Settings")]
        [SerializeField, Tooltip("交互触发距离（米）")]
        private float _triggerRadius = 2f;

        [SerializeField, Tooltip("Nudge 推力强度")]
        private float _nudgeForce = 3f;

        [SerializeField, Tooltip("Sway 摇晃角度（度）")]
        private float _swayAngle = 10f;

        [SerializeField, Tooltip("Sway 摇晃恢复速度")]
        private float _swayRecoverySpeed = 3f;

        private PlayerAnchorController _player;
        private ChunkGenerator _generator;
        private bool _initialized;

        // 正在摇晃的障碍物
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

                    float dist = Vector3.Distance(playerPos, obs.GameObject.transform.position);
                    if (dist > _triggerRadius) continue;

                    // 触发交互
                    if (obs.Config.ContactResponse == Core.Scene.ContactResponse.Sway)
                    {
                        TriggerSway(obs.GameObject, playerPos);
                    }
                    else if (obs.Config.ContactResponse == Core.Scene.ContactResponse.Nudge)
                    {
                        TriggerNudge(obs.GameObject, playerPos);
                        TriggerSway(obs.GameObject, playerPos); // Nudge 也带摇晃
                    }
                }
            }
        }

        private void TriggerSway(GameObject obj, Vector3 playerPos)
        {
            if (_swaying.ContainsKey(obj)) return; // 已在摇晃中

            Vector3 pushDir = (obj.transform.position - playerPos).normalized;
            _swaying[obj] = new SwayState
            {
                OriginalRotation = obj.transform.rotation,
                PushDirection = pushDir,
                Timer = 0f,
                Duration = 1.5f
            };
        }

        private void TriggerNudge(GameObject obj, Vector3 playerPos)
        {
            // 轻推玩家：远离障碍物方向
            Vector3 pushDir = (playerPos - obj.transform.position).normalized;
            Vector2 push2D = new Vector2(
                Vector3.Dot(pushDir, _player.CurrentAnchor.Normal),
                pushDir.y
            );
            _player.LocalOffset += push2D * _nudgeForce * Time.deltaTime;
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
                    obj.transform.rotation = state.OriginalRotation;
                    toRemove.Add(obj);
                    continue;
                }

                // 阻尼摇晃：sin 衰减
                float swayAmount = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * _swayAngle;
                Vector3 swayAxis = Vector3.Cross(Vector3.up, state.PushDirection).normalized;
                if (swayAxis.sqrMagnitude < 0.01f) swayAxis = Vector3.right;

                obj.transform.rotation = state.OriginalRotation * Quaternion.AngleAxis(swayAmount, swayAxis);
            }

            foreach (var obj in toRemove)
                _swaying.Remove(obj);
        }

        private class SwayState
        {
            public Quaternion OriginalRotation;
            public Vector3 PushDirection;
            public float Timer;
            public float Duration;
        }
    }
}
