using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class TerritoryEditorViewModelTests
{
    private sealed record World(
        ConfigDocument Regions, ConfigDocument States, ConfigReferenceIndex References, ReflectionConfigMetadataProvider Metadata);

    // 司隶(0) and 豫州(1) form 中原; 冀州(2) is 河北. 洛阳 (5) is in 司隶, 许昌 (6) in 豫州, 邺 (7) in 冀州.
    private static World CreateWorld(string sili = "1 2", string yu = "0", string ji = "0")
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var architectures = new ConfigDocument
        {
            Definition = new ConfigDefinition("architectures", "建筑", "世界与地图", "Architectures.json",
                typeof(ArchitectureConfig), ConfigScope.Scenario),
            Items = [new ArchitectureConfig { Id = 5, Name = "洛阳", StateId = 0 },
                new ArchitectureConfig { Id = 6, Name = "许昌", StateId = 1 },
                new ArchitectureConfig { Id = 7, Name = "邺", StateId = 2 }]
        };
        var states = new ConfigDocument
        {
            Definition = new ConfigDefinition("states", "州域", "世界与地图", "States.json",
                typeof(StateConfig), ConfigScope.Scenario),
            Items = [new StateConfig { Id = 0, Name = "司隶", StateAdminID = 5, ContactStatesString = sili },
                new StateConfig { Id = 1, Name = "豫州", StateAdminID = 6, ContactStatesString = yu },
                new StateConfig { Id = 2, Name = "冀州", StateAdminID = 6, ContactStatesString = ji }]
        };
        var regions = new ConfigDocument
        {
            Definition = new ConfigDefinition("regions", "地区", "世界与地图", "Regions.json",
                typeof(RegionConfig), ConfigScope.Scenario),
            Items = [new RegionConfig { Id = 0, Name = "中原", RegionCoreID = 5, StatesListString = "0 1" },
                new RegionConfig { Id = 1, Name = "河北", RegionCoreID = 5, StatesListString = "2" }]
        };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([new EditorProject
        {
            Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents = [architectures, states, regions]
        }]);
        return new World(regions, states, references, metadata);
    }

    [Fact]
    public void Region_AddingAStateMovesItFromItsOldRegionInOneStep()
    {
        var world = CreateWorld();
        using var document = new ConfigDocumentViewModel(world.Regions, world.Metadata, _ => { },
            referenceIndex: world.References,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new RegionEditorProvider()]));
        var editor = Assert.IsType<RegionEditorViewModel>(document.SpecializedEditor?.Content);
        var central = (RegionConfig)world.Regions.Items[0]!;
        var north = (RegionConfig)world.Regions.Items[1]!;

        Assert.Equal(["名称", "核心城市"], editor.AllFields.Select(field => field.Label));
        Assert.Equal("5 洛阳", editor.AllFields.Single(field => field.Label == "核心城市").SelectedChoice?.Label);
        Assert.Equal("共 2 座：洛阳、许昌", editor.ArchitectureSummary);
        Assert.False(editor.BasicGroup.IsWarning);

        editor.States.Candidate = editor.States.Candidates.Single(option => option.Id == 2);
        editor.States.AddCommand.Execute(null);
        Assert.Equal("0 1 2", central.StatesListString);
        Assert.Equal(string.Empty, north.StatesListString);
        Assert.Equal("共 3 座：洛阳、许昌、邺", editor.ArchitectureSummary);

        document.UndoCommand.Execute(null);
        Assert.Equal("0 1", central.StatesListString);
        Assert.Equal("2", north.StatesListString);

        editor.SelectedRecord = editor.Records[1];
        Assert.True(editor.BasicGroup.IsWarning);
        Assert.Contains("核心城市不在所辖州内", editor.BasicGroup.Note);
    }

    [Fact]
    public void State_NeighboursStayMutualAndOneSidedPairsCanBeFixed()
    {
        // 冀州 lists 司隶 as a neighbour, but 司隶 does not list 冀州 back.
        var world = CreateWorld(sili: "1", yu: "0", ji: "0");
        using var document = new ConfigDocumentViewModel(world.States, world.Metadata, _ => { },
            referenceIndex: world.References,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new StateEditorProvider()]));
        var editor = Assert.IsType<StateEditorViewModel>(document.SpecializedEditor?.Content);
        var sili = (StateConfig)world.States.Items[0]!;
        var yu = (StateConfig)world.States.Items[1]!;
        var ji = (StateConfig)world.States.Items[2]!;

        Assert.Equal("0 中原", editor.RegionText);
        Assert.Equal("共 1 座：洛阳", editor.ArchitectureSummary);
        Assert.True(editor.HasOneSidedNeighbours);
        Assert.Contains("冀州 把本州列为相邻，但本州未列出对方", editor.NeighbourStatus);

        editor.MakeMutualCommand.Execute(null);
        Assert.Equal("1 2", sili.ContactStatesString);
        Assert.False(editor.HasOneSidedNeighbours);
        document.UndoCommand.Execute(null);
        Assert.Equal("1", sili.ContactStatesString);

        editor.Neighbours.Items.Single(item => item.Id == 1).RemoveCommand.Execute(null);
        Assert.Equal(string.Empty, sili.ContactStatesString);
        Assert.Equal(string.Empty, yu.ContactStatesString);
        document.UndoCommand.Execute(null);
        Assert.Equal("1", sili.ContactStatesString);
        Assert.Equal("0", yu.ContactStatesString);

        editor.SelectedRecord = editor.Records[1];
        editor.Neighbours.Candidate = editor.Neighbours.Candidates.Single(option => option.Id == 2);
        editor.Neighbours.AddCommand.Execute(null);
        Assert.Equal("0 2", yu.ContactStatesString);
        Assert.Equal("0 1", ji.ContactStatesString);

        editor.SelectedRecord = editor.Records[2];
        Assert.True(editor.BasicGroup.IsWarning);
        Assert.Contains("州治不在本州内", editor.BasicGroup.Note);
        Assert.Equal("1 河北", editor.RegionText);
    }
}
