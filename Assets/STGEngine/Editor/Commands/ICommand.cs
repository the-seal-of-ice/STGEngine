namespace STGEngine.Editor.Commands
{
    /// <summary>
    /// Base interface for undoable commands.
    /// All editing operations go through this for Undo/Redo support.
    /// </summary>
    public interface ICommand
    {
        string Description { get; }
        void Execute();
        void Undo();
    }
}
