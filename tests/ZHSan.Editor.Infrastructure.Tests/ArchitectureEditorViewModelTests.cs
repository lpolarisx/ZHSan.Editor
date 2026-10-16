using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ArchitectureEditorViewModelTests
{
    private static ConfigDefinition Scenario(string key) =>
        new GameDataConfigRegistry().Find(new ConfigAddress(ConfigScope.Scenario, key))!;

    // 洛阳 (5) holds 曹操 (6) and 许褚 (9, unaffiliated); 许昌 (6) holds 刘备 (16). 洛阳 links to 许昌 by land,
    // 许昌 does not link back. 曹操's faction owns 洛阳.
    private static (ConfigDocumentViewModel Document, ArchitectureEditorViewModel Editor, ArchitectureConfig Luoyang,
        ArchitectureConfig Xuchang) Create()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var luoyang = new ArchitectureConfig
        {
            Id = 5, Name = "洛阳", StateId = 0, KindId = 1, MayorId = 16, PersonsString = "6",
            NoFactionPersonsString = "9", AILandLinksString = "6"
        };
        var xuchang = new ArchitectureConfig { Id = 6, Name = "许昌", StateId = 0, KindId = 1, MayorId = -1, PersonsString = "16" };
        var architectures = new ConfigDocument
        {
            Definition = Scenario("architectures"),
            Items = [luoyang, xuchang]
        };
        var scenario = new EditorProject
        {
            Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents =
            [
                architectures,
                new ConfigDocument
                {
                    Definition = Scenario("persons"),
                    Items = [new PersonConfig { Id = 6, SurName = "曹", GivenName = "操" },
                        new PersonConfig { Id = 9, SurName = "许", GivenName = "褚" },
                        new PersonConfig { Id = 16, SurName = "刘", GivenName = "备" }]
                },
                new ConfigDocument
                {
                    Definition = Scenario("states"),
                    Items = [new StateConfig { Id = 0, Name = "司隶" }]
                },
                new ConfigDocument
                {
                    Definition = Scenario("regions"),
                    Items = [new RegionConfig { Id = 0, Name = "中原", StatesListString = "0" }]
                },
                new ConfigDocument
                {
                    Definition = Scenario("factions"),
                    Items = [new FactionConfig { Id = 1, Name = "曹操", ArchitecturesString = "5" }]
                }
            ]
        };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([scenario]);
        var document = new ConfigDocumentViewModel(architectures, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new ArchitectureEditorProvider()]));
        return (document, Assert.IsType<ArchitectureEditorViewModel>(document.SpecializedEditor?.Content), luoyang, xuchang);
    }

    [Fact]
    public void Architecture_ShowsChineseFieldsOwnershipAndWarnings()
    {
        var (document, editor, _, _) = Create();
        using var _ = document;

        Assert.Equal(["名称", "类型", "所属州", "太守", "名牌图", "原势力名", "战略要地", "皇帝所在"],
            editor.BasicGroup.Fields.Select(field => field.Label));
        Assert.Equal("0 司隶", editor.BasicGroup.Fields.Single(field => field.Label == "所属州").SelectedChoice?.Label);
        Assert.Equal("1 曹操", editor.FactionText);
        Assert.Equal("0 中原", editor.RegionText);
        Assert.True(editor.BasicGroup.IsWarning);
        Assert.Contains("太守不在本城", editor.BasicGroup.Note);
        Assert.Contains("陆路：许昌 未连回本城", editor.LinkStatus);

        editor.SelectedRecord = editor.Records[1];
        Assert.Equal("（无势力占领）", editor.FactionText);
        Assert.Contains("陆路：洛阳 连到本城，但本城未连回", editor.LinkStatus);
        Assert.False(editor.BasicGroup.IsWarning);
    }

    [Fact]
    public void Architecture_AddingAPersonMovesThemFromWhereTheyWereInOneStep()
    {
        var (document, editor, luoyang, xuchang) = Create();
        using var _ = document;
        var officers = editor.PeopleLists.Single(list => list.Label == "武将");

        Assert.Equal(["武将", "在野武将", "妃子"], editor.PeopleLists.Select(list => list.Label));
        Assert.Equal("16 刘备（现在 许昌·武将）", officers.Candidates.Single(option => option.Id == 16).Label);
        officers.Candidate = officers.Candidates.Single(option => option.Id == 16);
        officers.AddCommand.Execute(null);
        Assert.Equal("6 16", luoyang.PersonsString);
        Assert.Equal(string.Empty, xuchang.PersonsString);
        Assert.False(editor.BasicGroup.IsWarning);

        // Moving a person between this architecture's own lists works the same way.
        officers.Candidate = officers.Candidates.Single(option => option.Id == 9);
        officers.AddCommand.Execute(null);
        Assert.Equal("6 16 9", luoyang.PersonsString);
        Assert.Equal(string.Empty, luoyang.NoFactionPersonsString);

        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        Assert.Equal("6", luoyang.PersonsString);
        Assert.Equal("9", luoyang.NoFactionPersonsString);
        Assert.Equal("16", xuchang.PersonsString);
    }
}
