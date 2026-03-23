using System;
using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Runtime.Rendering
{
    /// <summary>
    /// Batched GPU Instancing renderer for bullets.
    /// Groups instances by (mesh, material) pair; each group is drawn
    /// via Graphics.DrawMeshInstanced with per-instance color.
    /// </summary>
    public class BulletRenderer : IDisposable
    {
        // GPU Instancing hard limit per draw call
        private const int MaxPerDraw = 1023;

        /// <summary>
        /// One render batch = all instances sharing the same mesh + material.
        /// </summary>
        public class RenderBatch
        {
            public Mesh Mesh;
            public Material Material;

            // Per-frame instance data (cleared after Flush)
            public readonly List<Matrix4x4> Transforms = new();
            public readonly List<Vector4> Colors = new();

            // Reusable buffers to avoid per-frame allocation
            private readonly MaterialPropertyBlock _propertyBlock = new();
            private Matrix4x4[] _matrixBuffer = new Matrix4x4[MaxPerDraw];
            private Vector4[] _colorBuffer = new Vector4[MaxPerDraw];

            /// <summary>Submit one instance to this batch.</summary>
            public void Add(Vector3 position, float scale, Color color)
            {
                Transforms.Add(Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * scale));
                Colors.Add(color);
            }

            /// <summary>Issue all DrawMeshInstanced calls for this batch.</summary>
            public void Draw()
            {
                int total = Transforms.Count;
                if (total == 0) return;

                int offset = 0;
                while (offset < total)
                {
                    int count = Mathf.Min(total - offset, MaxPerDraw);

                    // Grow buffers if needed (only happens once)
                    if (_matrixBuffer.Length < count)
                    {
                        _matrixBuffer = new Matrix4x4[count];
                        _colorBuffer = new Vector4[count];
                    }

                    Transforms.CopyTo(offset, _matrixBuffer, 0, count);
                    Colors.CopyTo(offset, _colorBuffer, 0, count);

                    _propertyBlock.SetVectorArray("_Color", _colorBuffer);
                    Graphics.DrawMeshInstanced(Mesh, 0, Material, _matrixBuffer, count, _propertyBlock);

                    offset += count;
                }
            }

            public void Clear()
            {
                Transforms.Clear();
                Colors.Clear();
            }
        }

        // Batch key = (mesh instance id, material instance id)
        private readonly Dictionary<(int, int), RenderBatch> _batches = new();

        /// <summary>
        /// Get or create a render batch for the given mesh + material pair.
        /// Vertical slice has one batch; the interface supports many.
        /// </summary>
        public RenderBatch GetBatch(Mesh mesh, Material material)
        {
            var key = (mesh.GetInstanceID(), material.GetInstanceID());
            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = new RenderBatch { Mesh = mesh, Material = material };
                _batches[key] = batch;
            }
            return batch;
        }

        /// <summary>
        /// Submit one bullet instance to the appropriate batch.
        /// </summary>
        public void Submit(Mesh mesh, Material material,
            Vector3 position, float scale, Color color)
        {
            GetBatch(mesh, material).Add(position, scale, color);
        }

        /// <summary>
        /// Draw all batches then clear per-frame data.
        /// Call once per frame after all Submit calls.
        /// </summary>
        public void Flush()
        {
            foreach (var batch in _batches.Values)
            {
                batch.Draw();
                batch.Clear();
            }
        }

        public void Dispose()
        {
            _batches.Clear();
        }
    }
}
