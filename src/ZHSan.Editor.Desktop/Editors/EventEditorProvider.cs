using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class EventEditorProvider : IConfigEditorProvider
{
    public string Id => "event";
    public string DisplayName => "事件";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "events" && definition.ItemType == typeof(EventConfig);

    public object CreateViewModel(ConfigEditorContext context) => new EventEditorViewModel(context);
}
