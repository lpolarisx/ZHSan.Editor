using ZHSan.Editor.Domain.Differences;

namespace ZHSan.Editor.Application.Importing;

public sealed class ConfigMergeConflictException : InvalidOperationException
{
    public ConfigMergeConflictException(
        ConfigDifference difference,
        IReadOnlyList<ConfigRecordDifference> conflicts)
        : base(CreateMessage(difference, conflicts))
    {
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(conflicts);

        Difference = difference;
        Conflicts = conflicts.ToArray();
    }

    public ConfigDifference Difference { get; }

    public IReadOnlyList<ConfigRecordDifference> Conflicts { get; }

    private static string CreateMessage(
        ConfigDifference difference,
        IReadOnlyList<ConfigRecordDifference> conflicts)
    {
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(conflicts);

        return $"配置 {difference.ConfigKey} 存在 {conflicts.Count} 个重复 ID 冲突，无法执行合并。";
    }
}
