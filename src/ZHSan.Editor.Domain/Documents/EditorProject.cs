namespace ZHSan.Editor.Domain.Documents;

public sealed class EditorProject
{
    public required string ArchivePath { get; init; }
    public required IReadOnlyList<ConfigDocument> Documents { get; init; }
    public ConfigDocument? ActiveDocument { get; set; }
}
