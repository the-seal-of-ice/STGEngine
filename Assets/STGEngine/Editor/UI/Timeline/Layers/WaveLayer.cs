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
            RebuildBlockList();
            return _blocks;
        }

        // ── Timeline parameters ──

        public float TotalDuration => _wave.Duration;

        public bool IsSequential => false;

        // ── Interaction ──

        public bool CanAddBlock => false; // Adding enemies is done via Wave editor, not timeline

        public bool CanDoubleClickEnter(ITimelineBlock block) => false;

        public ITimelineLayer CreateChildLayer(ITimelineBlock block) => null;

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            return Array.Empty<ContextMenuEntry>();
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
            // Wave preview not implemented yet
            playback?.LoadSegment(null);
        }

        // ── Data access ──

        public Wave Wave => _wave;
        public string WaveId => _waveId;

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
