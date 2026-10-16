using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class FactionEditorViewModelTests
{
    private static ConfigDocument FactionData(params FactionConfig[] factions) => new()
    {
        Definition = new ConfigDefinition("factions", "势力", "势力与组织", "Factions.json",
            typeof(FactionConfig), ConfigScope.Scenario),
        Items = [..factions]
    };

    [Fact]
    public void Editor_UsesChineseLabelsReferenceChoicesAndChipLists()
    {
        var faction = new FactionConfig
        {
            Id = 1, Name = "曹操", LeaderID = 343, PrinceID = -1, CapitalID = 59, ColorIndex = 1,
            ArchitecturesString = "59 ", AvailableTechniquesString = "200 999 "
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var techniqueData = new ConfigDocument
        {
            Definition = new ConfigDefinition("techniques", "技巧", "技巧", "Techniques.json", typeof(TechniqueConfig)),
            Items = [new TechniqueConfig { Id = 200, Name = "屯田", Kind = 1 }, new TechniqueConfig { Id = 0, Name = "强弩", Kind = 0 },
                new TechniqueConfig { Id = 295, Name = "水军", Kind = 1, DisplayRow = 1 }]
        };
        var colorData = new ConfigDocument
        {
            Definition = new GameDataConfigRegistry().Find("colors")!,
            Items = [new FactionColorConfig { Id = 0, R = 160 }, new FactionColorConfig { Id = 1, G = 128 }]
        };
        var personData = new ConfigDocument
        {
            Definition = new ConfigDefinition("persons", "人物", "人物", "Persons.json",
                typeof(PersonConfig), ConfigScope.Scenario),
            Items = [new PersonConfig { Id = 343, SurName = "曹", GivenName = "操" },
                new PersonConfig { Id = 344, SurName = "曹", GivenName = "丕" }]
        };
        var architectureData = new ConfigDocument
        {
            Definition = new ConfigDefinition("architectures", "建筑", "世界与地图", "Architectures.json",
                typeof(ArchitectureConfig), ConfigScope.Scenario),
            Items = [new ArchitectureConfig { Id = 59, Name = "陈留" }, new ArchitectureConfig { Id = 60, Name = "许昌" }]
        };
        var factionData = FactionData(faction);
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject { Scope = ConfigScope.Common, ArchivePath = "common.dat", Documents = [colorData, techniqueData] },
            new EditorProject
            {
                Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat",
                Documents = [personData, architectureData, factionData]
            }
        ]);
        using var document = new ConfigDocumentViewModel(factionData, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new FactionEditorProvider()]));
        var editor = Assert.IsType<FactionEditorViewModel>(document.SpecializedEditor?.Content);

        Assert.Equal("曹操", editor.SelectedTitle);
        Assert.Equal(["名称", "君主", "太子", "首都", "官爵", "朝廷贡献", "声望"],
            editor.BasicGroup.Fields.Select(field => field.Label));
        Assert.DoesNotContain(editor.AllFields, field => field.Label.Any(char.IsAsciiLetterLower));
        Assert.Equal("343 曹操", editor.AllFields.Single(field => field.Label == "君主").SelectedChoice?.Label);
        var prince = editor.AllFields.Single(field => field.Label == "太子");
        Assert.Equal("-1 无", prince.SelectedChoice?.Label);
        prince.SelectedChoice = prince.ChoiceOptions.Single(option => option.Id == 344);
        Assert.Equal(344, faction.PrinceID);
        Assert.Equal("撤销 修改势力太子", document.UndoLabel);

        Assert.Equal(["名称", "君主", "太子", "首都", "官爵", "朝廷贡献", "声望", "异族", "玩家不可选", "自动拒绝赎回",
            "已灭亡", "技巧点数", "研究中", "剩余天数", "计划研究"], editor.AllFields.Select(field => field.Label));
        Assert.Equal(1, editor.SelectedColor?.Id);
        editor.SelectedColor = editor.ColorOptions.Single(option => option.Id == 0);
        Assert.Equal(0, faction.ColorIndex);

        var architectures = editor.TerritoryLists.Single(list => list.Label == "领有建筑");
        Assert.Equal(["59 陈留"], architectures.Items.Select(item => item.Label));
        architectures.Candidate = architectures.Candidates.Single(option => option.Id == 60);
        architectures.AddCommand.Execute(null);
        Assert.Equal("59 60", faction.ArchitecturesString);

        Assert.Equal(["强弩", "屯田", "水军"], editor.TechniqueButtons.Select(button => button.Name));
        Assert.Equal([false, true, false], editor.TechniqueButtons.Select(button => button.IsSelected));
        Assert.Contains("已研究 1 / 3", editor.TechniqueStatus);
        Assert.Contains("999", editor.TechniqueStatus);
        editor.TechniqueButtons[2].IsSelected = true;
        Assert.Equal("200 999 295", faction.AvailableTechniquesString);
        editor.TechniqueButtons[1].IsSelected = false;
        Assert.Equal("999 295", faction.AvailableTechniquesString);

        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        Assert.Equal("200 999 ", faction.AvailableTechniquesString);
        Assert.True(editor.TechniqueButtons[1].IsSelected);
        document.UndoCommand.Execute(null);
        document.UndoCommand.Execute(null);
        Assert.Equal("59 ", faction.ArchitecturesString);
        Assert.Equal(1, faction.ColorIndex);
        Assert.Equal(1, editor.SelectedColor?.Id);
    }

    [Fact]
    public void DeleteFaction_RemovesItsDiplomaticRelationsAndNewFactionTakesNextId()
    {
        var metadata = new ReflectionConfigMetadataProvider();
        using var relations = new ConfigDocumentViewModel(new ConfigDocument
        {
            Definition = new ConfigDefinition("diplomatic-relations", "外交关系", "势力与组织", "DiplomaticRelations.json",
                typeof(DiplomaticRelationConfig), ConfigScope.Scenario),
            Items = [new DiplomaticRelationConfig { RelationFaction1ID = 2, RelationFaction2ID = 1, Relation = 500 },
                new DiplomaticRelationConfig { RelationFaction1ID = 3, RelationFaction2ID = 1 },
                new DiplomaticRelationConfig { RelationFaction1ID = 3, RelationFaction2ID = 2 }]
        }, metadata, _ => { });
        using var factions = new ConfigDocumentViewModel(
            FactionData(new FactionConfig { Id = 1, Name = "曹操" }, new FactionConfig { Id = 2, Name = "刘备" },
                new FactionConfig { Id = 3, Name = "孙坚" }),
            metadata, _ => { }, findRelatedDocument: key => key == "diplomatic-relations" ? relations : null);

        factions.SelectedRecord = factions.Records[0];
        factions.DeleteCommand.Execute(null);

        Assert.Equal([2, 3], factions.Records.Select(record => ((FactionConfig)record.Item).Id));
        var remaining = Assert.IsType<DiplomaticRelationConfig>(Assert.Single(relations.Records).Item);
        Assert.Equal((3, 2), (remaining.RelationFaction1ID, remaining.RelationFaction2ID));
        Assert.True(relations.IsDirty);

        factions.UndoCommand.Execute(null);
        Assert.Equal(3, factions.Records.Count);
        Assert.Equal(3, relations.Records.Count);
        Assert.False(relations.IsDirty);

        factions.AddCommand.Execute(null);
        Assert.Equal(4, ((FactionConfig)factions.Records[^1].Item).Id);
    }

    [Fact]
    public void FactionReferencesAndIdLists_AreRegisteredForValidation()
    {
        var properties = new ReflectionConfigMetadataProvider().GetProperties(typeof(FactionConfig))
            .ToDictionary(property => property.Name);

        Assert.Equal("persons", properties[nameof(FactionConfig.PrinceID)].Reference?.TargetConfigKey);
        Assert.Equal("official-title-kinds", properties[nameof(FactionConfig.OfficialRank)].Reference?.TargetConfigKey);
        Assert.Equal("architectures",
            properties[nameof(FactionConfig.ArchitecturesString)].StructuredString?.TargetConfigKey);
        Assert.Equal("techniques",
            properties[nameof(FactionConfig.AvailableTechniquesString)].StructuredString?.TargetConfigKey);
    }
}
