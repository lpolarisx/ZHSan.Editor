namespace ZHSan.Editor.Domain.Documents;

public sealed class EditorProject
{
    public required string ArchivePath { get; set; }
    public string? ArchiveRevision { get; set; }
    public required IReadOnlyList<ConfigDocument> Documents { get; init; }
    public ConfigDocument? ActiveDocument { get; set; }

    public bool HasUnsavedChanges => Documents.Any(document => document.IsDirty);
}
