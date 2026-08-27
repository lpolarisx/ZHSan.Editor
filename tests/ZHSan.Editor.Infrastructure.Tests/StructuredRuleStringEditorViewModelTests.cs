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

    private static ConfigDocument CreateDocument(string key, Type itemType, params object[] items) => new()
    {
        Definition = new ConfigDefinition(key, key, "测试", $"{key}.json", itemType),
        Items = items.ToList(),
    };
}
