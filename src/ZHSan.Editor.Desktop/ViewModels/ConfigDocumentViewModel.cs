using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Editing;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ConfigDocumentViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly IReadOnlyList<ConfigPropertyDefinition> _properties;
    private readonly List<ConfigRecordViewModel> _selectedRecords = [];
    private readonly RecordClipboard _clipboard;
    private readonly DocumentUiState _uiState;
    private readonly UndoRedoHistory _history = new();
    private readonly ConfigReferenceIndex? _referenceIndex;
    private bool _wasDirtyBeforeHistory;
    private int _savedHistoryPosition;
    private string _searchText = string.Empty;
    private ConfigFilterFieldViewModel _selectedFilterField;
    private BatchEditFieldViewModel? _selectedBatchField;
    private string _batchValueText = string.Empty;
    private ConfigRecordViewModel? _selectedRecord;
    private string _rawJson = "选择一条记录以查看 JSON。";
    private string? _notificationMessage;

    public ConfigDocumentViewModel(
        ConfigDocument document,
        IConfigMetadataProvider metadataProvider,
        Action<ConfigDocumentViewModel> selectDocument,
        RecordClipboard? clipboard = null,
        DocumentUiState? uiState = null,
        ConfigReferenceIndex? referenceIndex = null)
    {
        Document = document;
        _clipboard = clipboard ?? new RecordClipboard();
        _uiState = uiState ?? new DocumentUiState();
        _referenceIndex = referenceIndex;
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

        foreach (var item in document.Items)
        {
            Records.Add(new ConfigRecordViewModel(item, _properties));
        }

        SelectCommand = new RelayCommand(() => selectDocument(this));
        AddCommand = new RelayCommand(AddRecord, CanCreateRecord);
        CopyCommand = new RelayCommand(CopySelectedRecords, () => SelectedRecord is not null);
        DeleteCommand = new RelayCommand(DeleteSelectedRecords, () => SelectedRecord is not null);
        CutCommand = new RelayCommand(CutSelectedRecords, () => SelectedRecord is not null);
        CopyToClipboardCommand = new RelayCommand(CopySelectionToClipboard, () => SelectedRecord is not null);
        PasteCommand = new RelayCommand(PasteRecords, CanPasteRecords);
        ApplyBatchEditCommand = new RelayCommand(ApplyBatchEdit, CanApplyBatchEdit);
        UndoCommand = new RelayCommand(Undo, () => _history.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _history.CanRedo);
        _history.Changed += HistoryChanged;
        _clipboard.Changed += ClipboardChanged;
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);
        ApplyFilter();
    }

    public event EventHandler? StateChanged;

    public ConfigDocument Document { get; }
    public string Key => Document.Definition.Key;
    public string DisplayName => Document.Definition.DisplayName;
    public string EntryName => Document.Definition.EntryName;
    public IReadOnlyList<ConfigPropertyDefinition> Properties => _properties;
    public IReadOnlyList<ConfigFilterFieldViewModel> FilterFields { get; }
    public IReadOnlyList<BatchEditFieldViewModel> BatchEditFields { get; }
    public ObservableCollection<ConfigRecordViewModel> Records { get; } = [];
    public ObservableCollection<ConfigRecordViewModel> FilteredRecords { get; } = [];
    public ObservableCollection<PropertyEditorViewModel> PropertyEditors { get; } = [];
    public ICommand SelectCommand { get; }
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

    public int ItemCount => Records.Count;
    public int VisibleItemCount => FilteredRecords.Count;
    public bool HasMultipleSelection => _selectedRecords.Count > 1;
    public bool IsDirty => Document.IsDirty;
    public string DirtyMarker => IsDirty ? " ●" : string.Empty;
    public string DisplayLabel => DisplayName + DirtyMarker;
    public string RawJson => _rawJson;
    public string? NotificationMessage => _notificationMessage;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
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
        foreach (var editor in PropertyEditors.Where(editor => editor.IsReference))
        {
            editor.ReloadReferenceOptions(GetReferenceTargets(editor.Definition));
        }
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

    public void MarkSaved()
    {
        _wasDirtyBeforeHistory = false;
        _savedHistoryPosition = _history.Position;
        Document.IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyMarker));
        OnPropertyChanged(nameof(DisplayLabel));
        StateChanged?.Invoke(this, EventArgs.Empty);
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
            if (query.Length == 0 || properties.Any(property => ContainsText(record.Item, property, query)))
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
            var item = Activator.CreateInstance(Document.Definition.ItemType)
                ?? throw new InvalidOperationException("无法创建记录实例。");
            Document.Items.Add(item);
            var record = new ConfigRecordViewModel(item, _properties);
            Records.Add(record);
            FinishRecordMutation(record);
            var index = Records.IndexOf(record);
            RecordEdit(new DelegateUndoableEdit(
                "新增记录",
                () => RemoveRecords([record], null),
                () => InsertRecords([(index, record)], record)),
                "已新增记录");
        }
        catch (Exception exception)
        {
            SetNotification($"新增失败：{exception.GetBaseException().Message}");
        }
    }

    private void CopySelectedRecords()
    {
        var sourceRecords = _selectedRecords.Count > 0
            ? _selectedRecords.ToArray()
            : SelectedRecord is null ? [] : [SelectedRecord];

        try
        {
            var copies = new List<(int Index, ConfigRecordViewModel Record)>();
            foreach (var source in sourceRecords)
            {
                var copy = CloneRecord(source.Item);
                Document.Items.Add(copy);
                var copyRecord = new ConfigRecordViewModel(copy, _properties);
                Records.Add(copyRecord);
                copies.Add((Records.Count - 1, copyRecord));
            }

            if (copies.Count > 0)
            {
                var lastCopy = copies[^1].Record;
                FinishRecordMutation(lastCopy);
                RecordEdit(new DelegateUndoableEdit(
                    $"复制 {copies.Count} 条记录",
                    () => RemoveRecords(copies.Select(copy => copy.Record), null),
                    () => InsertRecords(copies, lastCopy)),
                    $"已复制 {copies.Count} 条记录");
            }
        }
        catch (Exception exception)
        {
            SetNotification($"复制失败：{exception.GetBaseException().Message}");
        }
    }

    private void DeleteSelectedRecords()
    {
        var records = _selectedRecords.Count > 0
            ? _selectedRecords.ToArray()
            : SelectedRecord is null ? [] : [SelectedRecord];
        if (records.Length == 0)
        {
            return;
        }

        var removed = records
            .Select(record => (Index: Records.IndexOf(record), Record: record))
            .OrderBy(item => item.Index)
            .ToArray();
        var selectionAfterDelete = Records.FirstOrDefault(record => !records.Contains(record));
        RemoveRecords(records, selectionAfterDelete);
        RecordEdit(new DelegateUndoableEdit(
            $"删除 {records.Length} 条记录",
            () => InsertRecords(removed, removed[0].Record),
            () => RemoveRecords(records, selectionAfterDelete)),
            $"已删除 {records.Length} 条记录");
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

    private void CutSelectedRecords()
    {
        var records = GetSelectedRecords();
        if (records.Length == 0)
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
        RecordEdit(new DelegateUndoableEdit(
            $"剪切 {records.Length} 条记录",
            () => InsertRecords(removed, removed[0].Record),
            () => RemoveRecords(records, selectionAfterCut)),
            $"已剪切 {records.Length} 条记录");
    }

    private bool CanPasteRecords() => _clipboard.Contains(Document.Definition.ItemType);

    private void PasteRecords()
    {
        if (!CanPasteRecords())
        {
            return;
        }

        try
        {
            var pasted = _clipboard.Items
                .Select(CloneRecord)
                .Select(item => new ConfigRecordViewModel(item, _properties))
                .ToArray();
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
            RecordEdit(new DelegateUndoableEdit(
                $"粘贴 {pasted.Length} 条记录",
                () => RemoveRecords(pasted, null),
                () => InsertRecords(insertions, lastRecord)),
                $"已粘贴 {pasted.Length} 条记录");
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

    private void RebuildPropertyEditors()
    {
        PropertyEditors.Clear();
        if (SelectedRecord is null)
        {
            SetRawJson("选择一条记录以查看 JSON。");
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
                GetReferenceTargets(property)));
        }

        RefreshRawJson();
    }

    private IReadOnlyList<ConfigReferenceTarget> GetReferenceTargets(
        ConfigPropertyDefinition property) =>
        property.Reference is null || _referenceIndex is null
            ? []
            : _referenceIndex.GetTargets(property.Reference.TargetConfigKey);

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
        _selectedRecords.Clear();
        SelectedRecord = null;
        ApplyFilter();
        SelectedRecord = selection is not null && FilteredRecords.Contains(selection)
            ? selection
            : FilteredRecords.FirstOrDefault();
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(FilterSummary));
    }

    private void RecordEdit(IUndoableEdit edit, string message)
    {
        _history.Record(edit);
        SetNotification(message);
    }

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
        Document.IsDirty = _wasDirtyBeforeHistory || _history.Position != _savedHistoryPosition;
        ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
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
        ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CutCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyToClipboardCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyBatchEditCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasMultipleSelection));
    }

    private void ClipboardChanged(object? sender, EventArgs eventArgs) =>
        ((RelayCommand)PasteCommand).RaiseCanExecuteChanged();

    private static bool IsBatchEditable(ConfigPropertyDefinition definition)
    {
        if (!definition.CanWrite)
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

    private bool CanCreateRecord() => Document.Definition.ItemType.GetConstructor(Type.EmptyTypes) is not null;

    private static bool ContainsText(object item, ConfigPropertyDefinition property, string query)
    {
        var value = item.GetType().GetProperty(property.Name)?.GetValue(item);
        if (value is IEnumerable values and not string)
        {
            return values.Cast<object?>().Any(element =>
                element?.ToString()?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);
        }

        return value?.ToString()?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
    }

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
