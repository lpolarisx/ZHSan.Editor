namespace ZHSan.Editor.Domain.Editing;

public interface IUndoableEdit
{
    string Description { get; }

    void Undo();

    void Redo();
}
