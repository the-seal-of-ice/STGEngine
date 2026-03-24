using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace STGEngine.Editor.UI.FileManager
{
    /// <summary>
    /// File picker popup modes.
    /// </summary>
    public enum FilePickerMode
    {
        Save,
        Load
    }

    /// <summary>
    /// A reusable file picker popup built with UI Toolkit Runtime.
    /// Shows a scrollable list of catalog entries with Save/Load/Delete actions.
    ///
    /// Save mode: click existing entry to overwrite, or type a new name and click Save.
    /// Load mode: click existing entry to load. No new-name input.
    /// </summary>
    public class FilePickerPopup
    {
        private readonly VisualElement _popup;
        private readonly VisualElement _dismiss;
        private readonly VisualElement _listContainer;
        private readonly TextField _nameField;
        private readonly Button _actionBtn;
        private readonly Button _deleteBtn;
        private readonly Label _statusLabel;

        private readonly FilePickerMode _mode;
        private readonly List<CatalogEntry> _entries;
        private CatalogEntry _selected;

        // Callbacks
        private readonly Action<CatalogEntry> _onSelect;
        private readonly Action<string> _onCreateNew;
        private readonly Action<CatalogEntry> _onDelete;
        private readonly Action _onCancel;

        private static readonly Color Bg = new(0.16f, 0.16f, 0.16f, 0.98f);
        private static readonly Color Border = new(0.4f, 0.4f, 0.4f);
        private static readonly Color Lt = new(0.85f, 0.85f, 0.85f);
        private static readonly Color Dim = new(0.6f, 0.6f, 0.6f);
        private static readonly Color BtnBg = new(0.25f, 0.25f, 0.25f);
        private static readonly Color SelectedBg = new(0.2f, 0.35f, 0.55f);
        private static readonly Color DangerBg = new(0.45f, 0.18f, 0.18f);

        /// <summary>
        /// Create a file picker popup.
        /// </summary>
        /// <param name="title">Popup title, e.g. "Save Pattern" or "Load Stage".</param>
        /// <param name="mode">Save or Load.</param>
        /// <param name="entries">Catalog entries to display.</param>
        /// <param name="onSelect">Called when user selects an existing entry (overwrite-save or load).</param>
        /// <param name="onCreateNew">Save mode only: called with the new name when user creates a new file.</param>
        /// <param name="onDelete">Called when user deletes an entry. Null to disable delete.</param>
        /// <param name="onCancel">Called when popup is dismissed.</param>
        public FilePickerPopup(
            string title,
            FilePickerMode mode,
            List<CatalogEntry> entries,
            Action<CatalogEntry> onSelect,
            Action<string> onCreateNew = null,
            Action<CatalogEntry> onDelete = null,
            Action onCancel = null)
        {
            _mode = mode;
            _entries = entries;
            _onSelect = onSelect;
            _onCreateNew = onCreateNew;
            _onDelete = onDelete;
            _onCancel = onCancel;

            // ── Dismiss layer ──
            _dismiss = new VisualElement();
            _dismiss.style.position = Position.Absolute;
            _dismiss.style.left = _dismiss.style.top = 0;
            _dismiss.style.right = _dismiss.style.bottom = 0;
            _dismiss.RegisterCallback<MouseDownEvent>(evt =>
            {
                Close();
                evt.StopPropagation();
            });

            // ── Popup container ──
            _popup = new VisualElement();
            _popup.style.position = Position.Absolute;
            _popup.style.left = Length.Percent(50);
            _popup.style.top = Length.Percent(50);
            _popup.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            _popup.style.width = 320;
            _popup.style.backgroundColor = Bg;
            SetBorder(_popup, Border, 1, 6);
            _popup.style.paddingTop = 10;
            _popup.style.paddingBottom = 10;
            _popup.style.paddingLeft = 12;
            _popup.style.paddingRight = 12;
            // Prevent clicks on popup from dismissing
            _popup.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());

            // ── Title ──
            var titleLabel = new Label(title);
            titleLabel.style.color = Lt;
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 8;
            _popup.Add(titleLabel);

            // ── File list (scrollable) ──
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 240;
            scroll.style.minHeight = 60;
            scroll.style.marginBottom = 8;
            scroll.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            SetBorder(scroll, new Color(0.3f, 0.3f, 0.3f), 1, 3);

            _listContainer = new VisualElement();
            scroll.Add(_listContainer);
            _popup.Add(scroll);

            // Build list items
            RebuildList();

            // ── Status label ──
            _statusLabel = new Label("");
            _statusLabel.style.color = Dim;
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.marginBottom = 4;
            _statusLabel.style.display = DisplayStyle.None;
            _popup.Add(_statusLabel);

            // ── New name row (Save mode only) ──
            if (_mode == FilePickerMode.Save)
            {
                var newRow = new VisualElement();
                newRow.style.flexDirection = FlexDirection.Row;
                newRow.style.marginBottom = 8;

                _nameField = new TextField();
                _nameField.style.flexGrow = 1;
                _nameField.style.color = Lt;
                _nameField.Q(className: "unity-base-field__input").style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                newRow.Add(_nameField);

                _actionBtn = new Button(OnSaveNewClicked) { text = "Save New" };
                _actionBtn.style.width = 72;
                _actionBtn.style.marginLeft = 4;
                StyleButton(_actionBtn);
                newRow.Add(_actionBtn);

                _popup.Add(newRow);
            }

            // ── Bottom bar ──
            var bottomBar = new VisualElement();
            bottomBar.style.flexDirection = FlexDirection.Row;
            bottomBar.style.justifyContent = Justify.FlexEnd;

            if (_onDelete != null)
            {
                _deleteBtn = new Button(OnDeleteClicked) { text = "Delete" };
                _deleteBtn.style.width = 60;
                _deleteBtn.style.backgroundColor = DangerBg;
                _deleteBtn.style.color = Lt;
                _deleteBtn.style.marginRight = 4;
                _deleteBtn.SetEnabled(false);
                bottomBar.Add(_deleteBtn);
            }

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bottomBar.Add(spacer);

            var cancelBtn = new Button(Close) { text = "Cancel" };
            cancelBtn.style.width = 60;
            StyleButton(cancelBtn);
            bottomBar.Add(cancelBtn);

            _popup.Add(bottomBar);
        }

        // ─── Public API ───

        /// <summary>Show the popup by attaching to the panel's visual tree.</summary>
        public void Show(VisualElement panelRoot)
        {
            var tree = panelRoot.panel?.visualTree;
            if (tree == null)
            {
                Debug.LogWarning("[FilePickerPopup] Cannot show: panel not attached.");
                return;
            }
            tree.Add(_dismiss);
            tree.Add(_popup);
        }

        public void Close()
        {
            _popup.RemoveFromHierarchy();
            _dismiss.RemoveFromHierarchy();
            _onCancel?.Invoke();
        }

        // ─── List Building ───

        private void RebuildList()
        {
            _listContainer.Clear();
            _selected = null;

            if (_entries.Count == 0)
            {
                var empty = new Label(_mode == FilePickerMode.Save
                    ? "No existing files. Type a name below."
                    : "No files found.");
                empty.style.color = Dim;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.paddingTop = 12;
                empty.style.paddingBottom = 12;
                _listContainer.Add(empty);
                return;
            }

            foreach (var entry in _entries)
            {
                var row = MakeEntryRow(entry);
                _listContainer.Add(row);
            }
        }

        private VisualElement MakeEntryRow(CatalogEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
            row.style.cursor = new UnityEngine.UIElements.Cursor();

            var nameLabel = new Label(entry.DisplayLabel);
            nameLabel.style.color = Lt;
            nameLabel.style.flexGrow = 1;
            nameLabel.style.fontSize = 12;
            row.Add(nameLabel);

            var fileLabel = new Label(entry.File);
            fileLabel.style.color = Dim;
            fileLabel.style.fontSize = 10;
            fileLabel.style.maxWidth = 140;
            fileLabel.style.overflow = Overflow.Hidden;
            row.Add(fileLabel);

            // Click handler
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();

                if (evt.clickCount == 2)
                {
                    // Double-click: immediate action
                    OnEntryConfirmed(entry);
                }
                else
                {
                    // Single click: select
                    SelectEntry(entry, row);
                }
            });

            return row;
        }

        private void SelectEntry(CatalogEntry entry, VisualElement row)
        {
            // Deselect previous
            _listContainer.Query<VisualElement>().ForEach(r =>
                r.style.backgroundColor = Color.clear);

            row.style.backgroundColor = SelectedBg;
            _selected = entry;

            if (_deleteBtn != null)
                _deleteBtn.SetEnabled(true);

            _statusLabel.text = $"Selected: {entry.Name}";
            _statusLabel.style.display = DisplayStyle.Flex;

            // In Save mode, populate name field with selected entry's name
            if (_mode == FilePickerMode.Save && _nameField != null)
                _nameField.SetValueWithoutNotify(entry.Name);
        }

        private void OnEntryConfirmed(CatalogEntry entry)
        {
            _popup.RemoveFromHierarchy();
            _dismiss.RemoveFromHierarchy();
            _onSelect?.Invoke(entry);
        }

        // ─── Actions ───

        private void OnSaveNewClicked()
        {
            // If an entry is selected and name field matches, treat as overwrite
            if (_selected != null)
            {
                _popup.RemoveFromHierarchy();
                _dismiss.RemoveFromHierarchy();
                _onSelect?.Invoke(_selected);
                return;
            }

            var name = _nameField?.value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                _statusLabel.text = "Please enter a file name.";
                _statusLabel.style.color = new Color(1f, 0.5f, 0.3f);
                _statusLabel.style.display = DisplayStyle.Flex;
                return;
            }

            _popup.RemoveFromHierarchy();
            _dismiss.RemoveFromHierarchy();
            _onCreateNew?.Invoke(name);
        }

        private void OnDeleteClicked()
        {
            if (_selected == null) return;

            var entry = _selected;
            _entries.Remove(entry);
            _selected = null;
            if (_deleteBtn != null)
                _deleteBtn.SetEnabled(false);

            RebuildList();

            _statusLabel.text = $"Deleted: {entry.Name}";
            _statusLabel.style.color = new Color(1f, 0.5f, 0.3f);
            _statusLabel.style.display = DisplayStyle.Flex;

            _onDelete?.Invoke(entry);
        }

        // ─── Styling Helpers ───

        private static void StyleButton(Button btn)
        {
            btn.style.color = Lt;
            btn.style.backgroundColor = BtnBg;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = Border;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 3;
        }

        private static void SetBorder(VisualElement el, Color color, float width, float radius)
        {
            el.style.borderTopWidth = el.style.borderBottomWidth =
                el.style.borderLeftWidth = el.style.borderRightWidth = width;
            el.style.borderTopColor = el.style.borderBottomColor =
                el.style.borderLeftColor = el.style.borderRightColor = color;
            el.style.borderTopLeftRadius = el.style.borderTopRightRadius =
                el.style.borderBottomLeftRadius = el.style.borderBottomRightRadius = radius;
        }
    }
}
