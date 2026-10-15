namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigDefinition(
    string Key,
    string DisplayName,
    string Category,
    string EntryName,
    Type ItemType,
    ConfigScope Scope = ConfigScope.Common)
{
    public ConfigAddress Address => new(Scope, Key);

    /// <summary>
    /// False for documents that are loaded, saved and validated as usual but edited only through a
    /// specialised editor of another document, so they are left out of the navigation pane.
    /// </summary>
    public bool ShowInNavigation { get; init; } = true;

    /// <summary>
    /// For a document hidden from navigation: the key of the same-scope document whose record with
    /// the same ID edits it, used when jumping to a validation issue.
    /// </summary>
    public string? NavigationTargetKey { get; init; }
}
