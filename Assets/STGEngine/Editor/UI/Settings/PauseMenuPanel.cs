using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace STGEngine.Editor.UI.Settings
{
    /// <summary>
    /// ESC pause menu overlay. Toggle with Escape key.
    /// Shows Resume / Settings buttons over a semi-transparent backdrop.
    /// </summary>
    public class PauseMenuPanel
    {
        public VisualElement Root { get; }
        public bool IsOpen { get; private set; }

        /// <summary>Fired when user clicks Resume or presses Escape again.</summary>
        public event Action OnResumeRequested;

        /// <summary>Fired when user clicks Settings.</summary>
        public event Action OnSettingsRequested;

        private readonly VisualElement _menuPanel;

        public PauseMenuPanel()
        {
            // Full-screen backdrop
            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = Root.style.right = Root.style.top = Root.style.bottom = 0;
            Root.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Position; // block clicks through

            // Center panel
            _menuPanel = new VisualElement();
            _menuPanel.style.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 0.95f);
            _menuPanel.style.paddingTop = _menuPanel.style.paddingBottom = 20;
            _menuPanel.style.paddingLeft = _menuPanel.style.paddingRight = 30;
            _menuPanel.style.borderTopLeftRadius = _menuPanel.style.borderTopRightRadius =
                _menuPanel.style.borderBottomLeftRadius = _menuPanel.style.borderBottomRightRadius = 8;
            _menuPanel.style.borderTopWidth = _menuPanel.style.borderBottomWidth =
                _menuPanel.style.borderLeftWidth = _menuPanel.style.borderRightWidth = 1;
            _menuPanel.style.borderTopColor = _menuPanel.style.borderBottomColor =
                _menuPanel.style.borderLeftColor = _menuPanel.style.borderRightColor =
                    new Color(0.4f, 0.4f, 0.5f);
            _menuPanel.style.minWidth = 220;
            _menuPanel.style.alignItems = Align.Stretch;

            // Title
            var title = new Label("PAUSED");
            title.style.fontSize = 18;
            title.style.color = new Color(0.9f, 0.9f, 0.9f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 16;
            _menuPanel.Add(title);

            // Resume button
            AddMenuButton("Resume", () => Close());

            // Settings button
            AddMenuButton("Settings", () => OnSettingsRequested?.Invoke());

            Root.Add(_menuPanel);

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
            Root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Root.style.display = DisplayStyle.None;
            OnResumeRequested?.Invoke();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        private void AddMenuButton(string text, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.height = 36;
            btn.style.fontSize = 14;
            btn.style.marginBottom = 6;
            btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.3f);
            btn.style.color = new Color(0.9f, 0.9f, 0.9f);
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 4;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor =
                    new Color(0.35f, 0.35f, 0.4f);
            _menuPanel.Add(btn);
        }
    }
}
