using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Domain.Documents;

public sealed class ConfigDocument
{
    public required ConfigDefinition Definition { get; init; }
    public required IList<object> Items { get; init; }
    public ArchiveEntryState EntryState { get; set; } = ArchiveEntryState.Populated;
    public IList<int> NullRecordIndices { get; } = new List<int>();
    public bool IsDirty { get; set; }
}
