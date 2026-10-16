using GameDatas;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class TreasureEditorProvider(TreasurePictureDirectoryStore? pictures = null) : IConfigEditorProvider
{
    public string Id => "treasure";
    public string DisplayName => "宝物属性";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "treasures" && definition.ItemType == typeof(TreasureConfig);

    public object CreateViewModel(ConfigEditorContext context) => new TreasureEditorViewModel(context, pictures);
}
