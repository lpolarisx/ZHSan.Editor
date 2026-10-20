using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Editing;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ConfigDocumentViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private const string BiographiesKey = "biographies";
    private const string PersonFamiliesKey = "person-families";
    private const string PersonRelationsKey = "person-relations";
    private const string DiplomaticRelationsKey = "diplomatic-relations";

    private readonly IReadOnlyList<ConfigPropertyDefinition> _properties;
    private readonly List<ConfigRecordViewModel> _selectedRecords = [];
    private readonly RecordClipboard _clipboard;
    private readonly DocumentUiState _uiState;
    private readonly UndoRedoHistory _history = new();
    private readonly ConfigReferenceIndex? _referenceIndex;
    private readonly IReferenceDeletionPrompt? _referenceDeletionPrompt;
    private readonly Action<ConfigReferenceTarget>? _navigateReference;
    private readonly Func<string, ConfigDocumentViewModel?>? _findRelatedDocument;
    private bool _wasDirtyBeforeHistory;
    private int _savedHistoryPosition;
    private int _linkedEditOffset;
    private bool _arePropertyEditorsNeeded = true;
    private bool _propertyEditorsStale;
    private ConfigRecordViewModel? _validationTargetRecord;
    private string? _validationTargetProperty;
    private string _searchText = string.Empty;
    private ConfigFilterFieldViewModel _selectedFilterField;
    private BatchEditFieldViewModel? _selectedBatchField;
    private string _batchValueText = string.Empty;
    private ConfigRecordViewModel? _selectedRecord;
    private string _rawJson = "选择一条记录以查看 JSON。";
    private string? _notificationMessage;
    private bool _isSpecializedEditorActive;
    private readonly IConfigEditorProvider? _editorProvider;
    private ConfigEditorHostViewModel? _specializedEditor;
    private bool _isDisposed;

    public ConfigDocumentViewModel(
        ConfigDocument document,
        IConfigMetadataProvider metadataProvider,
        Action<ConfigDocumentViewModel> selectDocument,
        RecordClipboard? clipboard = null,
        DocumentUiState? uiState = null,
        ConfigReferenceIndex? referenceIndex = null,
        IReferenceDeletionPrompt? referenceDeletionPrompt = null,
        ConfigEditorProviderRegistry? editorProviderRegistry = null,
        Action<ConfigReferenceTarget>? navigateReference = null,
        Func<string, ConfigDocumentViewModel?>? findRelatedDocument = null)
    {
        Document = document;
        _clipboard = clipboard ?? new RecordClipboard();
        _uiState = uiState ?? new DocumentUiState();
        _referenceIndex = referenceIndex;
        _referenceDeletionPrompt = referenceDeletionPrompt;
        _navigateReference = navigateReference;
        _findRelatedDocument = findRelatedDocument;
        _wasDirtyBeforeHistory = document.IsDirty;
        _properties = metadataProvider.GetProperties(document.Definition.ItemType);
        FilterFields = [
            new ConfigFilterFieldViewModel("全部字段", null),
            .. _properties
                .Where(property => IsSearchable(property.PropertyType))
                .Select(property => new ConfigFilterFieldViewModel(property.DisplayName, property))
        ];
        _searchText = _uiState.SearchText ?? string.Empty;
        _selectedFilterField = FilterFields.FirstOrDefault(field =>
            field.Property?.Name == _uiState.FilterPropertyName) ?? FilterFields[0];
        BatchEditFields = _properties
            .Where(IsBatchEditable)
            .Select(property => new BatchEditFieldViewModel(property.DisplayName, property))
            .ToArray();
        _selectedBatchField = BatchEditFields.FirstOrDefault();

        NormalizeNullRecords();
        foreach (var item in document.Items)
        {
            Records.Add(CreateRecordViewModel(item));
        }

        SelectCommand = new RelayCommand(() => selectDocument(this));
        InitializeEntryCommand = new RelayCommand(InitializeEntry, () => CanInitializeEntry);
        AddCommand = new RelayCommand(AddRecord, CanCreateRecord);
        CopyCommand = new RelayCommand(
            CopySelectedRecords,
            () => SelectedRecord is not null && CanMutateRecordStructure);
        DeleteCommand = new AsyncCommand(
            DeleteSelectedRecordsAsync,
            () => SelectedRecord is not null && CanMutateRecordStructure);
        CutCommand = new AsyncCommand(
            CutSelectedRecordsAsync,
            () => SelectedRecord is not null && CanMutateRecordStructure);
        CopyToClipboardCommand = new RelayCommand(CopySelectionToClipboard, () => SelectedRecord is not null);
        PasteCommand = new RelayCommand(PasteRecords, CanPasteRecords);
        ApplyBatchEditCommand = new RelayCommand(ApplyBatchEdit, CanApplyBatchEdit);
        UndoCommand = new RelayCommand(Undo, () => _history.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _history.CanRedo);
        _history.Changed += HistoryChanged;
        _clipboard.Changed += ClipboardChanged;
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);
        ApplyFilter();
        _editorProvider = editorProviderRegistry?.Resolve(document.Definition);
        _isSpecializedEditorActive = _editorProvider is not null;
        ShowSpecializedEditorCommand = new RelayCommand(
            () => IsSpecializedEditorActive = true,
            () => HasSpecializedEditor && !IsSpecializedEditorActive);
        ShowGenericEditorCommand = new RelayCommand(
            () => IsSpecializedEditorActive = false,
            () => IsSpecializedEditorActive);
    }

    public event EventHandler? StateChanged;
    public event EventHandler? ReferenceOptionsChanged;

    internal ConfigDocumentViewModel? FindRelatedDocument(string key) =>
        _findRelatedDocument?.Invoke(key);

    internal Action<ConfigReferenceTarget>? NavigateReference => _navigateReference;

    public ConfigDocument Document { get; }
    public string Key => Document.Definition.Key;
    public string DisplayName => Document.Definition.DisplayName;
    public string EntryName => Document.Definition.EntryName;
    public bool IsBiography => Document.Definition.ItemType == typeof(BiographyConfig);
    public IReadOnlyList<ConfigPropertyDefinition> Properties => _properties;
    public IReadOnlyList<ConfigFilterFieldViewModel> FilterFields { get; }
    public IReadOnlyList<BatchEditFieldViewModel> BatchEditFields { get; }
    public ObservableCollection<ConfigRecordViewModel> Records { get; } = [];
    public ObservableCollection<ConfigRecordViewModel> FilteredRecords { get; } = [];
    public ObservableCollection<PropertyEditorViewModel> PropertyEditors { get; } = [];

    /// <summary>
    /// Whether the property panel is showing. While it is not, selecting a record skips building the property
    /// editors (about 120 for a person) and builds them when the panel is shown again.
    /// </summary>
    public bool ArePropertyEditorsNeeded
    {
        get => _arePropertyEditorsNeeded;
        set
        {
            if (_arePropertyEditorsNeeded == value)
            {
                return;
            }

            _arePropertyEditorsNeeded = value;
            if (value && _propertyEditorsStale)
            {
                RebuildPropertyEditors();
            }
        }
    }
    // Built the first time the document is shown: a project has dozens of tables and most are never opened.
    public ConfigEditorHostViewModel? SpecializedEditor =>
        _specializedEditor ??= _editorProvider is null || _isDisposed
            ? null
            : ConfigEditorProviderRegistry.CreateEditor(_editorProvider, this);
    public ICommand SelectCommand { get; }
    public ICommand InitializeEntryCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand CopyToClipboardCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand ApplyBatchEditCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ShowSpecializedEditorCommand { get; }
    public ICommand ShowGenericEditorCommand { get; }

    public int ItemCount => Records.Count + Document.NullRecordIndices.Count;
    public int VisibleItemCount => FilteredRecords.Count;
    public bool HasNullRecords => Document.NullRecordIndices.Count > 0;
    public bool CanInitializeEntry =>
        Document.EntryState is ArchiveEntryState.Missing or ArchiveEntryState.Null;
    public bool HasEntryWarning => CanInitializeEntry || HasNullRecords;
    public string EntryStateMessage
    {
        get
        {
            if (Document.EntryState == ArchiveEntryState.Missing)
            {
                return $"文件中缺少 {EntryName}。在明确初始化前，保存不会创建该条目。";
            }

            if (Document.EntryState == ArchiveEntryState.Null)
            {
                return $"{EntryName} 的顶层值为 null。它与空列表不同，在明确初始化前会保持 null。";
            }

            return HasNullRecords
                ? $"列表中包含 {Document.NullRecordIndices.Count} 条 null 记录；已保留原位置，并暂时禁用记录结构操作。"
                : string.Empty;
        }
    }
    private bool CanMutateRecordStructure => !CanInitializeEntry && !HasNullRecords;
    public bool HasMultipleSelection => _selectedRecords.Count > 1;
    public bool IsDirty => Document.IsDirty;
    public string DirtyMarker => IsDirty ? " ●" : string.Empty;
    public string DisplayLabel => DisplayName + DirtyMarker;
    public string RawJson => _rawJson;
    public string? NotificationMessage => _notificationMessage;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public bool HasSpecializedEditor => _editorProvider is not null;
    public bool IsSpecializedEditorActive
    {
        get => _isSpecializedEditorActive;
        private set
        {
            if (SetProperty(ref _isSpecializedEditorActive, value))
            {
                OnPropertyChanged(nameof(IsGenericEditorActive));
                ((RelayCommand)ShowSpecializedEditorCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ShowGenericEditorCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public bool IsGenericEditorActive => !IsSpecializedEditorActive;
    public string UndoLabel => _history.UndoDescription is null ? "撤销" : $"撤销 {_history.UndoDescription}";
    public string RedoLabel => _history.RedoDescription is null ? "重做" : $"重做 {_history.RedoDescription}";
    public IReadOnlyList<double> SavedColumnWidths => _uiState.ColumnWidths;

    public BatchEditFieldViewModel? SelectedBatchField
    {
        get => _selectedBatchField;
        set
        {
            if (SetProperty(ref _selectedBatchField, value))
            {
                ((RelayCommand)ApplyBatchEditCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string BatchValueText
    {
        get => _batchValueText;
        set => SetProperty(ref _batchValueText, value ?? string.Empty);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                _uiState.SearchText = _searchText;
                ((RelayCommand)ClearSearchCommand).RaiseCanExecuteChanged();
                ApplyFilter();
            }
        }
    }

    public ConfigFilterFieldViewModel SelectedFilterField
    {
        get => _selectedFilterField;
        set
        {
            if (SetProperty(ref _selectedFilterField, value))
            {
                _uiState.FilterPropertyName = value.Property?.Name;
                ApplyFilter();
            }
        }
    }

    public ConfigRecordViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetProperty(ref _selectedRecord, value))
            {
                if (value is not null && !_selectedRecords.Contains(value))
                {
                    _selectedRecords.Clear();
                    _selectedRecords.Add(value);
                }
                else if (value is null)
                {
                    _selectedRecords.Clear();
                }

                RebuildPropertyEditors();
                RaiseSelectionStateChanged();
            }
        }
    }

    public string FilterSummary => SearchText.Length == 0
        ? $"共 {ItemCount} 条记录"
        : $"显示 {VisibleItemCount} / {ItemCount} 条记录";

    public string SelectionSummary => _selectedRecords.Count switch
    {
        0 => "未选择记录",
        1 => "已选择 1 条记录",
        _ => $"已选择 {_selectedRecords.Count} 条记录"
    };

    public void SetSelectedRecords(IEnumerable selectedItems)
    {
        _selectedRecords.Clear();
        _selectedRecords.AddRange(selectedItems.Cast<object?>().OfType<ConfigRecordViewModel>());
        if (_selectedRecords.Count == 0)
        {
            SelectedRecord = null;
        }
        else if (!_selectedRecords.Contains(SelectedRecord!))
        {
            SelectedRecord = _selectedRecords[0];
        }

        RaiseSelectionStateChanged();
    }

    public void RefreshReferenceOptions()
    {
        foreach (var record in Records)
        {
            record.Refresh();
        }

        foreach (var editor in PropertyEditors.Where(editor => editor.UsesReferenceOptions))
        {
            editor.ReloadReferenceOptions(GetReferenceTargets(editor.Definition));
        }

        ReferenceOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NavigateTo(ConfigRecordViewModel record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!Records.Contains(record))
        {
            return;
        }

        SearchText = string.Empty;
        SelectedFilterField = FilterFields[0];
        SelectedRecord = record;
    }

    public void NavigateTo(int? itemId, string? propertyName)
    {
        if (itemId is { } id)
        {
            var record = Records.FirstOrDefault(candidate =>
                candidate.Item.GetType().GetProperty("Id")?.GetValue(candidate.Item) is int candidateId &&
                candidateId == id);
            if (record is not null)
            {
                NavigateTo(record);
            }
        }

        // Remembered so editors built later (while the property panel was hidden) are marked too.
        _validationTargetRecord = SelectedRecord;
        _validationTargetProperty = propertyName;
        MarkValidationTarget();
    }

    private void MarkValidationTarget()
    {
        var target = ReferenceEquals(SelectedRecord, _validationTargetRecord) ? _validationTargetProperty : null;
        foreach (var editor in PropertyEditors)
        {
            editor.SetValidationTarget(string.Equals(editor.Definition.Name, target, StringComparison.Ordinal));
        }
    }

    public void MarkSaved()
    {
        _wasDirtyBeforeHistory = false;
        _savedHistoryPosition = _history.Position;
        _linkedEditOffset = 0;
        Document.IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyMarker));
        OnPropertyChanged(nameof(DisplayLabel));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _history.Changed -= HistoryChanged;
        _clipboard.Changed -= ClipboardChanged;
        if (_specializedEditor?.Content is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public void SetPropertyValue(
        ConfigRecordViewModel record,
        string propertyName,
        object? value,
        string? editDescription = null) =>
        SetPropertyValues(
            [new ConfigEditorPropertyChange(record, propertyName, value)],
            editDescription ?? $"修改 {_properties.FirstOrDefault(property => property.Name == propertyName)?.DisplayName ?? propertyName}");

    public void SetPropertyValues(
        IEnumerable<ConfigEditorPropertyChange> changes,
        string editDescription)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentException.ThrowIfNullOrWhiteSpace(editDescription);
        var requestedChanges = changes.ToArray();
        if (requestedChanges.Length == 0)
        {
            return;
        }

        var preparedChanges = new List<PreparedPropertyChange>();
        foreach (var change in requestedChanges)
        {
            ArgumentNullException.ThrowIfNull(change.Record);
            ArgumentException.ThrowIfNullOrWhiteSpace(change.PropertyName);
            if (!Records.Contains(change.Record))
            {
                throw new ArgumentException("记录不属于当前配置文档。", nameof(changes));
            }

            var definition = _properties.FirstOrDefault(property => property.Name == change.PropertyName)
                ?? throw new ArgumentException($"配置不包含属性 {change.PropertyName}。", nameof(changes));
            if (!definition.CanWrite)
            {
                throw new InvalidOperationException($"属性 {change.PropertyName} 是只读的。");
            }

            var property = change.Record.Item.GetType().GetProperty(change.PropertyName)
                ?? throw new InvalidOperationException($"找不到属性 {change.PropertyName}。");
            if (change.Value is null && (!definition.AllowNullEditing ||
                property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null))
            {
                throw new ArgumentException($"属性 {change.PropertyName} 不接受空值。", nameof(changes));
            }

            if (change.Value is not null && !property.PropertyType.IsInstanceOfType(change.Value))
            {
                throw new ArgumentException(
                    $"属性 {change.PropertyName} 需要 {property.PropertyType.Name}，实际为 {change.Value.GetType().Name}。",
                    nameof(changes));
            }

            var existing = preparedChanges.FindIndex(prepared =>
                ReferenceEquals(prepared.Record, change.Record) &&
                prepared.Property.Name == change.PropertyName);
            if (existing >= 0)
            {
                preparedChanges[existing] = preparedChanges[existing] with
                {
                    NewValue = CloneValue(change.Value),
                };
            }
            else
            {
                preparedChanges.Add(new PreparedPropertyChange(
                    change.Record,
                    property,
                    CloneValue(property.GetValue(change.Record.Item)),
                    CloneValue(change.Value)));
            }
        }

        preparedChanges.RemoveAll(change => ValuesEqual(change.OldValue, change.NewValue));
        if (preparedChanges.Count == 0)
        {
            return;
        }

        void Apply(bool useNewValues)
        {
            foreach (var change in preparedChanges)
            {
                change.Property.SetValue(
                    change.Record.Item,
                    CloneValue(useNewValues ? change.NewValue : change.OldValue));
            }

            foreach (var record in preparedChanges.Select(change => change.Record).Distinct())
            {
                RefreshEditedRecord(record);
            }

            if (preparedChanges.Any(change => ReferenceEquals(SelectedRecord, change.Record)))
            {
                RebuildPropertyEditors();
            }
        }

        Apply(true);
        RecordEdit(new DelegateUndoableEdit(
            editDescription,
            () => Apply(false),
            () => Apply(true)),
            $"已{editDescription}");
    }

    public void ApplyImportedItems(
        IReadOnlyList<object> importedItems,
        string description)
    {
        ArgumentNullException.ThrowIfNull(importedItems);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var previousItems = GetItemsIncludingNullRecords();
        var previousEntryState = Document.EntryState;
        var nextItems = importedItems.ToArray();
        ReplaceItems(nextItems);
        RecordEdit(new DelegateUndoableEdit(
            description,
            () => ReplaceItems(previousItems, previousEntryState),
            () => ReplaceItems(nextItems)),
            description);
    }

    public void SaveColumnWidths(IEnumerable<double> widths)
    {
        _uiState.ColumnWidths = widths
            .Where(width => double.IsFinite(width) && width > 0)
            .ToList();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<ConfigPropertyDefinition> properties = SelectedFilterField.Property is null
            ? _properties
            : [SelectedFilterField.Property];
        var currentSelection = SelectedRecord;

        FilteredRecords.Clear();
        foreach (var record in Records)
        {
            if (query.Length == 0 || properties.Any(property => MatchesFilter(record.Item, property, query)))
            {
                FilteredRecords.Add(record);
            }
        }

        if (currentSelection is not null && !FilteredRecords.Contains(currentSelection))
        {
            SelectedRecord = null;
            _selectedRecords.Clear();
        }

        OnPropertyChanged(nameof(VisibleItemCount));
        OnPropertyChanged(nameof(FilterSummary));
        RaiseSelectionStateChanged();
    }

    private void AddRecord()
    {
        try
        {
            AddInitializedRecord(new Dictionary<string, object?>(), "新增记录");
        }
        catch (Exception exception)
        {
            SetNotification($"新增失败：{exception.GetBaseException().Message}");
        }
    }

    internal ConfigRecordViewModel AddInitializedRecord(
        IReadOnlyDictionary<string, object?> initialValues,
        string editDescription)
    {
        ArgumentNullException.ThrowIfNull(initialValues);
        ArgumentException.ThrowIfNullOrWhiteSpace(editDescription);
        if (!CanMutateRecordStructure)
        {
            throw new InvalidOperationException("请先初始化缺失或为 null 的条目，并处理列表中的 null 记录。");
        }

        var item = Activator.CreateInstance(Document.Definition.ItemType)
            ?? throw new InvalidOperationException("无法创建记录实例。");
        // Start references at their "none" value (e.g. -1) rather than pointing at ID 0 of the target table.
        foreach (var definition in _properties.Where(property => property.CanWrite && property.Name != "Id" &&
                     property.PropertyType == typeof(int) && property.Reference?.EmptyValue is not null))
        {
            Document.Definition.ItemType.GetProperty(definition.Name)?.SetValue(item, definition.Reference!.EmptyValue);
        }

        foreach (var (propertyName, value) in initialValues)
        {
            var definition = _properties.FirstOrDefault(property => property.Name == propertyName)
                ?? throw new ArgumentException($"配置不包含属性 {propertyName}。", nameof(initialValues));
            if (!definition.CanWrite)
            {
                throw new InvalidOperationException($"属性 {propertyName} 是只读的。");
            }

            var property = Document.Definition.ItemType.GetProperty(propertyName)
                ?? throw new InvalidOperationException($"找不到属性 {propertyName}。");
            if (value is null && (!definition.AllowNullEditing ||
                property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null))
            {
                throw new ArgumentException($"属性 {propertyName} 不接受空值。", nameof(initialValues));
            }

            if (value is not null && !property.PropertyType.IsInstanceOfType(value))
            {
                throw new ArgumentException(
                    $"属性 {propertyName} 需要 {property.PropertyType.Name}，实际为 {value.GetType().Name}。",
                    nameof(initialValues));
            }

            property.SetValue(item, CloneValue(value));
        }

        if (AssignsNextId && !initialValues.ContainsKey("Id"))
        {
            SetSequentialId(item, AllocateSequentialIds(1)[0]);
        }

        Document.Items.Add(item);
        var record = CreateRecordViewModel(item);
        Records.Add(record);
        FinishRecordMutation(record);
        var index = Records.IndexOf(record);
        var biographies = AddBiographiesForNewPersons([record]);
        RecordEdit(new DelegateUndoableEdit(
            editDescription,
            () => { RemoveRecords([record], null); biographies.Undo(); },
            () => { InsertRecords([(index, record)], record); biographies.Redo(); }),
            $"已{editDescription}{biographies.Message}");
        return record;
    }

    // Removes records on behalf of a specialized editor, as one undoable step and without the reference prompt.
    internal void RemoveRecordsWithHistory(IReadOnlyList<ConfigRecordViewModel> records, string editDescription)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(editDescription);
        if (!CanMutateRecordStructure)
        {
            throw new InvalidOperationException("请先初始化缺失或为 null 的条目，并处理列表中的 null 记录。");
        }

        var removed = records.Where(Records.Contains)
            .Select(record => (Index: Records.IndexOf(record), Record: record))
            .OrderBy(item => item.Index)
            .ToArray();
        if (removed.Length == 0)
        {
            return;
        }

        var removedRecords = removed.Select(item => item.Record).ToArray();
        var selection = SelectedRecord is { } selected && !removedRecords.Contains(selected)
            ? selected
            : Records.FirstOrDefault(record => !removedRecords.Contains(record));
        RemoveRecords(removedRecords, selection);
        RecordEdit(new DelegateUndoableEdit(
            editDescription,
            () => InsertRecords(removed, removed[0].Record),
            () => RemoveRecords(removedRecords, selection)),
            $"已{editDescription}");
    }

    private void CopySelectedRecords()
    {
        var sourceRecords = _selectedRecords.Count > 0
            ? _selectedRecords.ToArray()
            : SelectedRecord is null ? [] : [SelectedRecord];

        try
        {
            var copies = new List<(int Index, ConfigRecordViewModel Record)>();
            var newIds = new Queue<int>(AllocateSequentialIds(sourceRecords.Length));
            foreach (var source in sourceRecords)
            {
                var copy = CloneRecord(source.Item);
                if (AssignsNextId) SetSequentialId(copy, newIds.Dequeue());
                Document.Items.Add(copy);
                var copyRecord = CreateRecordViewModel(copy);
                Records.Add(copyRecord);
                copies.Add((Records.Count - 1, copyRecord));
            }

            if (copies.Count > 0)
            {
                var lastCopy = copies[^1].Record;
                FinishRecordMutation(lastCopy);
                var biographies = AddBiographiesForNewPersons(copies.Select(copy => copy.Record).ToArray());
                RecordEdit(new DelegateUndoableEdit(
                    $"复制 {copies.Count} 条记录",
                    () => { RemoveRecords(copies.Select(copy => copy.Record), null); biographies.Undo(); },
                    () => { InsertRecords(copies, lastCopy); biographies.Redo(); }),
                    $"已复制 {copies.Count} 条记录{biographies.Message}");
            }
        }
        catch (Exception exception)
        {
            SetNotification($"复制失败：{exception.GetBaseException().Message}");
        }
    }

    private async Task DeleteSelectedRecordsAsync()
    {
        var records = _selectedRecords.Count > 0
            ? _selectedRecords.ToArray()
            : SelectedRecord is null ? [] : [SelectedRecord];
        if (records.Length == 0)
        {
            return;
        }

        if (!await ConfirmRemovalAsync("删除", records))
        {
            return;
        }

        var removed = records
            .Select(record => (Index: Records.IndexOf(record), Record: record))
            .OrderBy(item => item.Index)
            .ToArray();
        var selectionAfterDelete = Records.FirstOrDefault(record => !records.Contains(record));
        RemoveRecords(records, selectionAfterDelete);
        var dependentData = RemoveDependentData(records);
        RecordEdit(new DelegateUndoableEdit(
            $"删除 {records.Length} 条记录",
            () => { dependentData.Undo(); InsertRecords(removed, removed[0].Record); },
            () => { RemoveRecords(records, selectionAfterDelete); dependentData.Redo(); }),
            $"已删除 {records.Length} 条记录{dependentData.Message}");
    }

    private void CopySelectionToClipboard()
    {
        var records = GetSelectedRecords();
        if (records.Length == 0)
        {
            return;
        }

        var copies = records.Select(record => CloneRecord(record.Item)).ToArray();
        _clipboard.Set(Document.Definition.ItemType, copies);
        SetNotification($"已复制 {copies.Length} 条记录到剪贴板");
    }

    private async Task CutSelectedRecordsAsync()
    {
        var records = GetSelectedRecords();
        if (records.Length == 0)
        {
            return;
        }

        if (!await ConfirmRemovalAsync("剪切", records))
        {
            return;
        }

        var clipboardItems = records.Select(record => CloneRecord(record.Item)).ToArray();
        _clipboard.Set(Document.Definition.ItemType, clipboardItems);
        var removed = records
            .Select(record => (Index: Records.IndexOf(record), Record: record))
            .OrderBy(item => item.Index)
            .ToArray();
        var selectionAfterCut = Records.FirstOrDefault(record => !records.Contains(record));
        RemoveRecords(records, selectionAfterCut);
        // Pasted persons and factions get new IDs, so the cut records' own data goes with them as on delete.
        var dependentData = RemoveDependentData(records);
        RecordEdit(new DelegateUndoableEdit(
            $"剪切 {records.Length} 条记录",
            () => { dependentData.Undo(); InsertRecords(removed, removed[0].Record); },
            () => { RemoveRecords(records, selectionAfterCut); dependentData.Redo(); }),
            $"已剪切 {records.Length} 条记录{dependentData.Message}");
    }

    private bool CanPasteRecords() =>
        CanMutateRecordStructure && _clipboard.Contains(Document.Definition.ItemType);

    private async Task<bool> ConfirmRemovalAsync(
        string operationName,
        IReadOnlyList<ConfigRecordViewModel> records)
    {
        if (_referenceIndex is null)
        {
            return true;
        }

        // References from the records' own dependent data (a person's biography, family record and relations,
        // a faction's diplomatic relations) are cleaned up together with them, so they are not reported.
        var cleanedKeys = GetDependentDocuments(records).Select(document => document.Key).ToHashSet();
        var impacts = _referenceIndex.GetDeletionImpacts(
                Document.Definition.Address,
                records.Select(record => record.Item))
            .Select(impact => impact with
            {
                References = impact.References
                    .Where(reference => reference.Scope != Document.Definition.Scope ||
                        !cleanedKeys.Contains(reference.ConfigKey))
                    .ToArray()
            })
            .Where(impact => impact.References.Count > 0)
            .ToArray();
        if (impacts.Length == 0)
        {
            return true;
        }

        if (_referenceDeletionPrompt is null)
        {
            SetNotification(
                $"已阻止{operationName}：所选记录仍被 {impacts.Sum(impact => impact.References.Count)} 处引用");
            return false;
        }

        try
        {
            var confirmed = await _referenceDeletionPrompt.ConfirmAsync(
                operationName,
                records.Count,
                impacts);
            if (!confirmed)
            {
                SetNotification($"已取消{operationName}");
            }

            return confirmed;
        }
        catch (Exception exception)
        {
            SetNotification(
                $"无法确认引用影响：{exception.GetBaseException().Message}");
            return false;
        }
    }

    private void PasteRecords()
    {
        if (!CanPasteRecords())
        {
            return;
        }

        try
        {
            var clones = _clipboard.Items.Select(CloneRecord).ToArray();
            var newIds = new Queue<int>(AllocateSequentialIds(clones.Length));
            if (AssignsNextId)
            {
                foreach (var clone in clones) SetSequentialId(clone, newIds.Dequeue());
            }
            var pasted = clones.Select(CreateRecordViewModel).ToArray();
            var insertions = new List<(int Index, ConfigRecordViewModel Record)>();
            foreach (var record in pasted)
            {
                var index = Records.Count;
                Document.Items.Add(record.Item);
                Records.Add(record);
                insertions.Add((index, record));
            }

            var lastRecord = pasted.LastOrDefault();
            FinishRecordMutation(lastRecord);
            var biographies = AddBiographiesForNewPersons(pasted);
            RecordEdit(new DelegateUndoableEdit(
                $"粘贴 {pasted.Length} 条记录",
                () => { RemoveRecords(pasted, null); biographies.Undo(); },
                () => { InsertRecords(insertions, lastRecord); biographies.Redo(); }),
                $"已粘贴 {pasted.Length} 条记录{biographies.Message}");
        }
        catch (Exception exception)
        {
            SetNotification($"粘贴失败：{exception.GetBaseException().Message}");
        }
    }

    private bool CanApplyBatchEdit() =>
        _selectedRecords.Count > 1 && SelectedBatchField is not null;

    private void ApplyBatchEdit()
    {
        if (!CanApplyBatchEdit() || SelectedBatchField is null)
        {
            return;
        }

        try
        {
            var definition = SelectedBatchField.Property;
            var property = Document.Definition.ItemType.GetProperty(definition.Name)
                ?? throw new InvalidOperationException($"找不到属性 {definition.Name}。");
            var value = ConvertBatchValue(BatchValueText, definition.PropertyType);
            var changes = GetSelectedRecords()
                .Select(record => (Record: record, OldValue: property.GetValue(record.Item)))
                .Where(change => !Equals(change.OldValue, value))
                .ToArray();
            if (changes.Length == 0)
            {
                SetNotification("所选记录已经是目标值");
                return;
            }

            SetBatchValue(changes.Select(change => change.Record), property, value);
            RecordEdit(new DelegateUndoableEdit(
                $"批量修改 {definition.DisplayName}",
                () =>
                {
                    foreach (var change in changes)
                    {
                        property.SetValue(change.Record.Item, change.OldValue);
                    }

                    RefreshBatchRecords(changes.Select(change => change.Record));
                },
                () => SetBatchValue(changes.Select(change => change.Record), property, value)),
                $"已将 {changes.Length} 条记录的 {definition.DisplayName} 修改为 {BatchValueText}");
        }
        catch (Exception exception)
        {
            SetNotification($"批量修改失败：{exception.GetBaseException().Message}");
        }
    }

    private void SetBatchValue(
        IEnumerable<ConfigRecordViewModel> records,
        System.Reflection.PropertyInfo property,
        object? value)
    {
        var changedRecords = records.ToArray();
        foreach (var record in changedRecords)
        {
            property.SetValue(record.Item, value);
        }

        RefreshBatchRecords(changedRecords);
    }

    private void RefreshBatchRecords(IEnumerable<ConfigRecordViewModel> records)
    {
        foreach (var record in records)
        {
            record.Refresh();
        }

        RefreshRawJson();
    }

    private ConfigRecordViewModel[] GetSelectedRecords() => _selectedRecords.Count > 0
        ? _selectedRecords.ToArray()
        : SelectedRecord is null ? [] : [SelectedRecord];

    // Person, treasure, faction and architecture IDs are sequential: new records continue after the highest ID in use.
    // Biographies are keyed by person ID, so leftover biographies count as in use and a new person never
    // inherits another's text.
    private static readonly IReadOnlySet<Type> SequentialIdTypes = new HashSet<Type>
    {
        typeof(PersonConfig),
        typeof(TreasureConfig),
        typeof(FactionConfig),
        typeof(ArchitectureConfig)
    };

    private bool HasSequentialIds => SequentialIdTypes.Contains(Document.Definition.ItemType);

    // New, copied and pasted records continue after the highest ID: in the sequential tables above, and in every
    // CommonData table keyed by its own ID (kinds, influences, titles, colours by position, …).
    private bool AssignsNextId => HasSequentialIds ||
        (Document.Definition.Scope == ConfigScope.Common &&
         Document.Definition.ItemType.GetProperty("Id") is { PropertyType: var type, CanWrite: true } && type == typeof(int));

    private static int GetSequentialId(object item) => (int)item.GetType().GetProperty("Id")!.GetValue(item)!;

    private static void SetSequentialId(object item, int id) => item.GetType().GetProperty("Id")!.SetValue(item, id);

    private int[] AllocateSequentialIds(int count)
    {
        if (!AssignsNextId)
        {
            return [];
        }

        var linkedIds = Document.Definition.ItemType == typeof(PersonConfig)
            ? FindRelatedDocument(BiographiesKey)?.Records
                .Select(record => record.Item).OfType<BiographyConfig>().Select(biography => biography.Id) ?? []
            : [];
        var next = Records.Select(record => GetSequentialId(record.Item))
            .Concat(linkedIds).DefaultIfEmpty(-1).Max() + 1;
        return Enumerable.Range(next, count).ToArray();
    }

    // A new person has no biography yet, so one is created alongside it and undone/redone with it.
    private LinkedRecordsChange AddBiographiesForNewPersons(IReadOnlyList<ConfigRecordViewModel> records)
    {
        var persons = records.Select(record => record.Item).OfType<PersonConfig>().ToArray();
        if (persons.Length == 0 || FindRelatedDocument(BiographiesKey) is not { } biographies)
        {
            return LinkedRecordsChange.None;
        }

        if (biographies.HasNullRecords)
        {
            return LinkedRecordsChange.None with { Message = "；列传包含 null 记录，未创建列传" };
        }

        var items = persons.Select(person => (object)new BiographyConfig
        {
            Id = person.Id,
            Brief = string.Empty,
            Romance = string.Empty,
            History = string.Empty,
            InGame = string.Empty
        }).ToArray();
        return biographies.AddLinkedRecords(items) with { Message = "，并创建列传" };
    }

    // Records added on behalf of another document's edit (a new person's biography). They live in that
    // document's undo history; this document only tracks how far they move it from its saved state.
    private LinkedRecordsChange AddLinkedRecords(IReadOnlyList<object> items)
    {
        var previousState = Document.EntryState;
        var records = items.Select(CreateRecordViewModel).ToArray();

        void Insert()
        {
            if (CanInitializeEntry)
            {
                Document.EntryState = ArchiveEntryState.Empty;
            }

            foreach (var record in records)
            {
                Document.Items.Add(record.Item);
                Records.Add(record);
            }

            FinishRecordMutation(SelectedRecord);
            ChangeLinkedEditOffset(1);
        }

        void Remove()
        {
            foreach (var record in records)
            {
                Document.Items.Remove(record.Item);
                Records.Remove(record);
            }

            Document.EntryState = previousState;
            FinishRecordMutation(SelectedRecord);
            ChangeLinkedEditOffset(-1);
        }

        Insert();
        return new LinkedRecordsChange(Remove, Insert, string.Empty);
    }

    // Documents whose records belong to the deleted records: a person's biography, family record and
    // relations, and a faction's diplomatic relations.
    private ConfigDocumentViewModel[] GetDependentDocuments(IReadOnlyList<ConfigRecordViewModel> records)
    {
        string[] keys = Document.Definition.ItemType == typeof(PersonConfig)
            ? [BiographiesKey, PersonFamiliesKey, PersonRelationsKey]
            : Document.Definition.ItemType == typeof(FactionConfig)
                ? [DiplomaticRelationsKey]
                : [];
        return records.Count == 0
            ? []
            : keys.Select(FindRelatedDocument).OfType<ConfigDocumentViewModel>()
                .Where(document => !document.HasNullRecords).ToArray();
    }

    // Deleting persons also deletes their biographies, family records and relations, and clears them from
    // other persons' family ties; deleting factions deletes their diplomatic relations. All of it is undone
    // and redone with the deletion.
    private LinkedRecordsChange RemoveDependentData(IReadOnlyList<ConfigRecordViewModel> records)
    {
        var ids = HasSequentialIds
            ? records.Select(record => GetSequentialId(record.Item)).ToHashSet()
            : [];
        if (ids.Count == 0)
        {
            return LinkedRecordsChange.None;
        }

        var changes = new List<LinkedRecordsChange>();
        var parts = new List<string>();
        foreach (var document in GetDependentDocuments(records))
        {
            var removed = document.Records.Where(record => record.Item switch
            {
                BiographyConfig biography => ids.Contains(biography.Id),
                PersonFamilyConfig family => ids.Contains(family.Id),
                PersonRelationConfig relation => ids.Contains(relation.PersonID1) || ids.Contains(relation.PersonID2),
                DiplomaticRelationConfig relation =>
                    ids.Contains(relation.RelationFaction1ID) || ids.Contains(relation.RelationFaction2ID),
                _ => false
            }).ToArray();
            var edits = document.Records.Except(removed)
                .SelectMany(record => record.Item is PersonFamilyConfig family
                    ? GetFamilyTieRemovals(family, ids).Select(edit => (record, edit.Property, edit.Value))
                    : [])
                .ToArray();
            if (removed.Length > 0)
            {
                changes.Add(document.RemoveLinkedRecords(removed));
                parts.Add(document.Key switch
                {
                    BiographiesKey => "列传",
                    PersonFamiliesKey => "亲属记录",
                    DiplomaticRelationsKey => $"{removed.Length} 条外交关系",
                    _ => $"{removed.Length} 条人物关系"
                });
            }

            if (edits.Length > 0)
            {
                changes.Add(document.SetLinkedPropertyValues(edits));
                parts.Add($"{edits.Select(edit => edit.record).Distinct().Count()} 名人物的亲属引用");
            }
        }

        return changes.Count == 0
            ? LinkedRecordsChange.None
            : new LinkedRecordsChange(
                () => { foreach (var change in Enumerable.Reverse(changes)) change.Undo(); },
                () => { foreach (var change in changes) change.Redo(); },
                $"，并清除{string.Join("、", parts)}");
    }

    private static IEnumerable<(PropertyInfo Property, object Value)> GetFamilyTieRemovals(
        PersonFamilyConfig family, IReadOnlySet<int> ids)
    {
        foreach (var name in new[] { nameof(PersonFamilyConfig.FatherId), nameof(PersonFamilyConfig.MotherId),
                     nameof(PersonFamilyConfig.SpouseId) })
        {
            var property = typeof(PersonFamilyConfig).GetProperty(name)!;
            if (ids.Contains((int)property.GetValue(family)!))
            {
                yield return (property, -1);
            }
        }

        foreach (var name in new[] { nameof(PersonFamilyConfig.BrotherIdsString),
                     nameof(PersonFamilyConfig.CloseIdsString), nameof(PersonFamilyConfig.HatedIdsString) })
        {
            var property = typeof(PersonFamilyConfig).GetProperty(name)!;
            var parsed = ConfigStructuredStringCodec.ParseIds((string?)property.GetValue(family));
            if (parsed.IsValid && parsed.Items.Any(ids.Contains))
            {
                yield return (property, ConfigStructuredStringCodec.FormatIds(parsed.Items.Where(id => !ids.Contains(id))));
            }
        }
    }

    // Records removed on behalf of another document's edit; see AddLinkedRecords.
    private LinkedRecordsChange RemoveLinkedRecords(IReadOnlyList<ConfigRecordViewModel> records)
    {
        var removed = records
            .Select(record => (Index: Records.IndexOf(record), Record: record))
            .OrderBy(item => item.Index)
            .ToArray();
        var selection = SelectedRecord;
        var selectionAfterRemove = selection is not null && records.Contains(selection) ? null : selection;

        void Remove()
        {
            RemoveRecords(records, selectionAfterRemove);
            ChangeLinkedEditOffset(1);
        }

        void Insert()
        {
            InsertRecords(removed, selection);
            ChangeLinkedEditOffset(-1);
        }

        Remove();
        return new LinkedRecordsChange(Insert, Remove, string.Empty);
    }

    // Property values changed on behalf of another document's edit; see AddLinkedRecords.
    private LinkedRecordsChange SetLinkedPropertyValues(
        IReadOnlyList<(ConfigRecordViewModel Record, PropertyInfo Property, object Value)> edits)
    {
        var changes = edits
            .Select(edit => (edit.Record, edit.Property, OldValue: edit.Property.GetValue(edit.Record.Item),
                NewValue: (object?)edit.Value))
            .ToArray();

        void Apply(bool useNewValues, int offset)
        {
            foreach (var change in changes)
            {
                change.Property.SetValue(change.Record.Item, useNewValues ? change.NewValue : change.OldValue);
            }

            foreach (var record in changes.Select(change => change.Record).Distinct())
            {
                RefreshEditedRecord(record);
            }

            if (changes.Any(change => ReferenceEquals(SelectedRecord, change.Record)))
            {
                RebuildPropertyEditors();
            }

            ChangeLinkedEditOffset(offset);
        }

        Apply(true, 1);
        return new LinkedRecordsChange(() => Apply(false, -1), () => Apply(true, 1), string.Empty);
    }

    private void ChangeLinkedEditOffset(int delta)
    {
        _linkedEditOffset += delta;
        UpdateDirtyState();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private object CloneRecord(object source)
    {
        var clone = Activator.CreateInstance(source.GetType())
            ?? throw new InvalidOperationException("无法创建记录副本。");
        foreach (var definition in _properties.Where(property => property.CanWrite))
        {
            var property = source.GetType().GetProperty(definition.Name);
            if (property is not null)
            {
                property.SetValue(clone, CloneValue(property.GetValue(source)));
            }
        }

        return clone;
    }

    private static object? CloneValue(object? value)
    {
        if (value is null || value is string || value.GetType().IsValueType)
        {
            return value;
        }

        if (value is Array array)
        {
            return array.Clone();
        }

        if (value is IList list && value.GetType().GetConstructor(Type.EmptyTypes) is not null)
        {
            var clone = (IList)Activator.CreateInstance(value.GetType())!;
            foreach (var item in list)
            {
                clone.Add(CloneValue(item));
            }

            return clone;
        }

        return value;
    }

    private static bool ValuesEqual(object? first, object? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null || second is null)
        {
            return false;
        }

        if (first is IEnumerable firstValues && second is IEnumerable secondValues &&
            first is not string && second is not string)
        {
            return firstValues.Cast<object?>().SequenceEqual(secondValues.Cast<object?>());
        }

        return Equals(first, second);
    }

    private void RebuildPropertyEditors()
    {
        PropertyEditors.Clear();
        if (SelectedRecord is null)
        {
            _propertyEditorsStale = false;
            SetRawJson("选择一条记录以查看 JSON。");
            return;
        }

        _propertyEditorsStale = !_arePropertyEditorsNeeded;
        if (_propertyEditorsStale)
        {
            RefreshRawJson();
            return;
        }

        var record = SelectedRecord;
        foreach (var property in _properties)
        {
            PropertyEditors.Add(new PropertyEditorViewModel(
                record.Item,
                property,
                (editor, oldValue, newValue) =>
                    RecordPropertyChanged(record, editor, oldValue, newValue),
                GetReferenceTargets(property),
                _navigateReference));
        }

        MarkValidationTarget();
        RefreshRawJson();
    }

    private IReadOnlyList<ConfigReferenceTarget> GetReferenceTargets(
        ConfigPropertyDefinition property) =>
        (property.Reference is null && property.StructuredString is null) || _referenceIndex is null
            ? []
            : _referenceIndex.GetTargets(
                property.Reference?.TargetAddress ?? property.StructuredString!.TargetAddress);

    internal IReadOnlyList<ConfigReferenceTarget> GetReferenceTargets(string configKey) =>
        _referenceIndex?.GetTargets(configKey) ?? [];

    internal IReadOnlyList<ConfigReferenceTarget> GetReferenceTargets(ConfigAddress address) =>
        _referenceIndex?.GetTargets(address) ?? [];

    private void RecordPropertyChanged(
        ConfigRecordViewModel record,
        PropertyEditorViewModel editor,
        object? oldValue,
        object? newValue)
    {
        RefreshEditedRecord(record);
        RecordEdit(new DelegateUndoableEdit(
            $"修改 {editor.DisplayName}",
            () =>
            {
                editor.ApplyHistoryValue(oldValue);
                RefreshEditedRecord(record);
            },
            () =>
            {
                editor.ApplyHistoryValue(newValue);
                RefreshEditedRecord(record);
            }),
            $"已修改 {editor.DisplayName}");
    }

    private void RefreshRawJson()
    {
        if (SelectedRecord is null)
        {
            SetRawJson("选择一条记录以查看 JSON。");
            return;
        }

        try
        {
            SetRawJson(JsonSerializer.Serialize(SelectedRecord.Item, SelectedRecord.Item.GetType(), JsonOptions));
        }
        catch (Exception exception)
        {
            SetRawJson($"无法生成 JSON 预览：{exception.GetBaseException().Message}");
        }
    }

    private void SetRawJson(string value)
    {
        _rawJson = value;
        OnPropertyChanged(nameof(RawJson));
    }

    private void RefreshEditedRecord(ConfigRecordViewModel record)
    {
        record.Refresh();
        if (ReferenceEquals(SelectedRecord, record))
        {
            foreach (var editor in PropertyEditors.Where(editor => editor.Definition.ValueGetter is not null))
            {
                editor.RefreshValue();
            }
            RefreshRawJson();
        }
    }

    private void RemoveRecords(
        IEnumerable<ConfigRecordViewModel> records,
        ConfigRecordViewModel? selection)
    {
        foreach (var record in records.ToArray())
        {
            Document.Items.Remove(record.Item);
            Records.Remove(record);
        }

        FinishRecordMutation(selection);
    }

    private void InsertRecords(
        IEnumerable<(int Index, ConfigRecordViewModel Record)> records,
        ConfigRecordViewModel? selection)
    {
        foreach (var (index, record) in records.OrderBy(item => item.Index))
        {
            var insertionIndex = Math.Min(index, Records.Count);
            Document.Items.Insert(insertionIndex, record.Item);
            Records.Insert(insertionIndex, record);
        }

        FinishRecordMutation(selection);
    }

    private void FinishRecordMutation(ConfigRecordViewModel? selection)
    {
        if (!CanInitializeEntry)
        {
            Document.EntryState = ItemCount == 0
                ? ArchiveEntryState.Empty
                : ArchiveEntryState.Populated;
        }

        _selectedRecords.Clear();
        SelectedRecord = null;
        ApplyFilter();
        SelectedRecord = selection is not null && FilteredRecords.Contains(selection)
            ? selection
            : FilteredRecords.FirstOrDefault();
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(FilterSummary));
        RaiseEntryStateChanged();
    }

    private void ReplaceItems(
        IReadOnlyList<object> items,
        ArchiveEntryState? explicitEntryState = null)
    {
        Document.Items.Clear();
        Document.NullRecordIndices.Clear();
        Records.Clear();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item is null)
            {
                Document.NullRecordIndices.Add(index);
                continue;
            }

            Document.Items.Add(item);
            Records.Add(CreateRecordViewModel(item));
        }

        Document.EntryState = explicitEntryState ?? (items.Count == 0
            ? ArchiveEntryState.Empty
            : ArchiveEntryState.Populated);
        FinishRecordMutation(Records.FirstOrDefault());
    }

    private object[] GetItemsIncludingNullRecords()
    {
        if (!HasNullRecords)
        {
            return Document.Items.ToArray();
        }

        var nullIndices = Document.NullRecordIndices.ToHashSet();
        var result = new object[Document.Items.Count + nullIndices.Count];
        var itemIndex = 0;
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = nullIndices.Contains(index)
                ? null!
                : Document.Items[itemIndex++];
        }

        return result;
    }

    private void NormalizeNullRecords()
    {
        for (var index = Document.Items.Count - 1; index >= 0; index--)
        {
            if (Document.Items[index] is not null)
            {
                continue;
            }

            if (!Document.NullRecordIndices.Contains(index))
            {
                Document.NullRecordIndices.Add(index);
            }

            Document.Items.RemoveAt(index);
        }

        if (Document.NullRecordIndices.Count > 1)
        {
            var ordered = Document.NullRecordIndices.Distinct().OrderBy(index => index).ToArray();
            Document.NullRecordIndices.Clear();
            foreach (var index in ordered)
            {
                Document.NullRecordIndices.Add(index);
            }
        }
    }

    private void InitializeEntry()
    {
        if (!CanInitializeEntry)
        {
            return;
        }

        var previousState = Document.EntryState;
        SetEntryState(ArchiveEntryState.Empty);
        RecordEdit(new DelegateUndoableEdit(
            $"初始化 {DisplayName} 列表",
            () => SetEntryState(previousState),
            () => SetEntryState(ArchiveEntryState.Empty)),
            $"已初始化 {DisplayName} 为空列表");
    }

    private void SetEntryState(ArchiveEntryState state)
    {
        Document.EntryState = state;
        RaiseEntryStateChanged();
    }

    private void RaiseEntryStateChanged()
    {
        OnPropertyChanged(nameof(HasNullRecords));
        OnPropertyChanged(nameof(CanInitializeEntry));
        OnPropertyChanged(nameof(HasEntryWarning));
        OnPropertyChanged(nameof(EntryStateMessage));
        ((RelayCommand)InitializeEntryCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)DeleteCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)CutCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PasteCommand).RaiseCanExecuteChanged();
    }

    private void RecordEdit(IUndoableEdit edit, string message)
    {
        _history.Record(edit);
        SetNotification(message);
    }

    private ConfigRecordViewModel CreateRecordViewModel(object item) =>
        new(item, _properties, GetReferenceTargets);

    private void Undo()
    {
        var description = _history.UndoDescription;
        _history.Undo();
        if (description is not null)
        {
            SetNotification($"已撤销：{description}");
        }
    }

    private void Redo()
    {
        var description = _history.RedoDescription;
        _history.Redo();
        if (description is not null)
        {
            SetNotification($"已重做：{description}");
        }
    }

    private void HistoryChanged(object? sender, EventArgs eventArgs)
    {
        ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
        UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        Document.IsDirty = _wasDirtyBeforeHistory || _history.Position != _savedHistoryPosition ||
            _linkedEditOffset != 0;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyMarker));
        OnPropertyChanged(nameof(DisplayLabel));
    }

    private void SetNotification(string message)
    {
        _notificationMessage = message;
        OnPropertyChanged(nameof(NotificationMessage));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseSelectionStateChanged()
    {
        ((RelayCommand)CopyCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)DeleteCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)CutCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyToClipboardCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyBatchEditCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasMultipleSelection));
    }

    private void ClipboardChanged(object? sender, EventArgs eventArgs) =>
        ((RelayCommand)PasteCommand).RaiseCanExecuteChanged();

    private sealed record LinkedRecordsChange(Action Undo, Action Redo, string Message)
    {
        public static LinkedRecordsChange None { get; } = new(() => { }, () => { }, string.Empty);
    }

    private sealed record PreparedPropertyChange(
        ConfigRecordViewModel Record,
        System.Reflection.PropertyInfo Property,
        object? OldValue,
        object? NewValue);

    private static bool IsBatchEditable(ConfigPropertyDefinition definition)
    {
        // IDs are assigned when records are added and are never edited.
        if (!definition.CanWrite || definition.Name == "Id")
        {
            return false;
        }

        var type = Nullable.GetUnderlyingType(definition.PropertyType) ?? definition.PropertyType;
        return type == typeof(string) || type == typeof(decimal) || type.IsPrimitive || type.IsEnum;
    }

    private static object? ConvertBatchValue(string text, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType == typeof(string))
        {
            return text;
        }

        if (string.IsNullOrWhiteSpace(text) && Nullable.GetUnderlyingType(targetType) is not null)
        {
            return null;
        }

        if (underlyingType.IsEnum)
        {
            return Enum.Parse(underlyingType, text, true);
        }

        if (underlyingType == typeof(bool))
        {
            return bool.Parse(text);
        }

        return Convert.ChangeType(text, underlyingType, CultureInfo.CurrentCulture);
    }

    private bool CanCreateRecord() =>
        CanMutateRecordStructure &&
        Document.Definition.ItemType.GetConstructor(Type.EmptyTypes) is not null;

    private static bool MatchesFilter(object item, ConfigPropertyDefinition property, string query)
    {
        var value = property.ValueGetter is { } getter
            ? getter(item)
            : item.GetType().GetProperty(property.Name)?.GetValue(item);
        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (IsIntegerType(valueType))
        {
            return MatchesInteger(value, valueType, query);
        }

        if (value is IEnumerable values and not string)
        {
            var elementType = GetCollectionElementType(property.PropertyType);
            if (elementType is not null && IsIntegerType(elementType))
            {
                return values.Cast<object?>().Any(element => MatchesInteger(element, elementType, query));
            }

            return values.Cast<object?>().Any(element =>
                element?.ToString()?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);
        }

        return value?.ToString()?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private static bool MatchesInteger(object? value, Type integerType, string query)
    {
        if (value is null)
        {
            return false;
        }

        try
        {
            var expected = Convert.ChangeType(query, integerType, CultureInfo.CurrentCulture);
            return Equals(value, expected);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    public bool NavigateToFilteredId(int itemId)
    {
        var idFilter = FilterFields.FirstOrDefault(field =>
            string.Equals(field.Property?.Name, "Id", StringComparison.OrdinalIgnoreCase));
        if (idFilter is null)
        {
            return false;
        }

        SelectedFilterField = idFilter;
        SearchText = itemId.ToString(CultureInfo.CurrentCulture);
        var record = FilteredRecords.FirstOrDefault(candidate =>
            candidate.Item.GetType().GetProperty("Id")?.GetValue(candidate.Item) is int candidateId &&
            candidateId == itemId);
        if (record is null)
        {
            return false;
        }

        SelectedRecord = record;
        IsSpecializedEditorActive = false;
        return true;
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var enumerableType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerableType is null
            ? null
            : Nullable.GetUnderlyingType(enumerableType.GetGenericArguments()[0]) ??
              enumerableType.GetGenericArguments()[0];
    }

    private static bool IsIntegerType(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong);

    private static bool IsSearchable(Type type) =>
        type == typeof(string) || type.IsPrimitive || type.IsEnum ||
        (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string));
}

public sealed record ConfigFilterFieldViewModel(
    string DisplayName,
    ConfigPropertyDefinition? Property);

public sealed record BatchEditFieldViewModel(
    string DisplayName,
    ConfigPropertyDefinition Property);
