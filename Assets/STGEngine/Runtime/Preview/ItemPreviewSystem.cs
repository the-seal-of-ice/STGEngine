using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Timeline;
using STGEngine.Core.Random;
using STGEngine.Runtime.Player;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Manages preview-mode item visuals for ItemDrop and AutoCollect events.
    /// Items use simple physics (gravity + scatter) and are rendered via Gizmos.
    /// Uses DeterministicRng for reproducible scatter directions.
    /// </summary>
    public class ItemPreviewSystem
    {
        private struct PreviewItem
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public ItemType Type;
            public bool Active;
            public float Elapsed;
        }

        private readonly List<PreviewItem> _items = new();
        private bool _autoCollecting;
        private Vector3 _collectTarget = new(0f, 15f, 0f); // screen top world approx
        private DeterministicRng _rng;

        private const float Gravity = -9.8f;
        private const float InitialUpSpeed = 5f;
        private const float CollectSpeed = 20f;
        private const float ItemLifetime = 10f;

        public ItemPreviewSystem(int seed = 42)
        {
            _rng = new DeterministicRng(seed);
        }

        /// <summary>
        /// Spawn items based on ItemDropParams.
        /// </summary>
        public void SpawnItems(ItemDropParams p, Vector3 bossPosition)
        {
            Vector3 origin = p.Pattern == DropPattern.AtPosition ? p.Position : bossPosition;

            for (int i = 0; i < p.Count; i++)
            {
                Vector3 scatter = Vector3.zero;
                switch (p.Pattern)
                {
                    case DropPattern.RandomSpread:
                        scatter = new Vector3(
                            _rng.Range(-1f, 1f),
                            _rng.Range(0f, 1f),
                            _rng.Range(-1f, 1f)
                        ).normalized * _rng.Range(0f, p.SpreadRadius);
                        break;
                    case DropPattern.ArcSpread:
                        float angle = (float)i / Mathf.Max(1, p.Count - 1) * Mathf.PI - Mathf.PI * 0.5f;
                        scatter = new Vector3(
                            Mathf.Cos(angle) * p.SpreadRadius,
                            0f,
                            Mathf.Sin(angle) * p.SpreadRadius * 0.5f
                        );
                        break;
                    case DropPattern.FromBoss:
                        scatter = new Vector3(
                            _rng.Range(-1f, 1f) * 2f,
                            _rng.Range(0.5f, 1f),
                            _rng.Range(-1f, 1f) * 2f
                        );
                        break;
                }

                _items.Add(new PreviewItem
                {
                    Position = origin + scatter * 0.3f,
                    Velocity = new Vector3(scatter.x * 2f, InitialUpSpeed + _rng.Range(0f, 2f), scatter.z * 2f),
                    Type = p.Type,
                    Active = true,
                    Elapsed = 0f
                });
            }
        }

        /// <summary>
        /// Trigger auto-collect: all active items fly toward the collect target.
        /// </summary>
        public void TriggerAutoCollect()
        {
            _autoCollecting = true;
        }

        /// <summary>
        /// Advance item physics each frame.
        /// </summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (!item.Active) continue;

                item.Elapsed += deltaTime;
                if (item.Elapsed > ItemLifetime)
                {
                    item.Active = false;
                    _items[i] = item;
                    continue;
                }

                if (_autoCollecting)
                {
                    // Fly toward collect target
                    Vector3 dir = (_collectTarget - item.Position).normalized;
                    item.Velocity = dir * CollectSpeed;
                    item.Position += item.Velocity * deltaTime;

                    // Deactivate when close to target
                    if (Vector3.Distance(item.Position, _collectTarget) < 0.5f)
                        item.Active = false;
                }
                else
                {
                    // Normal physics: gravity
                    item.Velocity += new Vector3(0f, Gravity, 0f) * deltaTime;
                    item.Position += item.Velocity * deltaTime;

                    // Floor bounce at y=0
                    if (item.Position.y < 0f)
                    {
                        item.Position = new Vector3(item.Position.x, 0f, item.Position.z);
                        item.Velocity = new Vector3(item.Velocity.x, Mathf.Abs(item.Velocity.y) * 0.3f, item.Velocity.z);
                    }
                }

                _items[i] = item;
            }
        }

        /// <summary>
        /// Check if player is close enough to pick up items.
        /// Returns counts of each item type picked up.
        /// </summary>
        public ItemPickupResult CheckPickup(Vector3 playerPos, float collectRadius)
        {
            var result = new ItemPickupResult();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (!item.Active) continue;

                if (Vector3.Distance(item.Position, playerPos) <= collectRadius)
                {
                    item.Active = false;
                    _items[i] = item;
                    switch (item.Type)
                    {
                        case ItemType.PowerSmall:   result.PowerSmallCount++; break;
                        case ItemType.PowerLarge:   result.PowerLargeCount++; break;
                        case ItemType.PointItem:    result.PointItemCount++; break;
                        case ItemType.BombFragment: result.BombFragmentCount++; break;
                        case ItemType.LifeFragment: result.LifeFragmentCount++; break;
                        case ItemType.FullPower:    result.FullPowerCount++; break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Spawn Power items at death position (called when player dies).
        /// </summary>
        public void SpawnDeathDrop(Vector3 deathPosition, int powerItemCount)
        {
            for (int i = 0; i < powerItemCount; i++)
            {
                var scatter = new Vector3(
                    _rng.Range(-1f, 1f),
                    _rng.Range(0.5f, 1.5f),
                    _rng.Range(-1f, 1f)
                );
                _items.Add(new PreviewItem
                {
                    Position = deathPosition + scatter * 0.3f,
                    Velocity = scatter * 3f + Vector3.up * 4f,
                    Type = ItemType.PowerSmall,
                    Active = true,
                    Elapsed = 0f
                });
            }
        }

        /// <summary>Set dynamic collect target (player position).</summary>
        public void SetCollectTarget(Vector3 target)
        {
            _collectTarget = target;
        }

        /// <summary>
        /// Draw item gizmos for preview visualization.
        /// </summary>
        public void DrawGizmos()
        {
            foreach (var item in _items)
            {
                if (!item.Active) continue;

                Gizmos.color = GetItemColor(item.Type);
                Gizmos.DrawSphere(item.Position, GetItemSize(item.Type));
            }
        }

        /// <summary>Reset all items.</summary>
        public void Reset()
        {
            _items.Clear();
            _autoCollecting = false;
        }

        private static Color GetItemColor(ItemType type) => type switch
        {
            ItemType.PowerSmall   => new Color(1f, 0.3f, 0.3f, 0.8f),
            ItemType.PowerLarge   => new Color(1f, 0.1f, 0.1f, 0.9f),
            ItemType.PointItem    => new Color(0.3f, 0.5f, 1f, 0.8f),
            ItemType.BombFragment => new Color(0.2f, 1f, 0.2f, 0.8f),
            ItemType.LifeFragment => new Color(1f, 0.4f, 0.8f, 0.8f),
            ItemType.FullPower    => new Color(1f, 0.8f, 0.1f, 0.9f),
            _ => Color.white
        };

        private static float GetItemSize(ItemType type) => type switch
        {
            ItemType.PowerLarge => 0.3f,
            ItemType.FullPower  => 0.4f,
            _ => 0.2f
        };
    }
}
