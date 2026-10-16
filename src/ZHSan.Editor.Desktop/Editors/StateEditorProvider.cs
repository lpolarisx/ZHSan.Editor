using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class StateEditorProvider : IConfigEditorProvider
{
    public string Id => "state";
    public string DisplayName => "州域属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "states" && definition.ItemType == typeof(StateConfig);

    public object CreateViewModel(ConfigEditorContext context) => new StateEditorViewModel(context);
}
