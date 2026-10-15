using GameDatas;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class PersonBasicEditorProvider(PersonPortraitDirectoryStore? portraits = null) : IConfigEditorProvider
{
    public string Id => "person-basic";
    public string DisplayName => "人物基本属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "persons" && definition.ItemType == typeof(PersonConfig);

    public object CreateViewModel(ConfigEditorContext context) => new PersonBasicEditorViewModel(context, portraits);
}
