using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

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
    private string _searchText = string.Empty;
    private ConfigFilterFieldViewModel _selectedFilterField;
    private ConfigRecordViewModel? _selectedRecord;
    private string _rawJson = "选择一条记录以查看 JSON。";
    private string? _notificationMessage;

    public ConfigDocumentViewModel(
        ConfigDocument document,
        IConfigMetadataProvider metadataProvider,
        Action<ConfigDocumentViewModel> selectDocument)
    {
        Document = document;
        _properties = metadataProvider.GetProperties(document.Definition.ItemType);
        FilterFields = [
            new ConfigFilterFieldViewModel("全部字段", null),
            .. _properties
                .Where(property => IsSearchable(property.PropertyType))
                .Select(property => new ConfigFilterFieldViewModel(property.DisplayName, property))
        ];
        _selectedFilterField = FilterFields[0];

        foreach (var item in document.Items)
        {
            Records.Add(new ConfigRecordViewModel(item, _properties));
        }

        SelectCommand = new RelayCommand(() => selectDocument(this));
        AddCommand = new RelayCommand(AddRecord, CanCreateRecord);
        CopyCommand = new RelayCommand(CopySelectedRecords, () => SelectedRecord is not null);
        DeleteCommand = new RelayCommand(DeleteSelectedRecords, () => SelectedRecord is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);
        ApplyFilter();
    }

    public event EventHandler? StateChanged;

    public ConfigDocument Document { get; }
    public string DisplayName => Document.Definition.DisplayName;
    public string EntryName => Document.Definition.EntryName;
    public IReadOnlyList<ConfigPropertyDefinition> Properties => _properties;
    public IReadOnlyList<ConfigFilterFieldViewModel> FilterFields { get; }
    public ObservableCollection<ConfigRecordViewModel> Records { get; } = [];
    public ObservableCollection<ConfigRecordViewModel> FilteredRecords { get; } = [];
    public ObservableCollection<PropertyEditorViewModel> PropertyEditors { get; } = [];
    public ICommand SelectCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public int ItemCount => Records.Count;
    public int VisibleItemCount => FilteredRecords.Count;
    public bool IsDirty => Document.IsDirty;
    public string DirtyMarker => IsDirty ? " ●" : string.Empty;
    public string DisplayLabel => DisplayName + DirtyMarker;
    public string RawJson => _rawJson;
    public string? NotificationMessage => _notificationMessage;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
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

                RebuildPropertyEditors();
                ((RelayCommand)CopyCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectionSummary));
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
        if (_selectedRecords.Count > 0 && !_selectedRecords.Contains(SelectedRecord!))
        {
            SelectedRecord = _selectedRecords[0];
        }

        OnPropertyChanged(nameof(SelectionSummary));
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
        OnPropertyChanged(nameof(SelectionSummary));
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
            MarkDirty("已新增记录");
            ApplyFilter();
            SelectedRecord = record;
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

        ConfigRecordViewModel? lastCopy = null;
        try
        {
            foreach (var source in sourceRecords)
            {
                var copy = CloneRecord(source.Item);
                Document.Items.Add(copy);
                lastCopy = new ConfigRecordViewModel(copy, _properties);
                Records.Add(lastCopy);
            }

            if (lastCopy is not null)
            {
                MarkDirty($"已复制 {sourceRecords.Length} 条记录");
                ApplyFilter();
                SelectedRecord = lastCopy;
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

        foreach (var record in records)
        {
            Document.Items.Remove(record.Item);
            Records.Remove(record);
        }

        SelectedRecord = null;
        _selectedRecords.Clear();
        MarkDirty($"已删除 {records.Length} 条记录");
        ApplyFilter();
        SelectedRecord = FilteredRecords.FirstOrDefault();
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

    private void RebuildPropertyEditors()
    {
        PropertyEditors.Clear();
        if (SelectedRecord is null)
        {
            SetRawJson("选择一条记录以查看 JSON。");
            return;
        }

        foreach (var property in _properties)
        {
            PropertyEditors.Add(new PropertyEditorViewModel(SelectedRecord.Item, property, RecordPropertyChanged));
        }

        RefreshRawJson();
    }

    private void RecordPropertyChanged()
    {
        SelectedRecord?.Refresh();
        MarkDirty("记录已修改");
        RefreshRawJson();
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

    private void MarkDirty(string message)
    {
        Document.IsDirty = true;
        SetNotification(message);
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyMarker));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(FilterSummary));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetNotification(string message)
    {
        _notificationMessage = message;
        OnPropertyChanged(nameof(NotificationMessage));
        StateChanged?.Invoke(this, EventArgs.Empty);
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
