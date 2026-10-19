using GameDatas;
using GameEnums;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class SectionEditorViewModelTests
{
    private static ConfigDefinition Find(ConfigScope scope, string key) =>
        new GameDataConfigRegistry().Find(new ConfigAddress(scope, key))!;

    [Fact]
    public void Registry_ListsSectionsAsLegionsAndHidesRuntimeLegions()
    {
        var registry = new GameDataConfigRegistry();

        Assert.Equal("军团", Find(ConfigScope.Scenario, "sections").DisplayName);
        Assert.False(Find(ConfigScope.Scenario, "legions").ShowInNavigation);
        Assert.Equal("Legions.json", Find(ConfigScope.Scenario, "legions").EntryName);
        Assert.Single(registry.GetDefinitions(ConfigScope.Scenario),
            definition => definition.DisplayName == "军团" && definition.ShowInNavigation);
    }

    [Fact]
    public void Section_EditsCommissionTargetsAndArchitectures()
    {
        // 曹操 holds 陈留 (59) and 许昌 (60) in section 0, 刘备 holds 小沛 (61) in section 1.
        var west = new SectionConfig
        {
            Id = 0, Name = "陈留军区", AIDetailIDString = 2, OrientationFactionID = -1, OrientationSectionID = -1,
            OrientationStateID = -1, OrientationArchitectureID = -1, ArchitecturesString = "59 60 "
        };
        var east = new SectionConfig
        {
            Id = 1, Name = "小沛军区", AIDetailIDString = 0, OrientationFactionID = -1, OrientationSectionID = -1,
            OrientationStateID = -1, OrientationArchitectureID = -1, ArchitecturesString = "61 "
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var sections = new ConfigDocument { Definition = Find(ConfigScope.Scenario, "sections"), Items = [west, east] };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject
            {
                Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents =
                [
                    new ConfigDocument
                    {
                        Definition = Find(ConfigScope.Common, "section-ai-details"),
                        Items =
                        [
                            new SectionAIDetailConfig { Id = 0, Name = "全权委任", OrientationKind = SectionOrientationKind.None },
                            new SectionAIDetailConfig
                            {
                                Id = 2, Name = "输送", Description = "向目标军区输送物资",
                                OrientationKind = SectionOrientationKind.Section
                            }
                        ]
                    }
                ]
            },
            new EditorProject
            {
                Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents =
                [
                    sections,
                    new ConfigDocument
                    {
                        Definition = Find(ConfigScope.Scenario, "architectures"),
                        Items = [new ArchitectureConfig { Id = 59, Name = "陈留" }, new ArchitectureConfig { Id = 60, Name = "许昌" },
                            new ArchitectureConfig { Id = 61, Name = "小沛" }]
                    },
                    new ConfigDocument
                    {
                        Definition = Find(ConfigScope.Scenario, "factions"),
                        Items = [new FactionConfig { Id = 1, Name = "曹操", ArchitecturesString = "59 60 ", SectionsString = "0 " },
                            new FactionConfig { Id = 2, Name = "刘备", ArchitecturesString = "61 ", SectionsString = "1 " }]
                    }
                ]
            }
        ]);
        using var document = new ConfigDocumentViewModel(sections, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new SectionEditorProvider()]));
        var editor = Assert.IsType<SectionEditorViewModel>(document.SpecializedEditor?.Content);

        Assert.Equal(["名称", "委任类型"], editor.BasicGroup.Fields.Select(field => field.Label));
        Assert.Equal("2 输送", editor.BasicGroup.Fields[1].SelectedChoice?.Label);
        Assert.Equal("向目标军区输送物资", editor.BasicGroup.Note);
        Assert.Equal("委任类型“输送”需要设置目标军团", editor.OrientationGroup.Note);
        Assert.True(editor.OrientationGroup.IsWarning);
        Assert.Equal("1 曹操", editor.FactionText);
        Assert.Equal(["59 陈留", "60 许昌"], editor.Architectures.Items.Select(item => item.Label));

        var target = editor.OrientationGroup.Fields.Single(field => field.Label == "目标军团");
        target.SelectedChoice = target.ChoiceOptions.Single(option => option.Id == 1);
        Assert.Equal(1, west.OrientationSectionID);
        Assert.Equal("委任类型“输送”只使用目标军团", editor.OrientationGroup.Note);

        // Taking 小沛 moves it out of 刘备's section in one step and flags it as outside 曹操's architectures.
        editor.Architectures.Candidate = editor.Architectures.Candidates.Single(option => option.Id == 61);
        editor.Architectures.AddCommand.Execute(null);
        Assert.Equal("59 60 61", west.ArchitecturesString.Trim());
        Assert.Equal(string.Empty, east.ArchitecturesString.Trim());
        Assert.Equal("以下建筑不属于曹操：61 小沛", editor.ArchitectureNote);
        document.UndoCommand.Execute(null);
        Assert.Equal("61", east.ArchitecturesString.Trim());
        Assert.Equal("59 60", west.ArchitecturesString.Trim());

        var commission = editor.BasicGroup.Fields[1];
        commission.SelectedChoice = commission.ChoiceOptions.Single(option => option.Id == 0);
        Assert.Equal("委任类型“全权委任”不需要目标，以下设置不起作用", editor.OrientationGroup.Note);
    }
}
