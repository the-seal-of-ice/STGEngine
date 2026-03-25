using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;
using STGEngine.Core.Serialization;
using STGEngine.Editor.UI.FileManager;

namespace STGEngine.Editor.UI.AssetLibrary
{
    /// <summary>
    /// Asset category for the library panel.
    /// </summary>
    public enum AssetCategory
    {
        Patterns,
        Waves,
        Enemies,
        SpellCards
    }

    /// <summary>
    /// Left-side collapsible asset library panel. Lists all assets from STGCatalog
    /// grouped by category (Patterns, Waves, Enemies, SpellCards).
    /// </summary>
    public class AssetLibraryPanel
    {
        public VisualElement Root { get; }

        /// <summary>Fired when user selects an asset. (category, id)</summary>
        public Action<AssetCategory, string> OnAssetSelected;

        /// <summary>Fired when user double-clicks an asset. (category, id)</summary>
        public Action<AssetCategory, string> OnAssetDoubleClicked;

        /// <summary>Fired when user clicks "Add to Timeline". (category, id)</summary>
        public Action<AssetCategory, string> OnAssetAddRequested;

        private STGCatalog _catalog;
        private bool _collapsed;
        private readonly VisualElement _content;
        private readonly Button _toggleBtn;
        private readonly VisualElement _headerBar;

        // Currently selected
        private AssetCategory? _selectedCategory;
        private string _selectedId;
        private VisualElement _selectedElement;

        // Category foldouts
        private readonly Dictionary<AssetCategory, Foldout> _foldouts = new();

        private const float PanelWidth = 180f;
        private const float CollapsedWidth = 24f;

        public AssetLibraryPanel()
        {
            Root = new VisualElement();
            Root.style.width = PanelWidth;
            Root.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 0.95f);
            Root.style.borderRightWidth = 1;
            Root.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
            Root.style.flexDirection = FlexDirection.Column;

            // ── Header bar ──
            _headerBar = new VisualElement();
            _headerBar.style.flexDirection = FlexDirection.Row;
            _headerBar.style.alignItems = Align.Center;
            _headerBar.style.height = 28;
            _headerBar.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            _headerBar.style.borderBottomWidth = 1;
            _headerBar.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            _headerBar.style.paddingLeft = 4;
            _headerBar.style.paddingRight = 4;

            _toggleBtn = new Button(() => SetCollapsed(!_collapsed));
            _toggleBtn.text = "◀";
            _toggleBtn.style.width = 20;
            _toggleBtn.style.height = 20;
            _toggleBtn.style.fontSize = 10;
            _toggleBtn.style.marginRight = 4;
            _toggleBtn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            _toggleBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            _toggleBtn.style.borderTopWidth = _toggleBtn.style.borderBottomWidth =
                _toggleBtn.style.borderLeftWidth = _toggleBtn.style.borderRightWidth = 0;
            _headerBar.Add(_toggleBtn);

            var title = new Label("Assets");
            title.style.color = new Color(0.85f, 0.85f, 0.85f);
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.name = "library-title";
            _headerBar.Add(title);

            Root.Add(_headerBar);

            // ── Content area ──
            _content = new ScrollView(ScrollViewMode.Vertical);
            _content.style.flexGrow = 1;
            _content.style.paddingTop = 4;
            _content.style.paddingBottom = 4;
            Root.Add(_content);

            // Build category foldouts
            BuildCategoryFoldout(AssetCategory.Patterns, "Patterns");
            BuildCategoryFoldout(AssetCategory.Waves, "Waves");
            BuildCategoryFoldout(AssetCategory.Enemies, "Enemies");
            BuildCategoryFoldout(AssetCategory.SpellCards, "SpellCards");

            ApplyTheme(Root);
        }

        /// <summary>
        /// Refresh the panel with current catalog data.
        /// </summary>
        public void Refresh(STGCatalog catalog)
        {
            _catalog = catalog;
            RefreshCategory(AssetCategory.Patterns, catalog.Patterns);
            RefreshCategory(AssetCategory.Waves, catalog.Waves);
            RefreshCategory(AssetCategory.Enemies, catalog.EnemyTypes);
            RefreshCategory(AssetCategory.SpellCards, catalog.SpellCards);
            ApplyTheme(Root);
        }

        public void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            _content.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            Root.style.width = collapsed ? CollapsedWidth : PanelWidth;
            _toggleBtn.text = collapsed ? "▶" : "◀";

            // Hide title when collapsed
            var title = _headerBar.Q<Label>("library-title");
            if (title != null)
                title.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Force theme override (call after Unity Runtime Theme applies).</summary>
        public void ForceApplyTheme()
        {
            ApplyTheme(Root);
        }

        // ─── Internal ───

        private void BuildCategoryFoldout(AssetCategory category, string label)
        {
            var foldout = new Foldout { text = label, value = true };
            foldout.style.marginLeft = 4;
            foldout.style.marginRight = 4;
            foldout.style.marginTop = 2;
            foldout.style.marginBottom = 2;

            // Style the toggle label
            var toggle = foldout.Q<Toggle>();
            if (toggle != null)
            {
                toggle.style.marginBottom = 2;
                var lbl = toggle.Q<Label>();
                if (lbl != null)
                {
                    lbl.style.color = new Color(0.7f, 0.85f, 1f);
                    lbl.style.fontSize = 11;
                    lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
            }

            _foldouts[category] = foldout;
            _content.Add(foldout);
        }

        private void RefreshCategory(AssetCategory category, List<CatalogEntry> entries)
        {
            if (!_foldouts.TryGetValue(category, out var foldout)) return;

            // Clear existing items (keep the Toggle header)
            var toggle = foldout.Q<Toggle>();
            foldout.Clear();
            // Foldout.Clear() removes the toggle too, so we need to rebuild
            // Actually, Foldout content goes into contentContainer, let's clear that
            // Re-approach: just remove non-toggle children
            // Foldout in UI Toolkit: contentContainer is separate from the toggle
            // Let's just rebuild the foldout content
            foreach (var entry in entries)
            {
                var item = BuildAssetItem(category, entry);
                foldout.Add(item);
            }

            // Add "+" button at the bottom
            var addBtn = new Button(() => OnAddNewAsset(category));
            addBtn.text = "+";
            addBtn.style.height = 20;
            addBtn.style.fontSize = 12;
            addBtn.style.marginTop = 2;
            addBtn.style.marginLeft = 4;
            addBtn.style.marginRight = 4;
            addBtn.style.backgroundColor = new Color(0.25f, 0.35f, 0.25f);
            addBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            addBtn.style.borderTopWidth = addBtn.style.borderBottomWidth =
                addBtn.style.borderLeftWidth = addBtn.style.borderRightWidth = 0;
            foldout.Add(addBtn);
        }

        private VisualElement BuildAssetItem(AssetCategory category, CatalogEntry entry)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.height = 22;
            item.style.paddingLeft = 8;
            item.style.paddingRight = 4;
            item.style.marginTop = 1;
            item.style.marginBottom = 1;
            item.style.borderBottomWidth = 1;
            item.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            // Category icon/color indicator
            var indicator = new VisualElement();
            indicator.style.width = 6;
            indicator.style.height = 6;
            indicator.style.borderTopLeftRadius = indicator.style.borderTopRightRadius =
                indicator.style.borderBottomLeftRadius = indicator.style.borderBottomRightRadius = 3;
            indicator.style.marginRight = 6;
            indicator.style.backgroundColor = GetCategoryColor(category);
            item.Add(indicator);

            var label = new Label(string.IsNullOrEmpty(entry.Name) ? entry.Id : entry.Name);
            label.style.color = new Color(0.85f, 0.85f, 0.85f);
            label.style.fontSize = 11;
            label.style.flexGrow = 1;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            item.Add(label);

            // "Add to Timeline" button (only for types that can be added directly)
            if (category == AssetCategory.Patterns || category == AssetCategory.Waves)
            {
                var addToTimelineBtn = new Button(() =>
                {
                    OnAssetAddRequested?.Invoke(category, entry.Id);
                })
                { text = "\u25b6" };
                addToTimelineBtn.style.width = 18;
                addToTimelineBtn.style.height = 16;
                addToTimelineBtn.style.fontSize = 9;
                addToTimelineBtn.style.paddingLeft = addToTimelineBtn.style.paddingRight = 0;
                addToTimelineBtn.style.paddingTop = addToTimelineBtn.style.paddingBottom = 0;
                addToTimelineBtn.style.marginLeft = 2;
                addToTimelineBtn.style.backgroundColor = new Color(0.2f, 0.35f, 0.2f);
                addToTimelineBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
                addToTimelineBtn.style.borderTopWidth = addToTimelineBtn.style.borderBottomWidth =
                    addToTimelineBtn.style.borderLeftWidth = addToTimelineBtn.style.borderRightWidth = 0;
                addToTimelineBtn.tooltip = "Add to Timeline";
                item.Add(addToTimelineBtn);
            }

            // Hover highlight
            item.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (item != _selectedElement)
                    item.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 0.5f);
            });
            item.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (item != _selectedElement)
                    item.style.backgroundColor = StyleKeyword.Null;
            });

            // Click to select
            item.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    OnAssetDoubleClicked?.Invoke(category, entry.Id);
                    return;
                }

                SelectItem(category, entry.Id, item);
            });

            return item;
        }

        private void SelectItem(AssetCategory category, string id, VisualElement element)
        {
            // Deselect previous
            if (_selectedElement != null)
                _selectedElement.style.backgroundColor = StyleKeyword.Null;

            _selectedCategory = category;
            _selectedId = id;
            _selectedElement = element;
            element.style.backgroundColor = new Color(0.2f, 0.35f, 0.55f, 0.7f);

            OnAssetSelected?.Invoke(category, id);
        }

        private void OnAddNewAsset(AssetCategory category)
        {
            if (_catalog == null) return;

            string id, name;
            switch (category)
            {
                case AssetCategory.Patterns:
                    id = _catalog.EnsureUniquePatternId("new_pattern");
                    name = "New Pattern";
                    var pattern = new BulletPattern
                    {
                        Id = id, Name = name,
                        Emitter = new STGEngine.Core.Emitters.RingEmitter()
                    };
                    YamlSerializer.SerializeToFile(pattern, _catalog.GetPatternPath(id));
                    _catalog.AddOrUpdatePattern(id, name);
                    break;

                case AssetCategory.Enemies:
                    id = _catalog.EnsureUniqueEnemyTypeId("new_enemy");
                    name = "New Enemy";
                    var enemy = new EnemyType { Id = id, Name = name };
                    YamlSerializer.SerializeEnemyTypeToFile(enemy, _catalog.GetEnemyTypePath(id));
                    _catalog.AddOrUpdateEnemyType(id, name);
                    break;

                case AssetCategory.Waves:
                    id = _catalog.EnsureUniqueWaveId("new_wave");
                    name = "New Wave";
                    var wave = new Wave { Id = id, Name = name };
                    YamlSerializer.SerializeWaveToFile(wave, _catalog.GetWavePath(id));
                    _catalog.AddOrUpdateWave(id, name);
                    break;

                case AssetCategory.SpellCards:
                    id = _catalog.EnsureUniqueSpellCardId("new_spell");
                    name = "New SpellCard";
                    var spell = new SpellCard { Id = id, Name = name };
                    YamlSerializer.SerializeSpellCardToFile(spell, _catalog.GetSpellCardPath(id));
                    _catalog.AddOrUpdateSpellCard(id, name);
                    break;

                default:
                    return;
            }

            STGCatalog.Save(_catalog);
            Refresh(_catalog);
        }

        private static Color GetCategoryColor(AssetCategory category)
        {
            return category switch
            {
                AssetCategory.Patterns => new Color(0.3f, 0.7f, 1f),    // Blue
                AssetCategory.Waves => new Color(0.3f, 0.9f, 0.4f),     // Green
                AssetCategory.Enemies => new Color(1f, 0.5f, 0.3f),     // Orange
                AssetCategory.SpellCards => new Color(0.9f, 0.3f, 0.9f), // Purple
                _ => Color.gray
            };
        }

        private static void ApplyTheme(VisualElement root)
        {
            var lightText = new Color(0.85f, 0.85f, 0.85f);
            root.Query<Label>().ForEach(l => l.style.color = lightText);
            root.Query<TextElement>().ForEach(t => t.style.color = lightText);
            root.Query<Button>().ForEach(b =>
            {
                b.style.color = lightText;
                if (b.style.backgroundColor == StyleKeyword.Null)
                    b.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f);
            });

            // Schedule delayed re-apply for Runtime Theme override
            root.schedule.Execute(() =>
            {
                root.Query<Label>().ForEach(l => l.style.color = lightText);
                root.Query<TextElement>().ForEach(t => t.style.color = lightText);
            }).ExecuteLater(50);
            root.schedule.Execute(() =>
            {
                root.Query<Label>().ForEach(l => l.style.color = lightText);
                root.Query<TextElement>().ForEach(t => t.style.color = lightText);
            }).ExecuteLater(200);
        }
    }
}
