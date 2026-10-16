using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class RegionEditorProvider : IConfigEditorProvider
{
    public string Id => "region";
    public string DisplayName => "地区属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "regions" && definition.ItemType == typeof(RegionConfig);

    public object CreateViewModel(ConfigEditorContext context) => new RegionEditorViewModel(context);
}
