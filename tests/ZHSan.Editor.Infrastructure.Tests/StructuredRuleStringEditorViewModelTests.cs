using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class StructuredRuleStringEditorViewModelTests
{
    [Fact]
    public void InfluenceEditor_ShowsNamedTargetsWithoutReplacementOrReordering()
    {
        var technique = new TechniqueConfig { Id = 1, Name = "技术", InfluencesString = "10 20" };
        var (document, editor) = CreateEditor(
            technique,
            [
                new InfluenceConfig { Id = 10, Name = "影响甲" },
                new InfluenceConfig { Id = 20, Name = "影响乙" },
            ]);

        Assert.Equal(2, editor.Entries.Count);
        var first = editor.Entries[0];
        Assert.Contains("影响甲", first.ReferenceLabel);
        Assert.False(first.CanReplaceReference);
        Assert.True(first.ShowReadOnlyReference);
        Assert.False(first.CanReorder);
        Assert.False(first.MoveDownCommand.CanExecute(null));

        first.MoveDownCommand.Execute(null);

        Assert.Equal("10 20", technique.InfluencesString);
        Assert.False(document.CanUndo);
        Assert.Equal([10, 20], editor.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void InfluenceEditor_PreventsReplacementAndRemovesCompactRowsWithUndoSupport()
    {
        var technique = new TechniqueConfig { Id = 1, Name = "技术", InfluencesString = "10 20" };
        var (document, editor) = CreateEditor(
            technique,
            [
                new InfluenceConfig { Id = 10, Name = "影响甲" },
                new InfluenceConfig { Id = 20, Name = "影响乙" },
                new InfluenceConfig { Id = 30, Name = "影响丙" },
            ]);
        var replacement = Assert.Single(
            editor.Entries[0].ReferencePicker.FilteredOptions,
            option => option.Id == 30);

        editor.Entries[0].ReferencePicker.SelectedOption = replacement;

        Assert.Equal("10 20", technique.InfluencesString);
        Assert.Equal([10, 20], editor.Entries.Select(entry => entry.Id));
        Assert.False(document.CanUndo);

        editor.Entries[1].RemoveCommand.Execute(null);

        Assert.Equal("10", technique.InfluencesString);
        Assert.Single(editor.Entries);
        document.UndoCommand.Execute(null);
        Assert.Equal("10 20", technique.InfluencesString);
    }

    [Fact]
    public void InfluenceEditor_PreservesMalformedRawTextUntilUserRepairsIt()
    {
        var technique = new TechniqueConfig { Id = 1, Name = "技术", InfluencesString = "10 bad" };
        var (_, editor) = CreateEditor(
            technique,
            [new InfluenceConfig { Id = 10, Name = "影响甲" }]);

        Assert.False(editor.CanUseStructuredEditor);
        Assert.True(editor.HasIssues);
        Assert.Equal("10 bad", editor.RawText);

        editor.RawText = "10";

        Assert.True(editor.CanUseStructuredEditor);
        Assert.Equal(10, Assert.Single(editor.Entries).Id);
    }

    [Fact]
    public void WeightedConditionEditor_AddsAConditionWithDefaultWeight()
    {
        var technique = new TechniqueConfig { Id = 1, Name = "技术", AIConditionWeightString = string.Empty };
        var conditionDocument = CreateDocument(
            "conditions",
            typeof(ConditionConfig),
            new ConditionConfig { Id = 30, Name = "兵力充足" });
        var sourceDocument = CreateDocument("techniques", typeof(TechniqueConfig), technique);
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [conditionDocument, sourceDocument],
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild(project);
        var document = new ConfigDocumentViewModel(sourceDocument, metadata, _ => { }, referenceIndex: references);
        document.SelectedRecord = document.Records[0];
        var property = Assert.Single(
            document.PropertyEditors,
            item => item.Definition.Name == nameof(TechniqueConfig.AIConditionWeightString));
        var editor = Assert.IsType<StructuredRuleStringEditorViewModel>(property.StructuredStringEditor);
        var option = Assert.Single(editor.AddPicker.FilteredOptions, item => item.Id == 30);

        editor.AddPicker.SelectedOption = option;

        Assert.Equal("30 1", technique.AIConditionWeightString);
        var entry = Assert.Single(editor.Entries);
        Assert.Equal(1f, entry.Weight);
        Assert.True(entry.CanReplaceReference);
        Assert.False(entry.ShowReadOnlyReference);
        Assert.True(entry.CanReorder);
    }

    [Fact]
    public void ConditionEditor_ExposesSemanticGroupsAndHidesRuntimeOperators()
    {
        var technique = new TechniqueConfig
        {
            Id = 1,
            Name = "技术",
            ConditionTableString = "10 996 20 997 30",
        };
        var (document, editor) = CreateConditionEditor(
            technique,
            [
                new ConditionConfig { Id = 10, Name = "条件甲" },
                new ConditionConfig { Id = 20, Name = "条件乙" },
                new ConditionConfig { Id = 30, Name = "条件丙" },
                new ConditionConfig { Id = 996, Name = "运行时非操作符" },
                new ConditionConfig { Id = 997, Name = "运行时或操作符" },
            ]);

        Assert.True(editor.IsConditionExpression);
        Assert.False(editor.ShowFlatList);
        Assert.False(editor.ShowRawEditor);
        Assert.Empty(editor.Entries);
        Assert.Equal(2, editor.ConditionGroups.Count);
        Assert.Equal([10, 20], editor.ConditionGroups[0].Terms.Select(term => term.Id));
        Assert.False(editor.ConditionGroups[0].Terms[0].IsNegated);
        Assert.True(editor.ConditionGroups[0].Terms[1].IsNegated);
        Assert.Equal(30, Assert.Single(editor.ConditionGroups[1].Terms).Id);
        Assert.DoesNotContain(editor.AddPicker.FilteredOptions, option => option.Id is 996 or 997);

        editor.ToggleRawEditorCommand.Execute(null);
        Assert.True(editor.ShowRawEditor);
        editor.ToggleRawEditorCommand.Execute(null);
        Assert.False(editor.ShowRawEditor);

        editor.ConditionGroups[0].Terms[0].IsNegated = true;

        Assert.Equal("996 10 996 20 997 30", technique.ConditionTableString);
        document.UndoCommand.Execute(null);
        Assert.Equal("10 996 20 997 30", technique.ConditionTableString);
    }

    [Fact]
    public void ConditionEditor_AddsToAGroupOrCreatesAnAlternativeGroup()
    {
        var technique = new TechniqueConfig { Id = 1, Name = "技术", ConditionTableString = "10" };
        var (_, editor) = CreateConditionEditor(
            technique,
            [
                new ConditionConfig { Id = 10, Name = "条件甲" },
                new ConditionConfig { Id = 20, Name = "条件乙" },
                new ConditionConfig { Id = 30, Name = "条件丙" },
            ]);

        editor.AddPicker.SelectedOption = Assert.Single(
            editor.AddPicker.FilteredOptions,
            option => option.Id == 20);
        Assert.Equal("10 20", technique.ConditionTableString);

        editor.AddAsAlternativeGroup = true;
        editor.AddPicker.SelectedOption = Assert.Single(
            editor.AddPicker.FilteredOptions,
            option => option.Id == 30);

        Assert.Equal("10 20 997 30", technique.ConditionTableString);
        Assert.Equal(2, editor.ConditionGroups.Count);
    }

    [Fact]
    public void ConditionEditor_ReordersTermsAndAlternativeGroupsAndRemovesWholeGroups()
    {
        var technique = new TechniqueConfig
        {
            Id = 1,
            Name = "技术",
            ConditionTableString = "10 20 997 30",
        };
        var (_, editor) = CreateConditionEditor(
            technique,
            [
                new ConditionConfig { Id = 10, Name = "条件甲" },
                new ConditionConfig { Id = 20, Name = "条件乙" },
                new ConditionConfig { Id = 30, Name = "条件丙" },
            ]);

        editor.ConditionGroups[0].Terms[0].MoveDownCommand.Execute(null);
        Assert.Equal("20 10 997 30", technique.ConditionTableString);

        editor.ConditionGroups[0].MoveDownCommand.Execute(null);
        Assert.Equal("30 997 20 10", technique.ConditionTableString);

        editor.ConditionGroups[0].RemoveCommand.Execute(null);
        Assert.Equal("20 10", technique.ConditionTableString);
        Assert.Single(editor.ConditionGroups);
    }

    [Fact]
    public void ConditionEditor_PreservesMalformedRawTextUntilSemanticallyRepaired()
    {
        var technique = new TechniqueConfig { Id = 1, Name = "技术", ConditionTableString = "997 10" };
        var (_, editor) = CreateConditionEditor(
            technique,
            [new ConditionConfig { Id = 10, Name = "条件甲" }]);

        Assert.False(editor.CanUseStructuredEditor);
        Assert.True(editor.ShowRawEditor);
        Assert.Empty(editor.ConditionGroups);
        Assert.Contains("或", editor.IssueSummary);

        editor.RawText = "996 10";

        Assert.True(editor.CanUseStructuredEditor);
        var term = Assert.Single(Assert.Single(editor.ConditionGroups).Terms);
        Assert.Equal(10, term.Id);
        Assert.True(term.IsNegated);
    }

    private static (ConfigDocumentViewModel Document, StructuredRuleStringEditorViewModel Editor) CreateEditor(
        TechniqueConfig technique,
        InfluenceConfig[] influences)
    {
        var influenceDocument = CreateDocument("influences", typeof(InfluenceConfig), influences.Cast<object>().ToArray());
        var techniqueDocument = CreateDocument("techniques", typeof(TechniqueConfig), technique);
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [influenceDocument, techniqueDocument],
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild(project);
        var document = new ConfigDocumentViewModel(techniqueDocument, metadata, _ => { }, referenceIndex: references);
        document.SelectedRecord = document.Records[0];
        var property = Assert.Single(
            document.PropertyEditors,
            item => item.Definition.Name == nameof(TechniqueConfig.InfluencesString));
        return (document, Assert.IsType<StructuredRuleStringEditorViewModel>(property.StructuredStringEditor));
    }

    private static (ConfigDocumentViewModel Document, StructuredRuleStringEditorViewModel Editor)
        CreateConditionEditor(TechniqueConfig technique, ConditionConfig[] conditions)
    {
        var conditionDocument = CreateDocument("conditions", typeof(ConditionConfig), conditions.Cast<object>().ToArray());
        var techniqueDocument = CreateDocument("techniques", typeof(TechniqueConfig), technique);
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [conditionDocument, techniqueDocument],
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild(project);
        var document = new ConfigDocumentViewModel(techniqueDocument, metadata, _ => { }, referenceIndex: references);
        document.SelectedRecord = document.Records[0];
        var property = Assert.Single(
            document.PropertyEditors,
            item => item.Definition.Name == nameof(TechniqueConfig.ConditionTableString));
        return (document, Assert.IsType<StructuredRuleStringEditorViewModel>(property.StructuredStringEditor));
    }

    private static ConfigDocument CreateDocument(string key, Type itemType, params object[] items) => new()
    {
        Definition = new ConfigDefinition(key, key, "测试", $"{key}.json", itemType),
        Items = items.ToList(),
    };
}
