using UnityEngine;
using System;
using System.Collections.Generic;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 危险障碍物碰撞检测。每帧检查玩家与活跃 Chunk 中
    /// IsHazard 障碍物的距离，触发碰撞时发出事件。
    /// 不使用 Unity 物理系统，纯距离检测。
    /// </summary>
    [AddComponentMenu("STGEngine/Scene/HazardCollision")]
    public class HazardCollision : MonoBehaviour
    {
        [Header("Collision Settings")]
        [SerializeField, Tooltip("玩家碰撞半径（米），直径 1.6m")]
        private float _playerRadius = 0.8f;

        [SerializeField, Tooltip("碰撞后的无敌时间（秒）")]
        private float _invincibilityDuration = 2f;

        /// <summary>碰撞事件：参数为碰撞的障碍物实例。</summary>
        public event Action<ObstacleInstance> OnHazardHit;

        /// <summary>当前是否处于无敌状态。</summary>
        public bool IsInvincible => _invincibleTimer > 0f;

        /// <summary>累计碰撞次数（测试用）。</summary>
        public int HitCount { get; private set; }

        private PlayerAnchorController _player;
        private ChunkGenerator _generator;
        private float _invincibleTimer;
        private bool _initialized;

        /// <summary>初始化。</summary>
        public void Initialize(PlayerAnchorController player, ChunkGenerator generator)
        {
            _player = player;
            _generator = generator;
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            if (_invincibleTimer > 0f)
            {
                _invincibleTimer -= Time.deltaTime;
                return;
            }

            Vector3 playerPos = _player.WorldPosition;

            foreach (var chunk in _generator.ActiveChunks)
            {
                if (!chunk.IsActive) continue;

                foreach (var obs in chunk.Obstacles)
                {
                    if (obs.Config == null || !obs.Config.IsHazard) continue;
                    if (obs.GameObject == null || !obs.GameObject.activeSelf) continue;

                    // 简单距离检测（XZ 平面 + Y）
                    float dist = Vector3.Distance(playerPos, obs.GameObject.transform.position);
                    // 障碍物碰撞半径：用 renderer bounds 的最小水平 extent
                    float obsRadius = 1f;
                    var renderer = obs.GameObject.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        var ext = renderer.bounds.extents;
                        obsRadius = Mathf.Min(ext.x, ext.z) * 0.8f; // 略小于视觉，给容错
                    }

                    if (dist < _playerRadius + obsRadius)
                    {
                        HitCount++;
                        _invincibleTimer = _invincibilityDuration;
                        OnHazardHit?.Invoke(obs);
                        return; // 一帧只触发一次
                    }
                }
            }
        }
    }
}
