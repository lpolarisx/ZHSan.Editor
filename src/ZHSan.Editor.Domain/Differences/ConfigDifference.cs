namespace ZHSan.Editor.Domain.Differences;

public sealed class ConfigDifference
{
    public ConfigDifference(string configKey, IReadOnlyList<ConfigRecordDifference> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);
        ArgumentNullException.ThrowIfNull(records);

        ConfigKey = configKey;
        Records = records.ToArray();
    }

    public string ConfigKey { get; }

    public IReadOnlyList<ConfigRecordDifference> Records { get; }

    public int AddedCount => Count(ConfigDifferenceKind.Added);

    public int ModifiedCount => Count(ConfigDifferenceKind.Modified);

    public int DeletedCount => Count(ConfigDifferenceKind.Deleted);

    public int ConflictCount => Count(ConfigDifferenceKind.Conflict);

    public bool HasChanges => Records.Count > 0;

    public bool HasConflicts => ConflictCount > 0;

    private int Count(ConfigDifferenceKind kind) => Records.Count(record => record.Kind == kind);
}
