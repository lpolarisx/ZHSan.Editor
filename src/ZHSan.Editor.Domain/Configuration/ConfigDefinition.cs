namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigDefinition(
    string Key,
    string DisplayName,
    string Category,
    string EntryName,
    Type ItemType);
