using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Domain.Documents;

public sealed class EditorWorkspace
{
    public EditorWorkspace()
    {
        Common = new ArchiveSlot(ConfigScope.Common);
        Scenario = new ArchiveSlot(ConfigScope.Scenario);
    }

    public ArchiveSlot Common { get; }
    public ArchiveSlot Scenario { get; }
    public ConfigScope ActiveScope { get; private set; } = ConfigScope.Common;
    public ArchiveSlot ActiveSlot => GetSlot(ActiveScope);
    public bool HasUnsavedChanges => Common.HasUnsavedChanges || Scenario.HasUnsavedChanges;

    public ArchiveSlot GetSlot(ConfigScope scope) => scope switch
    {
        ConfigScope.Common => Common,
        ConfigScope.Scenario => Scenario,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未知的数据作用域。")
    };

    public void SwitchScope(ConfigScope scope)
    {
        _ = GetSlot(scope);
        ActiveScope = scope;
    }
}
