using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Fired after a delete or rename modifies the catalog.</summary>
        public Action OnCatalogChanged;

        /// <summary>
        /// Query whether the current timeline layer can accept this asset type.
        /// Returns true if the asset can be added. Used to enable/disable "Add to Timeline".
        /// </summary>
        public Func<AssetCategory, bool> CanAddToTimeline;

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

        // Active context menu overlay (dismiss on next click)
        private VisualElement _contextMenu;

        private const float PanelWidth = 240f;
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
            BuildCategoryFoldout(AssetCategory.Enemies, "Enemy Types");
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

        /// <summary>
        /// Refresh the enabled/disabled state of all "Add to Timeline" buttons
        /// based on the current timeline layer. Call when the layer changes.
        /// </summary>
        public void RefreshButtonStates()
        {
            foreach (var kvp in _foldouts)
            {
                var cat = kvp.Key;
                if (cat == AssetCategory.Enemies) continue; // no add button

                bool canAdd = CanAddToTimeline?.Invoke(cat) ?? true;
                var foldout = kvp.Value;
                foldout.Query<Button>(className: "add-to-timeline-btn").ForEach(btn =>
                {
                    btn.SetEnabled(canAdd);
                    btn.style.opacity = canAdd ? 1f : 0.35f;
                });
            }
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

            // "Add to Timeline" button (Patterns/Waves → MidStage, SpellCards → BossFight)
            if (category == AssetCategory.Patterns || category == AssetCategory.Waves
                || category == AssetCategory.SpellCards)
            {
                var catCapture = category;
                var addToTimelineBtn = new Button(() =>
                {
                    OnAssetAddRequested?.Invoke(category, entry.Id);
                })
                { text = "\u25b6" };
                addToTimelineBtn.AddToClassList("add-to-timeline-btn");
                addToTimelineBtn.style.width = 18;
                addToTimelineBtn.style.height = 16;
                addToTimelineBtn.style.fontSize = 9;
                addToTimelineBtn.style.paddingLeft = addToTimelineBtn.style.paddingRight = 0;
                addToTimelineBtn.style.paddingTop = addToTimelineBtn.style.paddingBottom = 0;
                addToTimelineBtn.style.marginLeft = 2;
                addToTimelineBtn.style.backgroundColor = category == AssetCategory.SpellCards
                    ? new Color(0.3f, 0.2f, 0.35f)
                    : new Color(0.2f, 0.35f, 0.2f);
                addToTimelineBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
                addToTimelineBtn.style.borderTopWidth = addToTimelineBtn.style.borderBottomWidth =
                    addToTimelineBtn.style.borderLeftWidth = addToTimelineBtn.style.borderRightWidth = 0;
                addToTimelineBtn.tooltip = category == AssetCategory.SpellCards
                    ? "Add to BossFight Segment"
                    : "Add to Timeline";
                // Disable if current layer can't accept this asset type
                bool canAdd = CanAddToTimeline?.Invoke(catCapture) ?? true;
                addToTimelineBtn.SetEnabled(canAdd);
                if (!canAdd) addToTimelineBtn.style.opacity = 0.35f;
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

            // Right-click context menu (Runtime UI Toolkit doesn't support ContextualMenuManipulator)
            item.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return; // right-click only
                evt.StopPropagation();
                SelectItem(category, entry.Id, item);
                ShowContextMenu(evt.position, category, entry);
            });

            return item;
        }

        // ─── Custom Context Menu (Runtime-compatible) ───

        private void ShowContextMenu(Vector3 position, AssetCategory category, CatalogEntry entry)
        {
            DismissContextMenu();

            // Full-screen dismiss layer
            var dismiss = new VisualElement();
            dismiss.style.position = Position.Absolute;
            dismiss.style.left = dismiss.style.right = dismiss.style.top = dismiss.style.bottom = 0;
            dismiss.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                DismissContextMenu();
            });

            // Menu panel
            var menu = new VisualElement();
            menu.style.position = Position.Absolute;
            menu.style.left = position.x;
            menu.style.top = position.y;
            menu.style.backgroundColor = new Color(0.22f, 0.22f, 0.25f, 0.98f);
            menu.style.borderTopLeftRadius = menu.style.borderTopRightRadius =
                menu.style.borderBottomLeftRadius = menu.style.borderBottomRightRadius = 4;
            menu.style.borderTopWidth = menu.style.borderBottomWidth =
                menu.style.borderLeftWidth = menu.style.borderRightWidth = 1;
            menu.style.borderTopColor = menu.style.borderBottomColor =
                menu.style.borderLeftColor = menu.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            menu.style.paddingTop = menu.style.paddingBottom = 4;
            menu.style.minWidth = 150;

            // "Add to Timeline" — only for timeline-level asset types, and only when the
            // current layer can accept this category
            if (category != AssetCategory.Enemies)
            {
                bool canAdd = CanAddToTimeline?.Invoke(category) ?? true;
                if (canAdd)
                {
                    string addLabel = category == AssetCategory.SpellCards
                        ? "Add to BossFight" : "Add to Timeline";
                    AddMenuItem(menu, addLabel, () =>
                    {
                        DismissContextMenu();
                        OnAssetAddRequested?.Invoke(category, entry.Id);
                    });
                }
                else
                {
                    string reason = category switch
                    {
                        AssetCategory.Patterns => "Navigate to a MidStage or SpellCard layer first",
                        AssetCategory.Waves => "Navigate to a MidStage layer first",
                        AssetCategory.SpellCards => "Navigate to a BossFight layer first",
                        _ => "Not available in current layer"
                    };
                    AddMenuItem(menu, $"Add to Timeline  ({reason})", null,
                        new Color(0.5f, 0.5f, 0.5f));
                }
            }
            AddMenuItem(menu, "Rename...", () =>
            {
                DismissContextMenu();
                ShowRenameDialog(category, entry);
            });
            AddMenuSeparator(menu);
            AddMenuItem(menu, "Delete", () =>
            {
                DismissContextMenu();
                ShowDeleteConfirmation(category, entry);
            }, new Color(1f, 0.5f, 0.5f));

            dismiss.Add(menu);

            _contextMenu = dismiss;
            Root.panel.visualTree.Add(dismiss);
        }

        private void DismissContextMenu()
        {
            _contextMenu?.RemoveFromHierarchy();
            _contextMenu = null;
        }

        private static void AddMenuItem(VisualElement menu, string text, Action onClick,
            Color? textColor = null)
        {
            var item = new VisualElement();
            item.style.height = 24;
            item.style.paddingLeft = 12;
            item.style.paddingRight = 12;
            item.style.justifyContent = Justify.Center;

            var label = new Label(text);
            label.style.color = textColor ?? new Color(0.85f, 0.85f, 0.85f);
            label.style.fontSize = 11;
            item.Add(label);

            if (onClick != null)
            {
                item.RegisterCallback<MouseEnterEvent>(_ =>
                    item.style.backgroundColor = new Color(0.3f, 0.4f, 0.6f, 0.7f));
                item.RegisterCallback<MouseLeaveEvent>(_ =>
                    item.style.backgroundColor = StyleKeyword.Null);
                item.RegisterCallback<PointerDownEvent>(evt =>
                {
                    evt.StopPropagation();
                    onClick.Invoke();
                });
            }
            else
            {
                label.style.opacity = 0.5f;
            }

            menu.Add(item);
        }

        private static void AddMenuSeparator(VisualElement menu)
        {
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.marginTop = sep.style.marginBottom = 2;
            sep.style.marginLeft = sep.style.marginRight = 6;
            sep.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            menu.Add(sep);
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
                    name = "New Enemy Type";
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
            OnCatalogChanged?.Invoke();
        }

        // ─── Delete ───

        private void ShowDeleteConfirmation(AssetCategory category, CatalogEntry entry)
        {
            if (_catalog == null) return;

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0, 0, 0, 0.5f);
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            panel.style.paddingTop = panel.style.paddingBottom = 12;
            panel.style.paddingLeft = panel.style.paddingRight = 16;
            panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
                panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 6;
            panel.style.width = 320;

            var categoryLabel = category == AssetCategory.Enemies ? "Enemy Type" : category.ToString();
            var title = new Label($"Delete {categoryLabel}?");
            title.style.fontSize = 14;
            title.style.color = new Color(0.95f, 0.7f, 0.7f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            var displayName = string.IsNullOrEmpty(entry.Name) ? entry.Id : entry.Name;
            var warning = category == AssetCategory.Enemies
                ? $"Are you sure you want to delete \"{displayName}\" ({entry.Id})?\nThis is a template asset. Waves referencing this EnemyType will break."
                : $"Are you sure you want to delete \"{displayName}\" ({entry.Id})?\nThis will remove the YAML file from disk.";
            var msg = new Label(warning);
            msg.style.color = new Color(0.85f, 0.85f, 0.85f);
            msg.style.fontSize = 11;
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.marginBottom = 12;
            panel.Add(msg);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var deleteBtn = new Button(() =>
            {
                DeleteAsset(category, entry.Id);
                overlay.RemoveFromHierarchy();
            }) { text = "Delete" };
            deleteBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            deleteBtn.style.color = new Color(0.95f, 0.95f, 0.95f);

            var cancelBtn = new Button(() => overlay.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            cancelBtn.style.color = new Color(0.85f, 0.85f, 0.85f);
            cancelBtn.style.marginLeft = 8;

            btnRow.Add(deleteBtn);
            btnRow.Add(cancelBtn);
            panel.Add(btnRow);
            overlay.Add(panel);

            Root.panel.visualTree.Add(overlay);
        }

        private void DeleteAsset(AssetCategory category, string id)
        {
            if (_catalog == null) return;

            bool removed = category switch
            {
                AssetCategory.Patterns => _catalog.RemovePattern(id),
                AssetCategory.Enemies => _catalog.RemoveEnemyType(id),
                AssetCategory.Waves => _catalog.RemoveWave(id),
                AssetCategory.SpellCards => _catalog.RemoveSpellCard(id),
                _ => false
            };

            if (removed)
            {
                STGCatalog.Save(_catalog);
                Refresh(_catalog);
                OnCatalogChanged?.Invoke();
                Debug.Log($"[AssetLibrary] Deleted {category}: {id}");
            }
        }

        // ─── Rename ───

        private void ShowRenameDialog(AssetCategory category, CatalogEntry entry)
        {
            if (_catalog == null) return;

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0, 0, 0, 0.5f);
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            panel.style.paddingTop = panel.style.paddingBottom = 12;
            panel.style.paddingLeft = panel.style.paddingRight = 16;
            panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
                panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 6;
            panel.style.width = 300;

            var renameCategoryLabel = category == AssetCategory.Enemies ? "Enemy Type" : category.ToString();
            var title = new Label($"Rename {renameCategoryLabel}");
            title.style.fontSize = 14;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            var idLabel = new Label($"ID: {entry.Id}");
            idLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            idLabel.style.fontSize = 10;
            idLabel.style.marginBottom = 4;
            panel.Add(idLabel);

            var nameField = new TextField("New Name:") { value = entry.Name ?? "" };
            nameField.isDelayed = true;
            nameField.style.marginBottom = 8;
            panel.Add(nameField);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var confirmBtn = new Button(() =>
            {
                var newName = nameField.value?.Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    overlay.RemoveFromHierarchy();
                    return;
                }
                RenameAsset(category, entry.Id, newName);
                overlay.RemoveFromHierarchy();
            }) { text = "Rename" };
            confirmBtn.style.backgroundColor = new Color(0.2f, 0.4f, 0.5f);
            confirmBtn.style.color = new Color(0.9f, 0.9f, 0.9f);

            var cancelBtn = new Button(() => overlay.RemoveFromHierarchy()) { text = "Cancel" };
            cancelBtn.style.backgroundColor = new Color(0.3f, 0.2f, 0.2f);
            cancelBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            cancelBtn.style.marginLeft = 8;

            btnRow.Add(confirmBtn);
            btnRow.Add(cancelBtn);
            panel.Add(btnRow);
            overlay.Add(panel);

            ApplyTheme(panel);
            Root.panel.visualTree.Add(overlay);
        }

        private void RenameAsset(AssetCategory category, string id, string newName)
        {
            if (_catalog == null) return;

            switch (category)
            {
                case AssetCategory.Patterns:
                    _catalog.AddOrUpdatePattern(id, newName);
                    break;
                case AssetCategory.Enemies:
                    _catalog.AddOrUpdateEnemyType(id, newName);
                    break;
                case AssetCategory.Waves:
                    _catalog.AddOrUpdateWave(id, newName);
                    break;
                case AssetCategory.SpellCards:
                    _catalog.AddOrUpdateSpellCard(id, newName);
                    break;
                default:
                    return;
            }

            STGCatalog.Save(_catalog);
            Refresh(_catalog);
            OnCatalogChanged?.Invoke();
            Debug.Log($"[AssetLibrary] Renamed {category} '{id}' to '{newName}'");
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
