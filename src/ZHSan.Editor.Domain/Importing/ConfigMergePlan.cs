using ZHSan.Editor.Domain.Differences;

namespace ZHSan.Editor.Domain.Importing;

public sealed class ConfigMergePlan
{
    public ConfigMergePlan(
        string configKey,
        ConfigImportStrategy strategy,
        ConfigDifference difference,
        IReadOnlyList<object> mergedItems,
        bool hasChanges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(mergedItems);

        ConfigKey = configKey;
        Strategy = strategy;
        Difference = difference;
        MergedItems = mergedItems.ToArray();
        HasChanges = hasChanges;
    }

    public string ConfigKey { get; }

    public ConfigImportStrategy Strategy { get; }

    public ConfigDifference Difference { get; }

    public IReadOnlyList<object> MergedItems { get; }

    public bool HasChanges { get; }
}
