using System.Collections.Generic;

namespace STGEngine.Editor.Commands
{
    /// <summary>
    /// Generic list operation command: Add/Remove/Move elements.
    /// Covers modifier lists, event lists, wave lists, etc.
    /// </summary>
    public class ListCommand<T> : ICommand
    {
        public enum Op { Add, Remove, Move }

        private readonly IList<T> _list;
        private readonly Op _operation;
        private readonly T _item;
        private readonly int _index;
        private readonly int _targetIndex;

        public string Description { get; }

        public static ListCommand<T> Add(IList<T> list, T item, int index = -1,
            string desc = null)
        {
            var idx = index < 0 ? list.Count : index;
            return new ListCommand<T>(list, Op.Add, item, idx, -1,
                desc ?? $"Add {typeof(T).Name}");
        }

        public static ListCommand<T> Remove(IList<T> list, int index,
            string desc = null)
        {
            return new ListCommand<T>(list, Op.Remove, list[index], index, -1,
                desc ?? $"Remove {typeof(T).Name}");
        }

        public static ListCommand<T> Move(IList<T> list, int from, int to,
            string desc = null)
        {
            return new ListCommand<T>(list, Op.Move, list[from], from, to,
                desc ?? $"Move {typeof(T).Name}");
        }

        private ListCommand(IList<T> list, Op op, T item, int index,
            int targetIndex, string desc)
        {
            _list = list;
            _operation = op;
            _item = item;
            _index = index;
            _targetIndex = targetIndex;
            Description = desc;
        }

        public void Execute()
        {
            switch (_operation)
            {
                case Op.Add:    _list.Insert(_index, _item); break;
                case Op.Remove: _list.RemoveAt(_index); break;
                case Op.Move:
                    _list.RemoveAt(_index);
                    _list.Insert(_targetIndex, _item);
                    break;
            }
        }

        public void Undo()
        {
            switch (_operation)
            {
                case Op.Add:    _list.RemoveAt(_index); break;
                case Op.Remove: _list.Insert(_index, _item); break;
                case Op.Move:
                    _list.RemoveAt(_targetIndex);
                    _list.Insert(_index, _item);
                    break;
            }
        }
    }
}
