using System.Collections.Generic;
using System.Linq;

namespace STGEngine.Editor.Commands
{
    /// <summary>
    /// Composite command: bundles multiple commands into one atomic operation.
    /// Used for operations like "change emitter type" that modify multiple properties.
    /// </summary>
    public class CompositeCommand : ICommand
    {
        private readonly List<ICommand> _commands;
        public string Description { get; }

        public CompositeCommand(string description, params ICommand[] commands)
        {
            Description = description;
            _commands = commands.ToList();
        }

        public void Execute()
        {
            foreach (var cmd in _commands) cmd.Execute();
        }

        public void Undo()
        {
            // Reverse order for undo
            for (int i = _commands.Count - 1; i >= 0; i--)
                _commands[i].Undo();
        }
    }
}
