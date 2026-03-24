using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Object pool for PatternPreviewer instances.
    /// Pre-creates a set of previewers and activates/deactivates on demand.
    /// </summary>
    public class PreviewerPool
    {
        private readonly List<PatternPreviewer> _all = new();
        private readonly Stack<PatternPreviewer> _available = new();
        private readonly Transform _parent;
        private readonly Mesh _defaultMesh;
        private readonly Material _defaultMaterial;

        public PreviewerPool(Transform parent, Mesh defaultMesh, Material defaultMaterial, int initialSize = 6)
        {
            _parent = parent;
            _defaultMesh = defaultMesh;
            _defaultMaterial = defaultMaterial;

            for (int i = 0; i < initialSize; i++)
                _available.Push(CreateInstance(i));
        }

        /// <summary>
        /// Get an active previewer from the pool. Creates a new one if pool is empty.
        /// </summary>
        public PatternPreviewer Acquire()
        {
            PatternPreviewer previewer;
            if (_available.Count > 0)
            {
                previewer = _available.Pop();
            }
            else
            {
                previewer = CreateInstance(_all.Count);
            }

            previewer.gameObject.SetActive(true);
            return previewer;
        }

        /// <summary>
        /// Return a previewer to the pool.
        /// </summary>
        public void Release(PatternPreviewer previewer)
        {
            previewer.Pattern = null;
            previewer.Playback.Reset();
            previewer.gameObject.SetActive(false);
            _available.Push(previewer);
        }

        /// <summary>
        /// Release all active previewers back to the pool.
        /// </summary>
        public void ReleaseAll()
        {
            foreach (var p in _all)
            {
                if (p.gameObject.activeSelf)
                {
                    p.Pattern = null;
                    p.Playback.Reset();
                    p.gameObject.SetActive(false);
                    _available.Push(p);
                }
            }
        }

        /// <summary>
        /// Update bullet visuals for all pooled previewers.
        /// </summary>
        public void UpdateVisuals(Mesh mesh, Material material)
        {
            foreach (var p in _all)
                p.SetBulletVisuals(mesh, material);
        }

        /// <summary>
        /// Destroy all pooled GameObjects.
        /// </summary>
        public void Dispose()
        {
            foreach (var p in _all)
            {
                if (p != null && p.gameObject != null)
                    Object.Destroy(p.gameObject);
            }
            _all.Clear();
            _available.Clear();
        }

        private PatternPreviewer CreateInstance(int index)
        {
            var go = new GameObject($"PooledPreviewer_{index}");
            go.transform.SetParent(_parent);
            go.SetActive(false);

            var previewer = go.AddComponent<PatternPreviewer>();
            previewer.SetBulletVisuals(_defaultMesh, _defaultMaterial);
            _all.Add(previewer);
            return previewer;
        }
    }
}
