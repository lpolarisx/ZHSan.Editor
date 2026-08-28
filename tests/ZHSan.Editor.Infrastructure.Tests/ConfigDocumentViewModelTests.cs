using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
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
    public void PropertyEditors_UseMultilineInputOnlyForDescriptionText()
    {
        var viewModel = CreateViewModel(
            [new TechniqueConfig { Id = 1, Name = "技术", Description = "较长的说明" }]);
        viewModel.SelectedRecord = viewModel.Records[0];

        var name = Assert.Single(
            viewModel.PropertyEditors,
            editor => editor.Definition.Name == nameof(TechniqueConfig.Name));
        var description = Assert.Single(
            viewModel.PropertyEditors,
            editor => editor.Definition.Name == nameof(TechniqueConfig.Description));

        Assert.True(name.ShowString);
        Assert.False(name.ShowMultilineString);
        Assert.False(description.ShowString);
        Assert.True(description.ShowMultilineString);
    }

    [Fact]
    public void Filter_UsesExactMatchingForIntegersAndFuzzyMatchingForStrings()
    {
        var exact = new TitleConfig { Id = 1, Name = "合欢", KindId = 2 };
        var contains = new TitleConfig { Id = 2, Name = "合欢进阶", KindId = 20 };
        var another = new TitleConfig { Id = 3, Name = "其他", KindId = 102 };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "titles", "称号", "测试", "Titles.json", typeof(TitleConfig)),
            Items = [exact, contains, another],
        };
        var viewModel = new ConfigDocumentViewModel(
            document,
            new ReflectionConfigMetadataProvider(),
            _ => { });

        viewModel.SelectedFilterField = Assert.Single(
            viewModel.FilterFields,
            field => field.Property?.Name == nameof(TitleConfig.KindId));
        viewModel.SearchText = "2";

        Assert.Same(exact, Assert.Single(viewModel.FilteredRecords).Item);

        viewModel.SelectedFilterField = Assert.Single(
            viewModel.FilterFields,
            field => field.Property?.Name == nameof(TitleConfig.Name));
        viewModel.SearchText = "合欢";

        Assert.Equal([exact, contains], viewModel.FilteredRecords.Select(record => record.Item));
    }

    [Fact]
    public void NavigateToFilteredId_FiltersToTargetAndShowsGenericTable()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var target = new TechniqueConfig { Id = 2, Name = "目标技术" };
        var similar = new TechniqueConfig { Id = 20, Name = "相似 ID 技术" };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items = [first, target, similar],
        };
        var viewModel = new ConfigDocumentViewModel(
            document,
            new ReflectionConfigMetadataProvider(),
            _ => { },
            editorProviderRegistry: new ConfigEditorProviderRegistry([new TechniqueTreeEditorProvider()]));

        Assert.True(viewModel.IsSpecializedEditorActive);

        var wasLocated = viewModel.NavigateToFilteredId(2);

        Assert.True(wasLocated);
        Assert.Equal(nameof(TechniqueConfig.Id), viewModel.SelectedFilterField.Property?.Name);
        Assert.Equal("2", viewModel.SearchText);
        Assert.Same(target, Assert.Single(viewModel.FilteredRecords).Item);
        Assert.Same(target, viewModel.SelectedRecord?.Item);
        Assert.True(viewModel.IsGenericEditorActive);
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
        ConfigReferenceTarget? navigatedTarget = null;
        var viewModel = new ConfigDocumentViewModel(
            document,
            metadata,
            _ => { },
            referenceIndex: index,
            navigateReference: target => navigatedTarget = target);
        viewModel.SelectedRecord = viewModel.Records[1];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == nameof(TechniqueConfig.PreID));

        Assert.True(editor.ShowReference);
        Assert.False(editor.ShowNumber);
        Assert.Equal(3, editor.ReferenceOptions.Count);
        Assert.Contains("基础技术", editor.SelectedReference?.Label);
        var picker = Assert.IsType<ReferencePickerViewModel>(editor.ReferencePicker);
        Assert.True(picker.SupportsNavigation);

        picker.SearchText = "进阶";

        Assert.Equal(2, picker.FilteredOptions.Count);
        Assert.Contains(picker.FilteredOptions, option => option.Id == 1);
        Assert.Contains(picker.FilteredOptions, option => option.Id == 2);

        picker.SelectedOption = Assert.Single(
            picker.FilteredOptions,
            option => option.Id == 2);

        Assert.Equal(2, second.PreID);
        Assert.True(document.IsDirty);
        Assert.True(picker.NavigateCommand.CanExecute(null));

        picker.NavigateCommand.Execute(null);

        Assert.Equal(2, navigatedTarget?.Id);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal(1, second.PreID);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void ReferenceColumns_DisplayIdsWithAssociatedNamesAndRefreshThem()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig { Id = 2, Name = "进阶技术", PreID = 1, PostID = 404 };
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
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var index = new ConfigReferenceIndex(metadata);
        index.Rebuild(project);
        var viewModel = new ConfigDocumentViewModel(
            document,
            metadata,
            _ => { },
            referenceIndex: index);
        var properties = viewModel.Properties.Select((property, index) => (property, index)).ToArray();
        var preIdIndex = Assert.Single(
            properties,
            item => item.property.Name == nameof(TechniqueConfig.PreID)).index;
        var postIdIndex = Assert.Single(
            properties,
            item => item.property.Name == nameof(TechniqueConfig.PostID)).index;

        Assert.Equal("#1 · 基础技术", viewModel.Records[1].Cells[preIdIndex].DisplayValue);
        Assert.Equal("#404 · [目标不存在]", viewModel.Records[1].Cells[postIdIndex].DisplayValue);

        first.Name = "基础科技";
        index.Rebuild(project);
        viewModel.RefreshReferenceOptions();

        Assert.Equal("#1 · 基础科技", viewModel.Records[1].Cells[preIdIndex].DisplayValue);
    }

    [Fact]
    public void ReferenceEditor_MarksMissingAndDuplicateTargetsAsInvalid()
    {
        var first = new TechniqueConfig { Id = 1, Name = "重复一" };
        var duplicate = new TechniqueConfig { Id = 1, Name = "重复二" };
        var source = new TechniqueConfig { Id = 2, Name = "来源", PreID = 404 };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items = [first, duplicate, source],
        };
        var project = new EditorProject
        {
            ArchivePath = "test.dat",
            Documents = [document],
        };
        var metadata = new ReflectionConfigMetadataProvider();
        var index = new ConfigReferenceIndex(metadata);
        index.Rebuild(project);
        var viewModel = new ConfigDocumentViewModel(
            document,
            metadata,
            _ => { },
            referenceIndex: index);
        viewModel.SelectedRecord = viewModel.Records[2];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == nameof(TechniqueConfig.PreID));
        var picker = Assert.IsType<ReferencePickerViewModel>(editor.ReferencePicker);

        Assert.True(editor.ReferenceOptions.Single(option => option.Id == 1).IsMissing);
        Assert.Contains("重复", editor.ReferenceOptions.Single(option => option.Id == 1).Label);
        Assert.True(picker.HasMissingSelection);
        Assert.False(picker.NavigateCommand.CanExecute(null));
        Assert.Contains("不存在", picker.SelectedOption?.Label);
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
        var propertyIndex = viewModel.Properties
            .Select((property, index) => (property, index))
            .Single(item => item.property.Name == nameof(TreasureCreationSettingConfig.EligibleInfluenceIDs))
            .index;

        Assert.True(itemEditor.ShowReference);
        Assert.Contains("影响一", itemEditor.SelectedReference?.Label);
        Assert.Equal("#10 · 影响一", viewModel.Records[0].Cells[propertyIndex].DisplayValue);
        var picker = Assert.IsType<ReferencePickerViewModel>(itemEditor.ReferencePicker);

        picker.SearchText = "影响二";

        Assert.Equal(2, picker.FilteredOptions.Count);

        picker.SelectedOption = Assert.Single(
            picker.FilteredOptions,
            option => option.Id == 20);

        Assert.Equal([20], treasure.EligibleInfluenceIDs);
        Assert.Equal("#20 · 影响二", viewModel.Records[0].Cells[propertyIndex].DisplayValue);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal([10], treasure.EligibleInfluenceIDs);
    }

    [Fact]
    public void Delete_WithIncomingReference_ShowsImpactAndRequiresConfirmation()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig
        {
            Id = 2,
            Name = "进阶技术",
            PreID = 1,
        };
        var prompt = new FakeReferenceDeletionPrompt { Confirmed = false };
        var viewModel = CreateReferenceAwareViewModel([first, second], prompt);
        viewModel.SelectedRecord = viewModel.Records[0];

        viewModel.DeleteCommand.Execute(null);

        Assert.Equal(2, viewModel.Records.Count);
        Assert.Equal("删除", prompt.OperationName);
        Assert.Equal(1, prompt.SelectedRecordCount);
        var impact = Assert.Single(prompt.Impacts);
        Assert.Equal(1, impact.Target.Id);
        Assert.Equal(2, Assert.Single(impact.References).RecordId);
        Assert.Equal("已取消删除", viewModel.NotificationMessage);

        prompt.Confirmed = true;
        viewModel.DeleteCommand.Execute(null);

        Assert.Single(viewModel.Records);
        Assert.Same(second, viewModel.Records[0].Item);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(2, viewModel.Records.Count);
    }

    [Fact]
    public void Cut_WithIncomingReference_IsBlockedWhenImpactIsNotConfirmed()
    {
        var clipboard = new RecordClipboard();
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig
        {
            Id = 2,
            Name = "进阶技术",
            PreID = 1,
        };
        var prompt = new FakeReferenceDeletionPrompt { Confirmed = false };
        var viewModel = CreateReferenceAwareViewModel([first, second], prompt, clipboard);
        viewModel.SelectedRecord = viewModel.Records[0];

        viewModel.CutCommand.Execute(null);

        Assert.Equal(2, viewModel.Records.Count);
        Assert.False(clipboard.Contains(typeof(TechniqueConfig)));
        Assert.Equal("剪切", prompt.OperationName);
    }

    private static ConfigDocumentViewModel CreateReferenceAwareViewModel(
        IList<object> items,
        IReferenceDeletionPrompt prompt,
        RecordClipboard? clipboard = null)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items = items,
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
        return new ConfigDocumentViewModel(
            document,
            metadata,
            _ => { },
            clipboard,
            referenceIndex: index,
            referenceDeletionPrompt: prompt);
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

    private sealed class FakeReferenceDeletionPrompt : IReferenceDeletionPrompt
    {
        public bool Confirmed { get; set; }
        public string? OperationName { get; private set; }
        public int SelectedRecordCount { get; private set; }
        public IReadOnlyList<ConfigReferenceImpact> Impacts { get; private set; } = [];

        public Task<bool> ConfirmAsync(
            string operationName,
            int selectedRecordCount,
            IReadOnlyList<ConfigReferenceImpact> impacts)
        {
            OperationName = operationName;
            SelectedRecordCount = selectedRecordCount;
            Impacts = impacts;
            return Task.FromResult(Confirmed);
        }
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

    [Fact]
    public void ApplyImportedItems_IsDirtyAndCanBeUndoneAndRedone()
    {
        var original = new TechniqueConfig { Id = 1, Name = "当前" };
        var imported = new TechniqueConfig { Id = 1, Name = "导入" };
        var added = new TechniqueConfig { Id = 2, Name = "新增" };
        var viewModel = CreateViewModel([original]);

        viewModel.ApplyImportedItems([imported, added], "导入技术");

        Assert.True(viewModel.IsDirty);
        Assert.Equal([imported, added], viewModel.Document.Items);
        Assert.Equal(2, viewModel.Records.Count);

        viewModel.UndoCommand.Execute(null);
        Assert.False(viewModel.IsDirty);
        Assert.Same(original, Assert.Single(viewModel.Document.Items));

        viewModel.RedoCommand.Execute(null);
        Assert.True(viewModel.IsDirty);
        Assert.Equal([imported, added], viewModel.Document.Items);
    }

}
