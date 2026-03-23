using System;

namespace STGEngine.Editor.Commands
{
    /// <summary>
    /// Generic property change command. Captures getter/setter via lambda,
    /// eliminating the need for per-property Command classes.
    /// </summary>
    public class PropertyChangeCommand<T> : ICommand
    {
        private readonly Action<T> _setter;
        private readonly T _oldValue;
        private readonly T _newValue;

        public string Description { get; }

        public PropertyChangeCommand(
            string description,
            Func<T> getter,
            Action<T> setter,
            T newValue)
        {
            Description = description;
            _setter = setter;
            _oldValue = getter();
            _newValue = newValue;
        }

        public void Execute() => _setter(_newValue);
        public void Undo() => _setter(_oldValue);
    }
}
