using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigDocumentViewModelTests
{
    [Fact]
    public void SearchAndPropertyEdit_UpdateVisibleRowsAndDirtyState()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig { Id = 2, Name = "进阶技术" };
        var viewModel = CreateViewModel([first, second]);

        viewModel.SearchText = "进阶";

        Assert.Single(viewModel.FilteredRecords);
        Assert.Same(second, viewModel.FilteredRecords[0].Item);

        viewModel.SelectedRecord = viewModel.FilteredRecords[0];
        var nameEditor = Assert.Single(viewModel.PropertyEditors, editor => editor.Definition.Name == "Name");
        nameEditor.ValueText = "高级技术";

        Assert.Equal("高级技术", second.Name);
        Assert.True(viewModel.IsDirty);
        Assert.Contains("高级技术", viewModel.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyAndDelete_ManageSelectedRecords()
    {
        var viewModel = CreateViewModel([new TechniqueConfig { Id = 1, Name = "技术" }]);
        viewModel.SelectedRecord = viewModel.Records[0];

        viewModel.CopyCommand.Execute(null);

        Assert.Equal(2, viewModel.ItemCount);
        Assert.NotSame(viewModel.Records[0].Item, viewModel.Records[1].Item);

        viewModel.DeleteCommand.Execute(null);

        Assert.Single(viewModel.Records);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void PropertyEdit_CanBeUndoneAndRedone()
    {
        var item = new TechniqueConfig { Id = 1, Name = "原名称" };
        var viewModel = CreateViewModel([item]);
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == "Name");

        editor.ValueText = "新名称";

        Assert.True(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
        Assert.Equal("新名称", item.Name);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal("原名称", item.Name);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanUndo);
        Assert.True(viewModel.CanRedo);

        viewModel.RedoCommand.Execute(null);

        Assert.Equal("新名称", item.Name);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
    }

    [Fact]
    public void NewEditAfterUndo_DiscardsRedoBranch()
    {
        var item = new TechniqueConfig { Id = 1, Name = "初始名称" };
        var viewModel = CreateViewModel([item]);
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == "Name");

        editor.ValueText = "第一次修改";
        viewModel.UndoCommand.Execute(null);

        Assert.True(viewModel.CanRedo);

        editor.ValueText = "分支修改";

        Assert.Equal("分支修改", item.Name);
        Assert.True(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal("初始名称", item.Name);
    }

    [Fact]
    public void RecordOperations_RestoreItemsAndSelection()
    {
        var original = new TechniqueConfig { Id = 1, Name = "技术" };
        var viewModel = CreateViewModel([original]);
        viewModel.SelectedRecord = viewModel.Records[0];

        viewModel.CopyCommand.Execute(null);
        var copy = viewModel.SelectedRecord;

        viewModel.UndoCommand.Execute(null);
        Assert.Single(viewModel.Records);
        Assert.Same(original, viewModel.Records[0].Item);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal(2, viewModel.Records.Count);
        Assert.Same(copy, viewModel.SelectedRecord);

        viewModel.DeleteCommand.Execute(null);
        Assert.Single(viewModel.Records);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal(2, viewModel.Records.Count);
        Assert.Same(copy, viewModel.SelectedRecord);
    }

    [Fact]
    public void Clipboard_CopiesAcrossDocumentsAndCutIsUndoable()
    {
        var clipboard = new RecordClipboard();
        var first = new TechniqueConfig { Id = 1, Name = "技术一" };
        var second = new TechniqueConfig { Id = 2, Name = "技术二" };
        var source = CreateViewModel([first, second], clipboard);
        var target = CreateViewModel(
            [new TechniqueConfig { Id = 3, Name = "已有技术" }],
            clipboard);
        source.SelectedRecord = source.Records[0];
        source.SetSelectedRecords(source.Records);

        source.CopyToClipboardCommand.Execute(null);
        target.PasteCommand.Execute(null);

        Assert.Equal(3, target.Records.Count);
        Assert.Equal("技术一", ((TechniqueConfig)target.Records[1].Item).Name);
        Assert.Equal("技术二", ((TechniqueConfig)target.Records[2].Item).Name);
        Assert.NotSame(first, target.Records[1].Item);
        Assert.NotSame(second, target.Records[2].Item);

        target.UndoCommand.Execute(null);
        Assert.Single(target.Records);

        source.CutCommand.Execute(null);
        Assert.Empty(source.Records);

        source.UndoCommand.Execute(null);
        Assert.Equal(2, source.Records.Count);
        Assert.Same(first, source.Records[0].Item);
        Assert.Same(second, source.Records[1].Item);
    }

    [Fact]
    public void Clipboard_RejectsRecordsFromAnotherConfigType()
    {
        var clipboard = new RecordClipboard();
        var source = CreateViewModel(
            [new TechniqueConfig { Id = 1, Name = "技术" }],
            clipboard);
        var treasureDocument = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "treasure", "宝物", "测试", "Treasure.json", typeof(TreasureCreationSettingConfig)),
            Items = [new TreasureCreationSettingConfig()]
        };
        var target = new ConfigDocumentViewModel(
            treasureDocument, new ReflectionConfigMetadataProvider(), _ => { }, clipboard);
        source.SelectedRecord = source.Records[0];

        source.CopyToClipboardCommand.Execute(null);

        Assert.False(target.PasteCommand.CanExecute(null));
    }

    [Fact]
    public void BatchEdit_ChangesSelectedRecordsAsSingleUndoStep()
    {
        var first = new TechniqueConfig { Id = 1, Name = "技术一" };
        var second = new TechniqueConfig { Id = 2, Name = "技术二" };
        var viewModel = CreateViewModel([first, second]);
        viewModel.SelectedRecord = viewModel.Records[0];
        viewModel.SetSelectedRecords(viewModel.Records);
        viewModel.SelectedBatchField = Assert.Single(
            viewModel.BatchEditFields,
            field => field.Property.Name == "Name");
        viewModel.BatchValueText = "统一名称";

        viewModel.ApplyBatchEditCommand.Execute(null);

        Assert.Equal("统一名称", first.Name);
        Assert.Equal("统一名称", second.Name);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal("技术一", first.Name);
        Assert.Equal("技术二", second.Name);
        Assert.False(viewModel.CanUndo);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal("统一名称", first.Name);
        Assert.Equal("统一名称", second.Name);
    }

    [Fact]
    public void CollectionEditor_RebuildsGenericList()
    {
        var item = new TreasureCreationSettingConfig
        {
            EligibleInfluenceIDs = [1, 2]
        };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "treasure", "宝物", "测试", "Treasure.json", typeof(TreasureCreationSettingConfig)),
            Items = [item]
        };
        var viewModel = new ConfigDocumentViewModel(document, new ReflectionConfigMetadataProvider(), _ => { });
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == "EligibleInfluenceIDs");

        editor.CollectionItems[0].ValueText = "9";
        editor.AddCollectionItemCommand.Execute(null);

        Assert.Equal([9, 2, 0], item.EligibleInfluenceIDs);
        Assert.True(document.IsDirty);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal([9, 2], item.EligibleInfluenceIDs);
        Assert.Equal(2, editor.CollectionItems.Count);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal([1, 2], item.EligibleInfluenceIDs);
        Assert.False(document.IsDirty);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal([9, 2], item.EligibleInfluenceIDs);
    }

    [Fact]
    public void GlobalSearch_FindsMatchingFieldAcrossDocumentsAndNavigates()
    {
        var firstDocument = CreateViewModel(
            [new TechniqueConfig { Id = 1, Name = "基础技术" }]);
        var targetItem = new TechniqueConfig { Id = 2, Name = "进阶技术" };
        var secondDocument = CreateViewModel([targetItem]);
        secondDocument.SearchText = "不存在";

        var match = Assert.Single(GlobalSearchEngine.Search(
            [firstDocument, secondDocument],
            "进阶"));

        Assert.Same(secondDocument, match.Document);
        Assert.Same(targetItem, match.Record.Item);
        Assert.Equal("Name", match.Property.Name);

        secondDocument.NavigateTo(match.Record);

        Assert.Equal(string.Empty, secondDocument.SearchText);
        Assert.Same(match.Record, secondDocument.SelectedRecord);
        Assert.Contains(match.Record, secondDocument.FilteredRecords);
    }

    [Fact]
    public void DocumentUiState_RestoresAndTracksFilterAndColumnWidths()
    {
        var state = new DocumentUiState
        {
            SearchText = "进阶",
            FilterPropertyName = "Name",
            ColumnWidths = [120, 240]
        };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items =
            [
                new TechniqueConfig { Id = 1, Name = "基础技术" },
                new TechniqueConfig { Id = 2, Name = "进阶技术" }
            ]
        };

        var viewModel = new ConfigDocumentViewModel(
            document,
            new ReflectionConfigMetadataProvider(),
            _ => { },
            uiState: state);

        Assert.Equal("进阶", viewModel.SearchText);
        Assert.Equal("Name", viewModel.SelectedFilterField.Property?.Name);
        Assert.Single(viewModel.FilteredRecords);
        Assert.Equal([120, 240], viewModel.SavedColumnWidths);

        viewModel.SearchText = "基础";
        viewModel.SelectedFilterField = viewModel.FilterFields[0];
        viewModel.SaveColumnWidths([150, 300]);

        Assert.Equal("基础", state.SearchText);
        Assert.Null(state.FilterPropertyName);
        Assert.Equal([150, 300], state.ColumnWidths);
    }

    [Fact]
    public void ReferenceEditor_UsesIndexedTargetsAndParticipatesInUndo()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig { Id = 2, Name = "进阶技术", PreID = 1 };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items = [first, second],
        };
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [document],
            ActiveDocument = document,
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var index = new ConfigReferenceIndex(metadata);
        index.Rebuild(project);
        var viewModel = new ConfigDocumentViewModel(
            document,
            metadata,
            _ => { },
            referenceIndex: index);
        viewModel.SelectedRecord = viewModel.Records[1];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == nameof(TechniqueConfig.PreID));

        Assert.True(editor.ShowReference);
        Assert.False(editor.ShowNumber);
        Assert.Equal(3, editor.ReferenceOptions.Count);
        Assert.Contains("基础技术", editor.SelectedReference?.Label);

        editor.SelectedReference = Assert.Single(
            editor.ReferenceOptions,
            option => option.Id == 2);

        Assert.Equal(2, second.PreID);
        Assert.True(document.IsDirty);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal(1, second.PreID);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void CollectionReferenceEditor_UsesReferenceChoiceForEachId()
    {
        var treasure = new TreasureCreationSettingConfig
        {
            Id = 1,
            Name = "宝物",
            EligibleInfluenceIDs = [10],
        };
        var treasureDocument = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "treasure-creation-settings",
                "宝物生成设置",
                "测试",
                "TreasureCreationSettings.json",
                typeof(TreasureCreationSettingConfig)),
            Items = [treasure],
        };
        var influenceDocument = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "influences", "影响", "测试", "Influences.json", typeof(InfluenceConfig)),
            Items =
            [
                new InfluenceConfig { Id = 10, Name = "影响一" },
                new InfluenceConfig { Id = 20, Name = "影响二" },
            ],
        };
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [treasureDocument, influenceDocument],
            ActiveDocument = treasureDocument,
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var index = new ConfigReferenceIndex(metadata);
        index.Rebuild(project);
        var viewModel = new ConfigDocumentViewModel(
            treasureDocument,
            metadata,
            _ => { },
            referenceIndex: index);
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == nameof(TreasureCreationSettingConfig.EligibleInfluenceIDs));
        var itemEditor = Assert.Single(editor.CollectionItems);

        Assert.True(itemEditor.ShowReference);
        Assert.Contains("影响一", itemEditor.SelectedReference?.Label);

        itemEditor.SelectedReference = Assert.Single(
            itemEditor.ReferenceOptions,
            option => option.Id == 20);

        Assert.Equal([20], treasure.EligibleInfluenceIDs);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal([10], treasure.EligibleInfluenceIDs);
    }

    private static ConfigDocumentViewModel CreateViewModel(
        IList<object> items,
        RecordClipboard? clipboard = null)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items = items
        };
        return new ConfigDocumentViewModel(
            document, new ReflectionConfigMetadataProvider(), _ => { }, clipboard);
    }
    [Fact]
    public void MarkSaved_TracksDirtyStateAgainstSavedHistoryPosition()
    {
        var item = new TechniqueConfig { Id = 1, Name = "original" };
        var viewModel = CreateViewModel([item]);
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == "Name");

        editor.ValueText = "saved";
        viewModel.MarkSaved();

        Assert.False(viewModel.IsDirty);
        viewModel.UndoCommand.Execute(null);
        Assert.True(viewModel.IsDirty);
        viewModel.RedoCommand.Execute(null);
        Assert.False(viewModel.IsDirty);
    }

}
