namespace ZHSan.Editor.Desktop.Services;

public interface IUnsavedChangesPrompt
{
    Task<UnsavedChangesChoice> ShowAsync(
        string projectName,
        IReadOnlyList<string> dirtyDocumentNames);
}

public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    Discard
}
