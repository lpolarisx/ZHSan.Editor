using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class TechniqueTreeEditorProvider : IConfigEditorProvider
{
    public string Id => "technique-tree";
    public string DisplayName => "科技树";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        string.Equals(definition.Key, "techniques", StringComparison.OrdinalIgnoreCase) &&
        definition.ItemType == typeof(TechniqueConfig);

    public object CreateViewModel(ConfigEditorContext context) =>
        new TechniqueTreeEditorViewModel(context);
}
