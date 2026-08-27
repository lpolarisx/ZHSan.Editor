using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class FacilityKindLevelEditorViewModelTests
{
    [Fact]
    public void Provider_MatchesOnlyFacilityKindLevelConfiguration()
    {
        var provider = new FacilityKindLevelEditorProvider();

        Assert.True(provider.CanEdit(CreateLevelDefinition()));
        Assert.False(provider.CanEdit(new ConfigDefinition(
            "other", "设施等级", "设施", "FacilityKindLevels.json", typeof(FacilityKindLevelConfig))));
        Assert.False(provider.CanEdit(new ConfigDefinition(
            "facility-kind-levels", "设施等级", "设施", "FacilityKindLevels.json", typeof(FacilityKindConfig))));
    }

    [Fact]
    public void Rebuild_GroupsLevelsByNamedFacilityKindAndSortsByLevel()
    {
        var first = CreateLevel(11, 1, 2);
        var second = CreateLevel(10, 1, 1);
        var (_, editor) = CreateEditor(
            [new FacilityKindConfig { Id = 1, Name = "市场" }],
            first,
            second);

        var kind = Assert.Single(editor.KindOptions);
        Assert.Equal("#1 · 市场", kind.Label);
        Assert.Equal(2, kind.LevelCount);
        Assert.Equal([1, 2], editor.Levels.Select(level => level.Level));
        Assert.Equal(0, editor.IssueCount);
    }

    [Fact]
    public void Rebuild_ReportsMissingKindDuplicateCombinationAndLevelGap()
    {
        var (_, editor) = CreateEditor(
            [new FacilityKindConfig { Id = 1, Name = "市场" }],
            CreateLevel(1, 1, 1),
            CreateLevel(2, 1, 3),
            CreateLevel(3, 1, 3),
            CreateLevel(4, 404, 1));

        editor.SelectedKind = editor.KindOptions.Single(option => option.Id == 1);

        Assert.Contains(editor.Levels.Where(level => level.Level == 3).SelectMany(level => level.Issues),
            issue => issue.Contains("已存在等级 3"));
        Assert.Contains(editor.Levels.Where(level => level.Level == 3).SelectMany(level => level.Issues),
            issue => issue.Contains("不连续"));

        editor.SelectedKind = editor.KindOptions.Single(option => option.Id == 404);

        Assert.True(editor.SelectedKind.IsMissing);
        Assert.Contains(Assert.Single(editor.Levels).Issues, issue => issue.Contains("不存在"));
        Assert.False(editor.AddLevelCommand.CanExecute(null));
    }

    [Fact]
    public void AddLevel_AssignsNextIdAndLevelAndCanBeUndoneAsOneStep()
    {
        var (document, editor) = CreateEditor(
            [new FacilityKindConfig { Id = 1, Name = "市场" }],
            CreateLevel(7, 1, 1),
            CreateLevel(9, 1, 2));

        editor.AddLevelCommand.Execute(null);

        var added = Assert.IsType<FacilityKindLevelConfig>(document.Records.Single(record =>
            ((FacilityKindLevelConfig)record.Item).Id == 10).Item);
        Assert.Equal(1, added.KindId);
        Assert.Equal(3, added.Level);
        Assert.True(document.CanUndo);

        document.UndoCommand.Execute(null);

        Assert.Equal(2, document.Records.Count);
        Assert.False(document.CanUndo);

        document.RedoCommand.Execute(null);

        Assert.Equal(3, document.Records.Count);
        Assert.Contains(document.Records, record => ReferenceEquals(record.Item, added));
    }

    [Fact]
    public void ChangingCombination_UsesDocumentHistoryAndUpdatesGrouping()
    {
        var level = CreateLevel(1, 1, 1);
        var (document, editor) = CreateEditor(
            [
                new FacilityKindConfig { Id = 1, Name = "市场" },
                new FacilityKindConfig { Id = 2, Name = "农田" },
            ],
            level);

        editor.SelectedKindForLevel = editor.KindOptions.Single(option => option.Id == 2);

        Assert.Equal(2, level.KindId);
        Assert.Equal(2, editor.SelectedKind?.Id);
        Assert.True(document.CanUndo);

        document.UndoCommand.Execute(null);

        Assert.Equal(1, level.KindId);
        Assert.Equal(1, editor.SelectedKind?.Id);
    }

    [Fact]
    public void Selection_IsSynchronizedWithGenericDocumentEditor()
    {
        var first = CreateLevel(1, 1, 1);
        var second = CreateLevel(2, 1, 2);
        var (document, editor) = CreateEditor(
            [new FacilityKindConfig { Id = 1, Name = "市场" }],
            first,
            second);

        document.SelectedRecord = document.Records[1];

        Assert.Equal(2, editor.SelectedLevel?.Id);

        editor.SelectedLevel = editor.Levels.Single(level => level.Id == 1);

        Assert.Same(document.Records[0], document.SelectedRecord);
    }

    private static (ConfigDocumentViewModel Document, FacilityKindLevelEditorViewModel Editor) CreateEditor(
        FacilityKindConfig[] kinds,
        params FacilityKindLevelConfig[] levels)
    {
        var metadata = new ReflectionConfigMetadataProvider();
        var kindDocument = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "facility-kinds", "设施类型", "设施", "FacilityKinds.json", typeof(FacilityKindConfig)),
            Items = kinds.Cast<object>().ToList(),
        };
        var levelDocument = new ConfigDocument
        {
            Definition = CreateLevelDefinition(),
            Items = levels.Cast<object>().ToList(),
        };
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [kindDocument, levelDocument],
        };
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild(project);
        var registry = new ConfigEditorProviderRegistry([new FacilityKindLevelEditorProvider()]);
        var document = new ConfigDocumentViewModel(
            levelDocument,
            metadata,
            _ => { },
            referenceIndex: references,
            editorProviderRegistry: registry);
        return (document, Assert.IsType<FacilityKindLevelEditorViewModel>(document.SpecializedEditor?.Content));
    }

    private static ConfigDefinition CreateLevelDefinition() =>
        new("facility-kind-levels", "设施等级", "设施", "FacilityKindLevels.json", typeof(FacilityKindLevelConfig));

    private static FacilityKindLevelConfig CreateLevel(int id, int kindId, int level) => new()
    {
        Id = id,
        KindId = kindId,
        Level = level,
    };
}
