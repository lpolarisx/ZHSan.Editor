using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class FacilityKindLevelEditorProvider : IConfigEditorProvider
{
    public string Id => "facility-kind-levels";
    public string DisplayName => "设施种类与等级";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        string.Equals(definition.Key, "facility-kind-levels", StringComparison.OrdinalIgnoreCase) &&
        definition.ItemType == typeof(FacilityKindLevelConfig);

    public object CreateViewModel(ConfigEditorContext context) =>
        new FacilityKindLevelEditorViewModel(context);
}
