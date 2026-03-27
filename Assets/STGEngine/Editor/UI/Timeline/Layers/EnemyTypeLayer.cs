using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Timeline;
using STGEngine.Runtime;
using STGEngine.Runtime.Bullet;
using STGEngine.Runtime.Preview;
using STGEngine.Editor.Commands;
using STGEngine.Editor.UI.FileManager;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// ITimelineBlock wrapper for an EnemyPattern within an EnemyType.
    /// Analogous to SpellCardPatternBlock. StartTime = Delay, Duration = Duration.
    /// Supports trajectory thumbnails via PatternLibrary resolution.
    /// </summary>
    public class EnemyPatternBlock : ITimelineBlock
    {
        private readonly EnemyPattern _pattern;
        private readonly BulletPattern _resolvedPattern;
        private readonly int _index;
        private bool _trajectoryComputed;
        private List<TrajectoryThumbnailRenderer.TrajPoint[]> _emitterTrajectories;

        public EnemyPatternBlock(EnemyPattern pattern, int index, BulletPattern resolvedPattern = null)
        {
            _pattern = pattern;
            _index = index;
            _resolvedPattern = resolvedPattern;
        }

        public string Id => $"{_pattern.PatternId}_{_index}";
        public string DisplayLabel => _pattern.PatternId;

        public float StartTime
        {
            get => _pattern.Delay;
            set => _pattern.Delay = Mathf.Max(0f, value);
        }

        public float Duration
        {
            get => _pattern.Duration;
            set => _pattern.Duration = Mathf.Max(0.1f, value);
        }

        public Color BlockColor
        {
            get
            {
                int hash = _pattern.PatternId?.GetHashCode() ?? 0;
                float hue = Mathf.Abs(hash % 360) / 360f;
                return Color.HSVToRGB(hue, 0.5f, 0.6f);
            }
        }

        public bool CanMove => true;
        public float DesignEstimate { get => -1f; set { } }
        public object DataSource => _pattern;
        public bool IsModified => false;

        // ── Thumbnail ──

        public bool HasThumbnail
        {
            get
            {
                if (_resolvedPattern != null)
                {
                    EnsureComputed();
                    return _emitterTrajectories != null && _emitterTrajectories.Count > 0;
                }
                return false;
            }
        }

        public bool ThumbnailInline => true;

        public void DrawThumbnail(Painter2D painter, float blockWidth, float blockHeight)
        {
            if (_emitterTrajectories == null || _emitterTrajectories.Count == 0) return;
            TrajectoryThumbnailRenderer.Draw(painter, blockWidth, blockHeight, _emitterTrajectories);
        }

        public void InvalidateThumbnailCache()
        {
            _trajectoryComputed = false;
            _emitterTrajectories = null;
        }

        private void EnsureComputed()
        {
            if (_trajectoryComputed) return;
            _trajectoryComputed = true;
            if (_resolvedPattern == null) return;

            float sampleDuration = Mathf.Max(10f, (_pattern.Duration > 0f ? _pattern.Duration : 5f) * 3f);
            _emitterTrajectories = TrajectoryThumbnailRenderer.ComputeEmitterOnly(_resolvedPattern, sampleDuration);
        }
    }

    /// <summary>
    /// Timeline layer for an EnemyType's pattern timeline.
    /// Blocks = EnemyPattern (free-form by Delay, can overlap).
    /// Double-click a pattern block → PatternLayer.
    /// Properties panel shows EnemyType stats (Name/Health/Scale/Color/MeshType).
    /// </summary>
    public class EnemyTypeLayer : ITimelineLayer
    {
        private readonly EnemyType _enemyType;
        private readonly string _enemyTypeId;
        private readonly STGCatalog _catalog;
        private PatternLibrary _library;
        private readonly List<EnemyPatternBlock> _blocks = new();

        public EnemyTypeLayer(EnemyType enemyType, string enemyTypeId, STGCatalog catalog)
        {
            _enemyType = enemyType;
            _enemyTypeId = enemyTypeId;
            _catalog = catalog;
            RebuildBlockList();
        }

        // ── Identity ──

        public string LayerId => $"enemytype:{_enemyTypeId}";

        public string DisplayName =>
            !string.IsNullOrEmpty(_enemyType.Name) ? _enemyType.Name : _enemyTypeId;

        // ── Block data ──

        public int BlockCount => _blocks.Count;
        public ITimelineBlock GetBlock(int index) => _blocks[index];
        public IReadOnlyList<ITimelineBlock> GetAllBlocks() => _blocks;

        public void InvalidateBlocks() => RebuildBlockList();

        // ── Timeline parameters ──

        public float TotalDuration => Mathf.Max(10f, _enemyType.PatternDuration + 5f);
        public bool IsSequential => false;

        // ── Interaction ──

        public bool CanAddBlock => true;

        public bool CanDoubleClickEnter(ITimelineBlock block)
        {
            return block is EnemyPatternBlock;
        }

        public ITimelineLayer CreateChildLayer(ITimelineBlock block)
        {
            if (block is EnemyPatternBlock epBlock && epBlock.DataSource is EnemyPattern ep)
            {
                var resolved = _library?.ResolveClone(ep.PatternId);
                if (resolved != null)
                    return new PatternLayer(resolved, ep.PatternId);
                Debug.LogWarning($"[EnemyTypeLayer] Cannot resolve pattern '{ep.PatternId}'");
            }
            return null;
        }

        // ── Context menu ──

        public IReadOnlyList<ContextMenuEntry> GetContextMenuEntries(float time, ITimelineBlock selectedBlock)
        {
            var entries = new List<ContextMenuEntry>
            {
                new("Add Pattern", () => OnAddPatternRequested?.Invoke())
            };

            if (selectedBlock != null)
            {
                entries.Add(new ContextMenuEntry("Delete Selected Pattern",
                    () => OnDeletePatternRequested?.Invoke(selectedBlock), true));
            }

            return entries;
        }

        // ── Properties panel ──

        public void BuildPropertiesPanel(VisualElement container, ITimelineBlock block)
        {
            if (block == null)
            {
                BuildEnemyTypePropertiesPanel(container, null);
                return;
            }

            if (block.DataSource is EnemyPattern ep)
            {
                var info = new Label($"Pattern: {ep.PatternId}\n" +
                    $"Delay: {ep.Delay:F1}s\n" +
                    $"Duration: {ep.Duration:F1}s");
                info.style.color = new Color(0.8f, 0.8f, 0.8f);
                container.Add(info);
            }
        }

        /// <summary>
        /// Build the full EnemyType stats editing panel (no pattern list — patterns are blocks now).
        /// commandStack is optional — if provided, edits go through Undo/Redo.
        /// </summary>
        public void BuildEnemyTypePropertiesPanel(VisualElement container, CommandStack commandStack)
        {
            var wrapper = new VisualElement();
            wrapper.style.paddingTop = 4;
            wrapper.style.paddingLeft = 8;
            wrapper.style.paddingRight = 8;

            // Title
            var title = new Label($"EnemyType: {DisplayName}");
            title.style.color = new Color(0.85f, 0.85f, 0.85f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            wrapper.Add(title);

            // ID (read-only)
            var idLabel = new Label($"ID: {_enemyTypeId}");
            idLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            idLabel.style.marginBottom = 6;
            wrapper.Add(idLabel);

            // Pattern count info
            var patInfo = new Label($"Patterns: {_enemyType.Patterns.Count}");
            patInfo.style.color = new Color(0.6f, 0.6f, 0.6f);
            patInfo.style.marginBottom = 6;
            wrapper.Add(patInfo);

            // ── Name ──
            var nameField = new TextField("Name") { value = _enemyType.Name ?? "" };
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(e =>
            {
                if (commandStack != null)
                {
                    var cmd = new PropertyChangeCommand<string>(
                        "Change EnemyType Name",
                        () => _enemyType.Name, v => _enemyType.Name = v, e.newValue);
                    commandStack.Execute(cmd);
                }
                else
                {
                    _enemyType.Name = e.newValue;
                }
                title.text = $"EnemyType: {DisplayName}";
                OnEnemyTypeChanged?.Invoke();
            });
            wrapper.Add(nameField);

            // ── Health ──
            var healthField = new FloatField("Health") { value = _enemyType.Health };
            healthField.isDelayed = true;
            healthField.RegisterValueChangedCallback(e =>
            {
                float val = Mathf.Max(1f, e.newValue);
                if (commandStack != null)
                {
                    var cmd = new PropertyChangeCommand<float>(
                        "Change EnemyType Health",
                        () => _enemyType.Health, v => _enemyType.Health = v, val);
                    commandStack.Execute(cmd);
                }
                else
                {
                    _enemyType.Health = val;
                }
                OnEnemyTypeChanged?.Invoke();
            });
            wrapper.Add(healthField);

            // ── Scale ──
            var scaleField = new FloatField("Scale") { value = _enemyType.Scale };
            scaleField.isDelayed = true;
            scaleField.RegisterValueChangedCallback(e =>
            {
                float val = Mathf.Max(0.1f, e.newValue);
                if (commandStack != null)
                {
                    var cmd = new PropertyChangeCommand<float>(
                        "Change EnemyType Scale",
                        () => _enemyType.Scale, v => _enemyType.Scale = v, val);
                    commandStack.Execute(cmd);
                }
                else
                {
                    _enemyType.Scale = val;
                }
                OnEnemyTypeChanged?.Invoke();
            });
            wrapper.Add(scaleField);

            // ── Color (foldout with swatch + RGBA fields) ──
            var colorFoldout = new Foldout { text = "Color", value = false };
            colorFoldout.style.marginTop = 6;

            var swatchRow = new VisualElement();
            swatchRow.style.flexDirection = FlexDirection.Row;
            swatchRow.style.alignItems = Align.Center;
            swatchRow.style.marginBottom = 4;

            var swatch = new VisualElement();
            swatch.style.width = 40;
            swatch.style.height = 16;
            swatch.style.backgroundColor = _enemyType.Color;
            swatch.style.borderBottomLeftRadius = 3;
            swatch.style.borderBottomRightRadius = 3;
            swatch.style.borderTopLeftRadius = 3;
            swatch.style.borderTopRightRadius = 3;
            swatchRow.Add(swatch);

            var hexLabel = new Label(ColorToHex(_enemyType.Color));
            hexLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            hexLabel.style.marginLeft = 6;
            swatchRow.Add(hexLabel);

            colorFoldout.Add(swatchRow);

            void UpdateColor(float r, float g, float b, float a)
            {
                var c = new Color(
                    Mathf.Clamp01(r), Mathf.Clamp01(g),
                    Mathf.Clamp01(b), Mathf.Clamp01(a));
                if (commandStack != null)
                {
                    var cmd = new PropertyChangeCommand<Color>(
                        "Change EnemyType Color",
                        () => _enemyType.Color, v => _enemyType.Color = v, c);
                    commandStack.Execute(cmd);
                }
                else
                {
                    _enemyType.Color = c;
                }
                swatch.style.backgroundColor = c;
                hexLabel.text = ColorToHex(c);
                OnEnemyTypeChanged?.Invoke();
            }

            var rField = new FloatField("R") { value = _enemyType.Color.r };
            rField.isDelayed = true;
            rField.RegisterValueChangedCallback(e =>
                UpdateColor(e.newValue, _enemyType.Color.g, _enemyType.Color.b, _enemyType.Color.a));
            colorFoldout.Add(rField);

            var gField = new FloatField("G") { value = _enemyType.Color.g };
            gField.isDelayed = true;
            gField.RegisterValueChangedCallback(e =>
                UpdateColor(_enemyType.Color.r, e.newValue, _enemyType.Color.b, _enemyType.Color.a));
            colorFoldout.Add(gField);

            var bField = new FloatField("B") { value = _enemyType.Color.b };
            bField.isDelayed = true;
            bField.RegisterValueChangedCallback(e =>
                UpdateColor(_enemyType.Color.r, _enemyType.Color.g, e.newValue, _enemyType.Color.a));
            colorFoldout.Add(bField);

            var aField = new FloatField("A") { value = _enemyType.Color.a };
            aField.isDelayed = true;
            aField.RegisterValueChangedCallback(e =>
                UpdateColor(_enemyType.Color.r, _enemyType.Color.g, _enemyType.Color.b, e.newValue));
            colorFoldout.Add(aField);

            wrapper.Add(colorFoldout);

            // ── MeshType (dropdown) ──
            var meshNames = Enum.GetNames(typeof(MeshType)).ToList();
            int currentMeshIdx = meshNames.IndexOf(_enemyType.MeshType.ToString());
            if (currentMeshIdx < 0) currentMeshIdx = 0;

            var meshDropdown = new DropdownField("Mesh Type", meshNames, currentMeshIdx);
            meshDropdown.RegisterValueChangedCallback(e =>
            {
                if (Enum.TryParse<MeshType>(e.newValue, out var newMesh))
                {
                    if (commandStack != null)
                    {
                        var cmd = new PropertyChangeCommand<MeshType>(
                            "Change EnemyType MeshType",
                            () => _enemyType.MeshType, v => _enemyType.MeshType = v, newMesh);
                        commandStack.Execute(cmd);
                    }
                    else
                    {
                        _enemyType.MeshType = newMesh;
                    }
                    OnEnemyTypeChanged?.Invoke();
                }
            });
            wrapper.Add(meshDropdown);

            container.Add(wrapper);
        }

        // ── Preview ──

        public void LoadPreview(TimelinePlaybackController playback)
        {
            // EnemyType pattern timeline preview — build a temp segment like SpellCardDetailLayer
            if (playback == null || _enemyType == null || _library == null)
            {
                playback?.LoadSegment(null);
                return;
            }

            var tempSegment = new TimelineSegment
            {
                Id = $"_enemytype_{_enemyTypeId}",
                Name = _enemyType.Name,
                Type = SegmentType.MidStage,
                Duration = _enemyType.PatternDuration
            };

            foreach (var ep in _enemyType.Patterns)
            {
                var pattern = _library?.Resolve(ep.PatternId);
                if (pattern == null) continue;

                // Compute spawn position: enemy path position at pattern start + offset
                var enemyPos = EvaluateEnemyPath(SourceInstance?.Path, ep.Delay);

                tempSegment.Events.Add(new SpawnPatternEvent
                {
                    Id = $"_et_evt_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                    StartTime = ep.Delay,
                    Duration = ep.Duration,
                    PatternId = ep.PatternId,
                    SpawnPosition = enemyPos + ep.Offset,
                    ResolvedPattern = pattern
                });
            }

            playback.LoadSegment(tempSegment);
        }

        // ── Data access ──

        public EnemyType EnemyType => _enemyType;
        public string EnemyTypeId => _enemyTypeId;
        public STGCatalog Catalog => _catalog;

        /// <summary>The EnemyInstance that entered this layer. Used for path-based preview positioning.</summary>
        public EnemyInstance SourceInstance { get; set; }

        /// <summary>Pattern library for resolving pattern IDs. Set by WireLayerToTrackArea.</summary>
        public PatternLibrary Library
        {
            get => _library;
            set
            {
                _library = value;
                RebuildBlockList();
            }
        }

        // ── Events ──

        /// <summary>Called after any EnemyType property is changed. Host should save to disk.</summary>
        public Action OnEnemyTypeChanged;

        /// <summary>Called when user wants to add a pattern (show picker).</summary>
        public Action OnAddPatternRequested;

        /// <summary>Called when user wants to delete a pattern block.</summary>
        public Action<ITimelineBlock> OnDeletePatternRequested;

        // ── Internal ──

        private void RebuildBlockList()
        {
            _blocks.Clear();
            if (_enemyType?.Patterns == null) return;

            for (int i = 0; i < _enemyType.Patterns.Count; i++)
            {
                var ep = _enemyType.Patterns[i];
                var resolved = _library?.Resolve(ep.PatternId);
                _blocks.Add(new EnemyPatternBlock(ep, i, resolved));
            }
        }

        // ── Helpers ──

        /// <summary>
        /// Evaluate enemy path position at a given time via linear interpolation.
        /// Returns Vector3.zero if path is null or empty.
        /// </summary>
        public static Vector3 EvaluateEnemyPath(List<PathKeyframe> path, float time)
        {
            if (path == null || path.Count == 0) return Vector3.zero;
            if (path.Count == 1 || time <= path[0].Time) return path[0].Position;
            if (time >= path[path.Count - 1].Time) return path[path.Count - 1].Position;

            for (int i = 0; i < path.Count - 1; i++)
            {
                if (time >= path[i].Time && time <= path[i + 1].Time)
                {
                    float t = (time - path[i].Time) / (path[i + 1].Time - path[i].Time);
                    return Vector3.Lerp(path[i].Position, path[i + 1].Position, t);
                }
            }
            return path[path.Count - 1].Position;
        }

        private static string ColorToHex(Color c)
        {
            return $"#{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";
        }
    }
}
