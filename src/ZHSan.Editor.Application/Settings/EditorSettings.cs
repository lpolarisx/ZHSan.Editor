using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Application.Settings;

public sealed class EditorSettings
{
    public const int DefaultRecentProjectLimit = 10;

    public bool ConfirmUnsavedChanges { get; set; } = true;
    public int RecentProjectLimit { get; set; } = DefaultRecentProjectLimit;
    public List<RecentProjectEntry> RecentProjects { get; set; } = [];
}

public sealed class RecentProjectEntry
{
    public required string ArchivePath { get; set; }
    public ConfigScope Scope { get; set; } = ConfigScope.Common;
    public DateTimeOffset LastOpenedAt { get; set; }
}
