using NUnit.Framework;
using STGEngine.Editor.Commands;

namespace STGEngine.Tests.EditMode.Editor
{
    public class CommandStackTests
    {
        private sealed class IntCommand : ICommand
        {
            private readonly System.Action _execute;
            private readonly System.Action _undo;

            public string Description => "int-command";

            public IntCommand(System.Action execute, System.Action undo)
            {
                _execute = execute;
                _undo = undo;
            }

            public void Execute() => _execute();
            public void Undo() => _undo();
        }

        [Test]
        public void ExecuteUndoRedo_UpdatesStacksAndValue()
        {
            var value = 0;
            var stack = new CommandStack();
            var command = new IntCommand(() => value = 10, () => value = 0);

            stack.Execute(command);
            Assert.That(value, Is.EqualTo(10));
            Assert.That(stack.CanUndo, Is.True);
            Assert.That(stack.UndoCount, Is.EqualTo(1));

            stack.Undo();
            Assert.That(value, Is.EqualTo(0));
            Assert.That(stack.CanRedo, Is.True);

            stack.Redo();
            Assert.That(value, Is.EqualTo(10));
        }
    }
}
