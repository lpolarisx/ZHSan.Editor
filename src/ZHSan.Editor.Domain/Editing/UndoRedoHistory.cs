namespace ZHSan.Editor.Domain.Editing;

public sealed class UndoRedoHistory
{
    private readonly List<IUndoableEdit> _edits = [];
    private int _position;

    public event EventHandler? Changed;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _edits.Count;
    public int Position => _position;
    public string? UndoDescription => CanUndo ? _edits[_position - 1].Description : null;
    public string? RedoDescription => CanRedo ? _edits[_position].Description : null;

    public void Record(IUndoableEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_position < _edits.Count)
        {
            _edits.RemoveRange(_position, _edits.Count - _position);
        }

        _edits.Add(edit);
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _edits[_position - 1].Undo();
        _position--;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _edits[_position].Redo();
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
