namespace ZHSan.Editor.Domain.Differences;

public sealed record ConfigRecordDifference(
    ConfigDifferenceKind Kind,
    int? ItemId,
    IReadOnlyList<ConfigDifferenceItem> CurrentItems,
    IReadOnlyList<ConfigDifferenceItem> IncomingItems,
    IReadOnlyList<ConfigPropertyDifference> PropertyDifferences,
    string? ConflictReason = null)
{
    public object? CurrentItem => CurrentItems.Count == 1 ? CurrentItems[0].Value : null;

    public object? IncomingItem => IncomingItems.Count == 1 ? IncomingItems[0].Value : null;
}
