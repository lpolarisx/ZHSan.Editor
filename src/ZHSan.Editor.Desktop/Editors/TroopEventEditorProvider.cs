using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class TroopEventEditorProvider : IConfigEditorProvider
{
    public string Id => "troop-event";
    public string DisplayName => "部队事件";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "troop-events" && definition.ItemType == typeof(TroopEventConfig);

    public object CreateViewModel(ConfigEditorContext context) => new TroopEventEditorViewModel(context);
}
