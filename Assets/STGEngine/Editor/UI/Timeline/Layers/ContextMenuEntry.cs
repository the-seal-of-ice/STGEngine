using System;

namespace STGEngine.Editor.UI.Timeline.Layers
{
    /// <summary>
    /// A single entry in a timeline layer's right-click context menu.
    /// </summary>
    public struct ContextMenuEntry
    {
        /// <summary>Display text for the menu item.</summary>
        public string Label;

        /// <summary>Action to invoke when the menu item is clicked.</summary>
        public Action Action;

        /// <summary>Whether the menu item is interactable.</summary>
        public bool Enabled;

        public ContextMenuEntry(string label, Action action, bool enabled = true)
        {
            Label = label;
            Action = action;
            Enabled = enabled;
        }
    }
}
