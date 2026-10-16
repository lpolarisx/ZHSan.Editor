using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class DiplomacyEditorProvider : IConfigEditorProvider
{
    public string Id => "diplomacy";
    public string DisplayName => "外交关系";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "diplomatic-relations" && definition.ItemType == typeof(DiplomaticRelationConfig);

    public object CreateViewModel(ConfigEditorContext context) => new DiplomacyEditorViewModel(context);
}
