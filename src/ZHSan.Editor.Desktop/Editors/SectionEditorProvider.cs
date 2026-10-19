using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class SectionEditorProvider : IConfigEditorProvider
{
    public string Id => "section";
    public string DisplayName => "军团属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "sections" && definition.ItemType == typeof(SectionConfig);

    public object CreateViewModel(ConfigEditorContext context) => new SectionEditorViewModel(context);
}
