namespace ZHSan.Editor.Domain.Documents;

public sealed class EditorProject
{
    public required string ArchivePath { get; set; }
    public required IReadOnlyList<ConfigDocument> Documents { get; init; }
    public ConfigDocument? ActiveDocument { get; set; }
}
