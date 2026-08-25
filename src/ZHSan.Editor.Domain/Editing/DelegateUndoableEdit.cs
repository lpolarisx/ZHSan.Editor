namespace ZHSan.Editor.Domain.Editing;

public sealed class DelegateUndoableEdit(
    string description,
    Action undo,
    Action redo) : IUndoableEdit
{
    public string Description { get; } = description;

    public void Undo() => undo();

    public void Redo() => redo();
}
