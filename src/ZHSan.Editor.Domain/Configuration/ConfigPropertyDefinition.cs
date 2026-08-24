namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigPropertyDefinition(
    string Name,
    string DisplayName,
    Type PropertyType,
    bool CanWrite,
    int Order);
