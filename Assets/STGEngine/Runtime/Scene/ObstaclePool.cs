using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 按预制体类型分池的障碍物对象池。
    /// 避免频繁实例化/销毁 GameObject。
    /// </summary>
    public class ObstaclePool
    {
        private readonly Dictionary<string, Queue<GameObject>> _pools = new();
        private readonly Dictionary<string, GameObject> _prefabCache = new();
        private readonly Transform _poolRoot;

        /// <summary>
        /// 创建对象池。
        /// </summary>
        /// <param name="poolRoot">池中非活跃物体的父 Transform（隐藏用）。</param>
        public ObstaclePool(Transform poolRoot)
        {
            _poolRoot = poolRoot;
        }

        /// <summary>
        /// 从池中获取一个障碍物实例。如果池为空则实例化新的。
        /// </summary>
        /// <param name="prefabPath">预制体资源路径（Resources 下）。</param>
        /// <returns>激活的 GameObject，或 null（如果预制体加载失败）。</returns>
        public GameObject Get(string prefabPath)
        {
            if (!_pools.ContainsKey(prefabPath))
                _pools[prefabPath] = new Queue<GameObject>();

            GameObject obj;
            if (_pools[prefabPath].Count > 0)
            {
                obj = _pools[prefabPath].Dequeue();
                obj.SetActive(true);
            }
            else
            {
                var prefab = GetPrefab(prefabPath);
                if (prefab == null) return null;
                obj = Object.Instantiate(prefab);
            }

            return obj;
        }

        /// <summary>
        /// 归还障碍物实例到池中。
        /// </summary>
        public void Return(string prefabPath, GameObject obj)
        {
            if (obj == null) return;
            obj.SetActive(false);
            obj.transform.SetParent(_poolRoot);

            if (!_pools.ContainsKey(prefabPath))
                _pools[prefabPath] = new Queue<GameObject>();

            _pools[prefabPath].Enqueue(obj);
        }

        /// <summary>
        /// 销毁池中所有物体。
        /// </summary>
        public void Clear()
        {
            foreach (var kvp in _pools)
            {
                while (kvp.Value.Count > 0)
                {
                    var obj = kvp.Value.Dequeue();
                    if (obj != null) Object.Destroy(obj);
                }
            }
            _pools.Clear();
            _prefabCache.Clear();
        }

        private GameObject GetPrefab(string path)
        {
            if (_prefabCache.TryGetValue(path, out var cached))
                return cached;

            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null)
                _prefabCache[path] = prefab;

            return prefab;
        }
    }
}
