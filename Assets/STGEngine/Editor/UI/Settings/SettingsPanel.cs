using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.DataModel;

namespace STGEngine.Editor.UI.Settings
{
    /// <summary>
    /// Settings configuration panel. Opened from PauseMenuPanel.
    /// Displays [GAMEPLAY] and [EDITOR] settings in separate sections.
    /// Changes are applied immediately and persisted to disk.
    /// </summary>
    public class SettingsPanel
    {
        public VisualElement Root { get; }
        public bool IsOpen { get; private set; }

        /// <summary>Fired when user clicks Back.</summary>
        public event Action OnBackRequested;

        private readonly VisualElement _contentArea;

        public SettingsPanel()
        {
            // Full-screen backdrop
            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = Root.style.right = Root.style.top = Root.style.bottom = 0;
            Root.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Position;

            // Center panel
            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 0.95f);
            panel.style.paddingTop = panel.style.paddingBottom = 16;
            panel.style.paddingLeft = panel.style.paddingRight = 20;
            panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
                panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 8;
            panel.style.borderTopWidth = panel.style.borderBottomWidth =
                panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = panel.style.borderBottomColor =
                panel.style.borderLeftColor = panel.style.borderRightColor =
                    new Color(0.4f, 0.4f, 0.5f);
            panel.style.minWidth = 340;
            panel.style.maxWidth = 450;
            panel.style.maxHeight = Length.Percent(80);
            panel.style.alignItems = Align.Stretch;

            // Title
            var title = new Label("Settings");
            title.style.fontSize = 18;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 12;
            panel.Add(title);

            // Scrollable content
            _contentArea = new VisualElement();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.Add(_contentArea);
            panel.Add(scroll);

            // Back button
            var backBtn = new Button(() => Close()) { text = "Back" };
            backBtn.style.height = 32;
            backBtn.style.marginTop = 12;
            backBtn.style.backgroundColor = new Color(0.25f, 0.22f, 0.28f);
            backBtn.style.color = new Color(0.9f, 0.9f, 0.9f);
            backBtn.style.borderTopLeftRadius = backBtn.style.borderTopRightRadius =
                backBtn.style.borderBottomLeftRadius = backBtn.style.borderBottomRightRadius = 4;
            backBtn.style.borderTopWidth = backBtn.style.borderBottomWidth =
                backBtn.style.borderLeftWidth = backBtn.style.borderRightWidth = 1;
            backBtn.style.borderTopColor = backBtn.style.borderBottomColor =
                backBtn.style.borderLeftColor = backBtn.style.borderRightColor =
                    new Color(0.35f, 0.35f, 0.4f);
            panel.Add(backBtn);

            Root.Add(panel);

            // Click backdrop to close
            Root.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.target == Root)
                {
                    Close();
                    e.StopPropagation();
                }
            });

            Timeline.TimelineEditorView.RegisterThemeOverride(Root);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            RebuildContent();
            Root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Root.style.display = DisplayStyle.None;
            OnBackRequested?.Invoke();
        }

        // ── Content Builder ──

        private void RebuildContent()
        {
            _contentArea.Clear();

            // ════════════════════════════════════════
            // [GAMEPLAY] section — affects actual play
            // ════════════════════════════════════════
            AddSectionHeader("Gameplay", true);

            var gameplay = EngineSettingsManager.Gameplay;

            // Simulation Tick Rate
            var tickRateChoices = new List<string> { "60", "120", "240", "480" };
            int currentIdx = tickRateChoices.IndexOf(gameplay.SimulationTickRate.ToString());
            if (currentIdx < 0) currentIdx = 2; // default 240

            var tickRateRow = AddSettingRow(
                "Simulation Tick Rate",
                "Affects Homing/Bounce/Split precision and collision accuracy. " +
                "Must match between editor and runtime.",
                true);

            var tickDropdown = new DropdownField(tickRateChoices, currentIdx);
            tickDropdown.style.width = 100;
            tickDropdown.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out int rate))
                {
                    EngineSettingsManager.ApplyGameplay(g => g.SimulationTickRate = rate);
                }
            });
            tickRateRow.Add(tickDropdown);

            // Max Concurrent SE
            var seChoices = new List<string> { "8", "16", "24", "32", "48", "64" };
            int seIdx = seChoices.IndexOf(gameplay.MaxConcurrentSe.ToString());
            if (seIdx < 0) seIdx = 1; // default 16

            var seRow = AddSettingRow(
                "Max Concurrent SE",
                "Maximum simultaneous sound effects. Higher = richer audio during " +
                "dense patterns, but more CPU. Oldest SE is stolen when limit is reached.",
                true);

            var seDropdown = new DropdownField(seChoices, seIdx);
            seDropdown.style.width = 100;
            seDropdown.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out int count))
                {
                    EngineSettingsManager.ApplyGameplay(g => g.MaxConcurrentSe = count);
                }
            });
            seRow.Add(seDropdown);

            // ════════════════════════════════════════
            // [EDITOR] section — editor only
            // ════════════════════════════════════════
            AddSectionHeader("Editor", false);

            var editor = EngineSettingsManager.Editor;

            // Preview FPS Limit — integer input, 0 = unlimited
            var fpsRow = AddSettingRow(
                "Preview FPS Limit",
                "Caps editor render frame rate. 0 = unlimited. " +
                "Does not affect simulation tick rate (logic and render are decoupled).",
                false);

            var fpsField = new IntegerField { value = editor.PreviewFpsLimit };
            fpsField.isDelayed = true; // commits on Enter or focus loss
            fpsField.style.width = 70;
            fpsField.RegisterValueChangedCallback(e =>
            {
                int val = Mathf.Max(0, e.newValue);
                EngineSettingsManager.ApplyEditor(ed => ed.PreviewFpsLimit = val);
            });
            fpsRow.Add(fpsField);

            // Previewer Pool Size
            var poolRow = AddSettingRow(
                "Previewer Pool Size",
                "Number of PatternPreviewer instances pre-allocated in the object pool. " +
                "Each active pattern on the timeline occupies one previewer. " +
                "If all are in use, new ones are created dynamically (slower). " +
                "Increase for stages with many simultaneous patterns. Range: 2-32.",
                false);

            var poolField = new IntegerField { value = editor.PreviewerPoolSize };
            poolField.isDelayed = true;
            poolField.style.width = 70;
            poolField.RegisterValueChangedCallback(e =>
            {
                int val = Mathf.Clamp(e.newValue, 2, 32);
                EngineSettingsManager.ApplyEditor(ed => ed.PreviewerPoolSize = val);
            });
            poolRow.Add(poolField);
        }

        // ── UI Helpers ──

        private void AddSectionHeader(string text, bool isGameplay)
        {
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.35f);
            separator.style.marginTop = 10;
            separator.style.marginBottom = 6;
            _contentArea.Add(separator);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            // Icon: gear for gameplay, wrench for editor
            var icon = new Label(isGameplay ? "\u2699" : "\u270E");
            icon.style.fontSize = 14;
            icon.style.marginRight = 6;
            icon.style.color = isGameplay
                ? new Color(1f, 0.8f, 0.3f)   // gold for gameplay
                : new Color(0.5f, 0.7f, 1f);  // blue for editor
            row.Add(icon);

            var label = new Label(text);
            label.style.fontSize = 14;
            label.style.color = new Color(0.85f, 0.85f, 0.85f);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            // Tag
            var tag = new Label(isGameplay ? "Affects gameplay" : "Editor only");
            tag.style.fontSize = 10;
            tag.style.color = isGameplay
                ? new Color(1f, 0.7f, 0.3f, 0.7f)
                : new Color(0.5f, 0.6f, 0.8f, 0.7f);
            tag.style.marginLeft = 8;
            row.Add(tag);

            _contentArea.Add(row);
        }

        private VisualElement AddSettingRow(string label, string description, bool isGameplay)
        {
            var container = new VisualElement();
            container.style.marginBottom = 10;
            container.style.paddingLeft = 4;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;

            var labelEl = new Label(label);
            labelEl.style.color = new Color(0.8f, 0.8f, 0.8f);
            labelEl.style.flexGrow = 1;
            row.Add(labelEl);

            container.Add(row);

            // Description text below the control row
            if (!string.IsNullOrEmpty(description))
            {
                var desc = new Label(description);
                desc.style.fontSize = 10;
                desc.style.color = new Color(0.55f, 0.55f, 0.6f);
                desc.style.whiteSpace = WhiteSpace.Normal;
                desc.style.marginTop = 2;
                desc.style.paddingRight = 4;
                container.Add(desc);
            }

            _contentArea.Add(container);
            return row;
        }
    }
}
