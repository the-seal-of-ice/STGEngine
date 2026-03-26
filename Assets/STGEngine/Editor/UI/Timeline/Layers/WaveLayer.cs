using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Runtime.Preview;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for an EnemyInstance within a Wave.
    /// StartTime = SpawnDelay, Duration = estimated from path length or default.
    /// Thumbnail: top-down (XZ) path polyline with time coloring.
    /// </summary>
    public class EnemyInstanceBlock : ITimelineBlock
    {
        private readonly EnemyInstance _enemy;
        private readonly int _index;

        public EnemyInstanceBlock(EnemyInstance enemy, int index)
        {
            _enemy = enemy;
            _index = index;
        }

        public string Id => $"enemy_{_index}_{_enemy.EnemyTypeId}";

        public string DisplayLabel => _enemy.EnemyTypeId;

        public float StartTime
        {
            get => _enemy.SpawnDelay;
            set => _enemy.SpawnDelay = Mathf.Max(0f, value);
        }

        public float Duration
        {
            get
            {
                // Estimate duration from path: last keyframe time, or default 3s
                if (_enemy.Path != null && _enemy.Path.Count > 0)
                    return Mathf.Max(1f, _enemy.Path[_enemy.Path.Count - 1].Time);
                return 3f;
            }
            set { } // Path-derived, not directly editable here
        }

        public Color BlockColor
        {
            get
            {
                int hash = _enemy.EnemyTypeId?.GetHashCode() ?? 0;
                float hue = 0.05f + Mathf.Abs(hash % 50) / 360f; // Red-orange range
                return Color.HSVToRGB(hue, 0.5f, 0.6f);
            }
        }

        public bool CanMove => true; // Free-form by SpawnDelay

        public float DesignEstimate { get => -1f; set { } }

        public object DataSource => _enemy;

        public bool IsModified => false;

        // ── Thumbnail: top-down path polyline ──

        public bool HasThumbnail => _enemy.Path != null && _enemy.Path.Count >= 2;
        public bool ThumbnailInline => true;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            var path = _enemy.Path;
            if (path == null || path.Count < 2) return;

            float margin = 2f;
            float drawW = blockWidth - margin * 2;
            float drawH = blockHeight - margin * 2;
            if (drawW < 4f || drawH < 4f) return;

            // Top-down projection: X → screen X, Z → screen Y
            float xMin = float.MaxValue, xMax = float.MinValue;
            float zMin = float.MaxValue, zMax = float.MinValue;
            float totalTime = path[path.Count - 1].Time;
            if (totalTime < 0.001f) totalTime = 1f;

            foreach (var kf in path)
            {
                if (kf.Position.x < xMin) xMin = kf.Position.x;
                if (kf.Position.x > xMax) xMax = kf.Position.x;
                if (kf.Position.z < zMin) zMin = kf.Position.z;
                if (kf.Position.z > zMax) zMax = kf.Position.z;
            }

            float bw = xMax - xMin;
            float bh = zMax - zMin;
            if (bw < 0.01f) bw = 1f;
            if (bh < 0.01f) bh = 1f;

            float scale = Mathf.Min(drawW / bw, drawH / bh);
            float offsetX = margin + (drawW - bw * scale) * 0.5f;
            float offsetY = margin + (drawH - bh * scale) * 0.5f;

            // Draw path segments with time coloring (blue → red)
            painter.lineWidth = 1.5f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float x0 = offsetX + (path[i].Position.x - xMin) * scale;
                float y0 = offsetY + (path[i].Position.z - zMin) * scale;
                float x1 = offsetX + (path[i + 1].Position.x - xMin) * scale;
                float y1 = offsetY + (path[i + 1].Position.z - zMin) * scale;

                float tMid = (path[i].Time + path[i + 1].Time) * 0.5f / totalTime;
                painter.strokeColor = Color.Lerp(
                    new Color(0.3f, 0.7f, 1f, 0.9f),  // Early: cyan-blue
                    new Color(1f, 0.4f, 0.2f, 0.9f),  // Late: orange-red
                    tMid);

                painter.BeginPath();
                painter.MoveTo(new Vector2(x0, y0));
                painter.LineTo(new Vector2(x1, y1));
                painter.Stroke();
            }

            // Draw start point marker (small filled circle)
            float sx = offsetX + (path[0].Position.x - xMin) * scale;
            float sy = offsetY + (path[0].Position.z - zMin) * scale;
            painter.fillColor = new Color(0.3f, 1f, 0.3f, 0.9f); // Green start
            painter.BeginPath();
            painter.Arc(new Vector2(sx, sy), 2.5f, 0f, 360f);
            painter.Fill();
        }
    }

    /// <summary>
    /// Timeline layer for a Wave's enemy instances.
    /// Blocks = EnemyInstance (free-form by SpawnDelay).
    /// Double-click not supported yet (would go to EnemyType editor).
    /// </summary>
    public class WaveLayer : ITimelineLayer
    {
        private readonly Wave _wave;
        private readonly string _waveId;
        private readonly List<EnemyInstanceBlock> _blocks = new();

        public WaveLayer(Wave wave, string waveId)
        {
            _wave = wave;
            _waveId = waveId;
            RebuildBlockList();
        }

        // ── Identity ──

        public string LayerId => $"wave:{_waveId}";
        public string DisplayName => !string.IsNullOrEmpty(_wave.Name) ? _wave.Name : _waveId;

        // ── Block data ──

        public int BlockCount => _blocks.Count;
        public ITimelineBlock GetBlock(int index) => _blocks[index];

        public IReadOnlyList<ITimelineBlock> GetAllBlocks()
        {
            return _blocks;
        }

        /// <summary>Force rebuild of block list from wave enemies.</summary>
        public void InvalidateBlocks()
        {
            RebuildBlockList();
        }

        // ── Timeline parameters ──

        public float TotalDuration => _wave.Duration;

        public bool IsSequential => false;

        // ── Interaction ──

        public bool CanAddBlock => true;

        public bool CanDoubleClickEnter(ITimelineBlock block) => false;

        public ITimelineLayer CreateChildLayer(ITimelineBlock block) => null;

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            var entries = new List<ContextMenuEntry>
            {
                new("Add Enemy", () => OnAddEnemyRequested?.Invoke())
            };

            if (selectedBlock != null)
            {
                entries.Add(new ContextMenuEntry("Delete Selected Enemy",
                    () => OnDeleteEnemyRequested?.Invoke(selectedBlock), true));
            }

            return entries;
        }

        // ── Properties panel ──

        public void BuildPropertiesPanel(VisualElement container, ITimelineBlock block)
        {
            if (block == null)
            {
                var label = new Label($"Wave: {DisplayName}\nEnemies: {_wave.Enemies.Count}\nDuration: {_wave.Duration:F1}s");
                label.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(label);
                return;
            }

            if (block.DataSource is EnemyInstance ei)
            {
                var info = new Label($"Enemy: {ei.EnemyTypeId}\n" +
                    $"SpawnDelay: {ei.SpawnDelay:F1}s\n" +
                    $"Path points: {ei.Path?.Count ?? 0}");
                info.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(info);
            }
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            // Wave layer shows enemy paths, not bullet patterns.
            // Keep parent layer's playback state intact — do nothing here.
        }

        // ── Data access ──

        public Wave Wave => _wave;
        public string WaveId => _waveId;

        // ── Layer-specific events ──

        public Action OnAddEnemyRequested;
        public Action<ITimelineBlock> OnDeleteEnemyRequested;

        // ── Internal ──

        private void RebuildBlockList()
        {
            _blocks.Clear();
            if (_wave?.Enemies == null) return;

            for (int i = 0; i < _wave.Enemies.Count; i++)
            {
                _blocks.Add(new EnemyInstanceBlock(_wave.Enemies[i], i));
            }
        }
    }
}
