using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;
using STGEngine.Editor.Commands;

namespace STGEngine.Editor.UI
{
    /// <summary>
    /// Lightweight data binding for UI Toolkit Runtime.
    /// Binds BaseField controls to model properties with optional Command-based undo.
    /// Call RefreshUI() after Undo/Redo to sync model → UI.
    /// </summary>
    public class DataBinder : IDisposable
    {
        private readonly List<IBinding> _bindings = new();

        /// <summary>
        /// Bind a UI field to a property on the target object.
        /// If commandStack is provided, changes go through PropertyChangeCommand for undo support.
        /// </summary>
        public void Bind<T>(BaseField<T> field, object target, string propertyName,
            CommandStack commandStack = null)
        {
            var prop = target.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException(
                    $"Property '{propertyName}' not found on {target.GetType().Name}");

            // Initial sync: model → UI
            field.SetValueWithoutNotify((T)prop.GetValue(target));

            // UI → model (via Command if stack provided)
            EventCallback<ChangeEvent<T>> callback = evt =>
            {
                if (commandStack != null)
                {
                    var cmd = new PropertyChangeCommand<T>(
                        $"Change {propertyName}",
                        () => (T)prop.GetValue(target),
                        v => { prop.SetValue(target, v); },
                        evt.newValue);
                    commandStack.Execute(cmd);
                }
                else
                {
                    prop.SetValue(target, evt.newValue);
                }
            };

            field.RegisterCallback(callback);
            _bindings.Add(new Binding<T>(field, target, prop, callback));
        }

        /// <summary>
        /// Refresh all bound UI fields from model values.
        /// Call after Undo/Redo to sync model → UI without triggering change events.
        /// </summary>
        public void RefreshUI()
        {
            foreach (var b in _bindings) b.SyncToUI();
        }

        /// <summary>Unbind all and release callbacks.</summary>
        public void Dispose()
        {
            foreach (var b in _bindings) b.Unbind();
            _bindings.Clear();
        }

        private interface IBinding
        {
            void SyncToUI();
            void Unbind();
        }

        private class Binding<T> : IBinding
        {
            private readonly BaseField<T> _field;
            private readonly object _target;
            private readonly PropertyInfo _prop;
            private readonly EventCallback<ChangeEvent<T>> _callback;

            public Binding(BaseField<T> field, object target, PropertyInfo prop,
                EventCallback<ChangeEvent<T>> callback)
            {
                _field = field;
                _target = target;
                _prop = prop;
                _callback = callback;
            }

            public void SyncToUI()
            {
                _field.SetValueWithoutNotify((T)_prop.GetValue(_target));
            }

            public void Unbind()
            {
                _field.UnregisterCallback(_callback);
            }
        }
    }
}
