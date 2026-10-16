using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class FactionEditorProvider : IConfigEditorProvider
{
    public string Id => "faction";
    public string DisplayName => "势力属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "factions" && definition.ItemType == typeof(FactionConfig);

    public object CreateViewModel(ConfigEditorContext context) => new FactionEditorViewModel(context);
}
