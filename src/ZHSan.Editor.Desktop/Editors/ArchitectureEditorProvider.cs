using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class ArchitectureEditorProvider : IConfigEditorProvider
{
    public string Id => "architecture";
    public string DisplayName => "建筑属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "architectures" && definition.ItemType == typeof(ArchitectureConfig);

    public object CreateViewModel(ConfigEditorContext context) => new ArchitectureEditorViewModel(context);
}
