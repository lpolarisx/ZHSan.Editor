namespace ZHSan.Editor.Domain.Differences;

public sealed record ConfigPropertyDifference(
    string PropertyName,
    object? CurrentValue,
    object? IncomingValue);
