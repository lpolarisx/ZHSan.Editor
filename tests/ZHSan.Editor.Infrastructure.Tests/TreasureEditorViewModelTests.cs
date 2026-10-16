using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class TreasureEditorViewModelTests
{
    private static ConfigDocument TreasureData(params TreasureConfig[] treasures) => new()
    {
        Definition = new ConfigDefinition("treasures", "宝物", "宝物", "Treasures.json",
            typeof(TreasureConfig), ConfigScope.Scenario),
        Items = [..treasures]
    };

    [Fact]
    public void Editor_SelectsTreasureEditsWithChineseLabelsAndUndoes()
    {
        var first = new TreasureConfig { Id = 5, Name = "青釭剑", Worth = 30, BelongedPersonIDString = -1, HidePlaceIDString = -1 };
        var second = new TreasureConfig { Id = 201, Name = "铜雀", Worth = 60, BelongedPersonIDString = -1, HidePlaceIDString = -1 };
        using var document = new ConfigDocumentViewModel(TreasureData(first, second),
            new ReflectionConfigMetadataProvider(), _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new TreasureEditorProvider()]));
        var editor = Assert.IsType<TreasureEditorViewModel>(document.SpecializedEditor?.Content);

        Assert.Equal("青釭剑", editor.SelectedTitle);
        Assert.Equal(
            ["名称", "图片序号", "价值", "宝物组", "耐久度", "所属人物", "隐藏地点", "出现年份", "已登场", "描述"],
            editor.AllFields.Select(field => field.Label));
        var worth = editor.AllFields.Single(field => field.Label == "价值");
        worth.NumericValue = 80;
        Assert.Equal(80, first.Worth);
        Assert.Equal("撤销 修改宝物价值", document.UndoLabel);
        document.UndoCommand.Execute(null);
        Assert.Equal(30, first.Worth);
        Assert.Equal(30, worth.NumericValue);

        editor.SearchText = "铜";
        Assert.Single(editor.FilteredTreasures);
        editor.SelectedTreasure = editor.FilteredTreasures[0];
        Assert.Same(document.Records[1], document.SelectedRecord);
        Assert.Equal(60, worth.NumericValue);
    }

    [Fact]
    public void NewTreasure_TakesNextIdWhenAddedCopiedOrPasted()
    {
        using var document = new ConfigDocumentViewModel(TreasureData(
                new TreasureConfig { Id = 300, Name = "玉玺" }, new TreasureConfig { Id = 5, Name = "青釭剑" }),
            new ReflectionConfigMetadataProvider(), _ => { }, editorProviderRegistry:
            new ConfigEditorProviderRegistry([new TreasureEditorProvider()]));

        document.AddCommand.Execute(null);
        var added = Assert.IsType<TreasureConfig>(document.Records[^1].Item);
        Assert.Equal(301, added.Id);
        Assert.Equal(-1, added.BelongedPersonIDString);
        Assert.Equal(-1, added.HidePlaceIDString);

        document.CopyCommand.Execute(null);
        Assert.Equal(302, Assert.IsType<TreasureConfig>(document.Records[^1].Item).Id);

        document.SelectedRecord = document.Records[0];
        document.CopyToClipboardCommand.Execute(null);
        document.PasteCommand.Execute(null);
        var pasted = Assert.IsType<TreasureConfig>(document.Records[^1].Item);
        Assert.Equal(303, pasted.Id);
        Assert.Equal("玉玺", pasted.Name);

        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        Assert.Equal(2, document.Records.Count);
    }

    [Fact]
    public void OwnerHidePlaceAndInfluences_UseReferencedRecords()
    {
        var treasure = new TreasureConfig
        {
            Id = 5, Name = "青釭剑", BelongedPersonIDString = -1, HidePlaceIDString = 3, InfluencesString = "523 "
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var influenceData = new ConfigDocument
        {
            Definition = new ConfigDefinition("influences", "影响", "影响", "Influences.json", typeof(InfluenceConfig)),
            Items = [new InfluenceConfig { Id = 523, Name = "武力+5" }, new InfluenceConfig { Id = 600, Name = "统率+5" }]
        };
        var personData = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 7, SurName = "关", GivenName = "羽" }]
        };
        var architectureData = new ConfigDocument
        {
            Definition = new ConfigDefinition("architectures", "建筑", "世界与地图", "Architectures.json",
                typeof(ArchitectureConfig), ConfigScope.Scenario),
            Items = [new ArchitectureConfig { Id = 3, Name = "许昌" }]
        };
        var treasureData = TreasureData(treasure);
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject { Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents = [influenceData] },
            new EditorProject
            {
                Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat",
                Documents = [personData, architectureData, treasureData]
            }
        ]);
        using var document = new ConfigDocumentViewModel(treasureData, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new TreasureEditorProvider()]));
        var editor = Assert.IsType<TreasureEditorViewModel>(document.SpecializedEditor?.Content);

        var owner = editor.AllFields.Single(field => field.Label == "所属人物");
        Assert.True(owner.IsChoice);
        Assert.Equal("-1 无", owner.SelectedChoice?.Label);
        owner.SelectedChoice = owner.ChoiceOptions.Single(option => option.Id == 7);
        Assert.Equal(7, treasure.BelongedPersonIDString);
        Assert.Equal("7 关羽", owner.SelectedChoice?.Label);
        Assert.Equal("3 许昌", editor.AllFields.Single(field => field.Label == "隐藏地点").SelectedChoice?.Label);

        Assert.Equal(["523 武力+5"], editor.Influences.Items.Select(item => item.Label));
        editor.Influences.Candidate = editor.Influences.Candidates.Single(option => option.Id == 600);
        editor.Influences.AddCommand.Execute(null);
        Assert.Equal("523 600", treasure.InfluencesString);
        editor.Influences.Items[0].RemoveCommand.Execute(null);
        Assert.Equal("600", treasure.InfluencesString);
    }

    [Fact]
    public void Picture_UsesPicIndexPrefersJpegAndRemembersFolder()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-treasures-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "9.png"), []);
            var treasure = new TreasureConfig { Id = 5, Name = "青釭剑", Pic = 2 };
            var store = TreasurePictureDirectoryStore.InMemory();
            using var document = new ConfigDocumentViewModel(TreasureData(treasure), new ReflectionConfigMetadataProvider(),
                _ => { }, editorProviderRegistry: new ConfigEditorProviderRegistry([new TreasureEditorProvider(store)]));
            var editor = Assert.IsType<TreasureEditorViewModel>(document.SpecializedEditor?.Content);

            Assert.Null(editor.PicturePath);
            Assert.Equal("点击选择宝物图片文件夹", editor.PictureHint);
            Assert.Equal("2.jpg", editor.PictureFileName);

            editor.PictureDirectory = directory;
            Assert.Equal(directory, store.Directory);
            Assert.Equal(Path.Combine(directory, "2.jpg"), editor.PicturePath);
            Assert.False(editor.HasPicture);
            Assert.Contains("未找到 2.jpg", editor.PictureHint);

            editor.AllFields.Single(field => field.Label == "图片序号").NumericValue = 9;
            Assert.Equal(Path.Combine(directory, "9.png"), editor.PicturePath);
            document.UndoCommand.Execute(null);
            Assert.Equal("2.jpg", editor.PictureFileName);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
