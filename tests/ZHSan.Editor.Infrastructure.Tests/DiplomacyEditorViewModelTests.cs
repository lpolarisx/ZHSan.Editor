using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class DiplomacyEditorViewModelTests
{
    private static (ConfigDocumentViewModel Document, DiplomacyEditorViewModel Editor) Create(
        params DiplomaticRelationConfig[] relations)
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var factionData = new ConfigDocument
        {
            Definition = new ConfigDefinition("factions", "势力", "势力与组织", "Factions.json",
                typeof(FactionConfig), ConfigScope.Scenario),
            Items = [new FactionConfig { Id = 1, Name = "曹操" }, new FactionConfig { Id = 2, Name = "刘备" },
                new FactionConfig { Id = 3, Name = "孙坚" }]
        };
        var relationData = new ConfigDocument
        {
            Definition = new ConfigDefinition("diplomatic-relations", "外交关系", "势力与组织", "DiplomaticRelations.json",
                typeof(DiplomaticRelationConfig), ConfigScope.Scenario),
            Items = [..relations]
        };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild([
            new EditorProject
            {
                Scope = ConfigScope.Scenario, ArchivePath = "scenario.dat", Documents = [factionData, relationData]
            }
        ]);
        var document = new ConfigDocumentViewModel(relationData, metadata, _ => { }, referenceIndex: references,
            editorProviderRegistry: new ConfigEditorProviderRegistry([new DiplomacyEditorProvider()]));
        return (document, Assert.IsType<DiplomacyEditorViewModel>(document.SpecializedEditor?.Content));
    }

    [Fact]
    public void Rows_ShowEveryOtherFactionWhicheverOrderThePairIsStoredIn()
    {
        var (document, editor) = Create(
            new DiplomaticRelationConfig { RelationFaction1ID = 2, RelationFaction2ID = 1, Relation = 500 },
            new DiplomaticRelationConfig { RelationFaction1ID = 3, RelationFaction2ID = 1, Relation = -300, Truce = 30 });
        using var _ = document;

        Assert.Equal(["曹操", "刘备", "孙坚"], editor.Factions.Select(faction => faction.Name));
        Assert.Equal("曹操 的外交关系", editor.SelectedTitle);
        Assert.Equal(["2 刘备", "3 孙坚"], editor.Rows.Select(row => row.OtherLabel));
        Assert.Equal(["同盟", "敌对"], editor.Rows.Select(row => row.State));
        Assert.Equal(30, editor.Rows[1].TruceValue);

        editor.SelectedFaction = editor.Factions[1];
        Assert.Equal(["1 曹操", "3 孙坚"], editor.Rows.Select(row => row.OtherLabel));
        Assert.Equal(["同盟", "无记录"], editor.Rows.Select(row => row.State));

        editor.Rows[0].RelationValue = 100;
        Assert.Equal(100, ((DiplomaticRelationConfig)document.Records[0].Item).Relation);
        Assert.Equal("中立", editor.Rows[0].State);
        document.UndoCommand.Execute(null);
        Assert.Equal(500, editor.Rows[0].RelationValue);
    }

    [Fact]
    public void HideNeutral_HidesEveryRowShownAsNeutralUnlessUnderTruce()
    {
        var (document, editor) = Create(
            new DiplomaticRelationConfig { RelationFaction1ID = 2, RelationFaction2ID = 1, Relation = 100 },
            new DiplomaticRelationConfig { RelationFaction1ID = 3, RelationFaction2ID = 1, Relation = 7, Truce = 30 },
            new DiplomaticRelationConfig { RelationFaction1ID = 3, RelationFaction2ID = 2, Relation = -315 });
        using var _ = document;
        Assert.Equal(["中立", "中立"], editor.Rows.Select(row => row.State));

        editor.HideNeutral = true;
        Assert.Equal([3], editor.Rows.Select(row => row.OtherId));

        editor.SelectedFaction = editor.Factions[1];
        Assert.Equal([3], editor.Rows.Select(row => row.OtherId));
        Assert.Equal("敌对", editor.Rows[0].State);
    }

    [Fact]
    public void ReferenceRefresh_KeepsRowsAndFilterSoTypingIsNotInterrupted()
    {
        var (document, editor) = Create(
            new DiplomaticRelationConfig { RelationFaction1ID = 2, RelationFaction2ID = 1, Relation = 500 },
            new DiplomaticRelationConfig { RelationFaction1ID = 3, RelationFaction2ID = 1 });
        using var _ = document;
        editor.HideNeutral = true;
        var row = Assert.Single(editor.Rows);
        var faction = editor.SelectedFaction;

        // The main window refreshes reference options after every edit.
        row.RelationValue = 450;
        document.RefreshReferenceOptions();

        Assert.Same(row, Assert.Single(editor.Rows));
        Assert.Same(faction, editor.SelectedFaction);
        Assert.True(editor.HideNeutral);
        Assert.Equal(450, row.RelationValue);
    }

    [Fact]
    public void EditingMissingPair_CreatesRecordWithHigherIdFirst()
    {
        var (document, editor) = Create(
            new DiplomaticRelationConfig { RelationFaction1ID = 2, RelationFaction2ID = 1 });
        using var _ = document;
        editor.SelectedFaction = editor.Factions[1];
        var sun = editor.Rows.Single(row => row.OtherId == 3);
        Assert.False(sun.HasRecord);

        sun.TruceValue = 90;

        var created = Assert.IsType<DiplomaticRelationConfig>(document.Records[^1].Item);
        Assert.Equal((3, 2, 0, 90), (created.RelationFaction1ID, created.RelationFaction2ID, created.Relation, created.Truce));
        sun = editor.Rows.Single(row => row.OtherId == 3);
        Assert.True(sun.HasRecord);
        Assert.Equal(90, sun.TruceValue);

        editor.HideNeutral = true;
        Assert.Equal([3], editor.Rows.Select(row => row.OtherId));

        document.UndoCommand.Execute(null);
        Assert.Single(document.Records);
    }
}
