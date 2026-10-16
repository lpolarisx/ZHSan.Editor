using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Shared shell of the record editors laid out like the person editor: a searchable record list on the
/// left, labelled field groups and ID chip lists for the selected record on the right. Derived editors
/// build their groups in the constructor and then call <see cref="Initialize"/>.
/// </summary>
public abstract class RecordListEditorViewModel<TConfig> : ObservableObject, IIdChipListOwner, IDisposable
    where TConfig : class
{
    private readonly string _subject;
    private readonly string _unit;
    private RecordOptionViewModel? _selectedRecord;
    private string _searchText = string.Empty;
    private bool _synchronizing;

    protected RecordListEditorViewModel(ConfigEditorContext context, string subject, string unit)
    {
        Context = context;
        _subject = subject;
        _unit = unit;
    }

    protected ConfigEditorContext Context { get; }
    public ObservableCollection<RecordOptionViewModel> Records { get; } = [];
    public ObservableCollection<RecordOptionViewModel> FilteredRecords { get; } = [];
    public IReadOnlyList<PersonFieldGroupViewModel> Groups { get; protected init; } = [];
    public IReadOnlyList<PersonIdChipListViewModel> ChipLists { get; protected init; } = [];
    public virtual IEnumerable<PersonBasicFieldViewModel> AllFields => Groups.SelectMany(group => group.Fields);
    public bool HasSelection => SelectedRecord is not null;
    public string SelectedTitle => SelectedRecord?.Name ?? $"请选择{_subject}";
    public string SelectedId => SelectedRecord is null ? "" : $"ID {SelectedRecord.Id}";
    public string CountText => $"{FilteredRecords.Count} / {Records.Count} {_unit}";
    protected TConfig? SelectedConfig => SelectedRecord?.Record.Item as TConfig;

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    public RecordOptionViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetProperty(ref _selectedRecord, value)) return;
            if (!_synchronizing) Context.SelectedRecord = value?.Record;
            RefreshSelection();
        }
    }

    protected abstract string GetName(TConfig config);

    protected void Initialize()
    {
        Context.Document.ReferenceOptionsChanged += OnReferenceOptionsChanged;
        Context.Document.StateChanged += OnDocumentStateChanged;
        Context.Document.PropertyChanged += OnDocumentPropertyChanged;
        RebuildRecords();
    }

    protected PersonFieldGroupViewModel Group(string title, params (string Label, string Property)[] fields) =>
        new(title, fields.Select(field => Field(field.Label, field.Property)).ToArray());

    protected PersonBasicFieldViewModel Field(string label, string propertyName) =>
        new(label, propertyName, () => SelectedRecord?.Record, Context, GetChoiceProvider(propertyName),
            itemType: typeof(TConfig), subject: _subject);

    protected virtual Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => null;

    protected PersonIdChipListViewModel ChipList(string label, string propertyName, string targetKey) =>
        new(label, propertyName, targetKey, this);

    // Scenario tables (architectures, persons, …) are looked up in the scenario first, then in CommonData.
    protected IReadOnlyList<ConfigReferenceTarget> Targets(string key)
    {
        var scenario = Context.GetReferenceTargets(new ConfigAddress(ConfigScope.Scenario, key));
        return scenario.Count > 0 ? scenario : Context.GetReferenceTargets(key);
    }

    protected IReadOnlyList<PersonChoiceOption> TargetOptions(string key, PersonChoiceOption? none = null)
    {
        var options = ReferenceOptions(Targets(key));
        return none is null ? [..options] : [none, ..options];
    }

    protected static IEnumerable<PersonChoiceOption> ReferenceOptions(IEnumerable<ConfigReferenceTarget> targets) =>
        targets.GroupBy(target => target.Id).Select(group => group.First()).OrderBy(target => target.Id)
            .Select(target => new PersonChoiceOption(target.Id, IdLabel(target)));

    protected static string IdLabel(ConfigReferenceTarget target) =>
        $"{target.Id.ToString(CultureInfo.InvariantCulture)} {target.DisplayName}";

    public object? GetValue(string propertyName) => SelectedConfig is { } config
        ? typeof(TConfig).GetProperty(propertyName)!.GetValue(config)
        : null;

    public void SetValue(string propertyName, object value, string description)
    {
        if (SelectedRecord is not { } selected) return;
        Context.SetPropertyValue(selected.Record, propertyName, value, description);
        foreach (var list in ChipLists) list.Refresh();
    }

    public IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey) => [..ReferenceOptions(Targets(targetKey))];

    public string GetChipLabel(string targetKey, int id) =>
        Targets(targetKey).FirstOrDefault(target => target.Id == id) is { } target
            ? IdLabel(target)
            : $"{id} （未收录）";

    protected virtual void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        foreach (var field in AllFields) field.Refresh();
        foreach (var list in ChipLists) list.Refresh();
    }

    protected virtual void RefreshSelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedId));
        foreach (var field in AllFields) field.Refresh();
        foreach (var group in Groups) group.Refresh();
        foreach (var list in ChipLists) list.Refresh();
    }

    private void RebuildRecords()
    {
        var selectedRecord = Context.SelectedRecord ?? SelectedRecord?.Record;
        Records.Clear();
        foreach (var record in Context.Records.Where(record => record.Item is TConfig))
            Records.Add(new RecordOptionViewModel(record, item => GetName((TConfig)item)));
        ApplyFilter();
        SelectWithoutDocumentUpdate(Records.FirstOrDefault(option => ReferenceEquals(option.Record, selectedRecord))
            ?? Records.FirstOrDefault());
        if (Context.SelectedRecord is null && SelectedRecord is not null)
            Context.SelectedRecord = SelectedRecord.Record;
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var matches = Records.Where(option => query.Length == 0 ||
            option.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            option.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal))
            .ToArray();
        for (var index = FilteredRecords.Count - 1; index >= 0; index--)
            if (!matches.Contains(FilteredRecords[index])) FilteredRecords.RemoveAt(index);
        for (var index = 0; index < matches.Length; index++)
        {
            if (index < FilteredRecords.Count && ReferenceEquals(FilteredRecords[index], matches[index]))
                continue;
            var previous = FilteredRecords.IndexOf(matches[index]);
            if (previous >= 0) FilteredRecords.Move(previous, index);
            else FilteredRecords.Insert(index, matches[index]);
        }
        OnPropertyChanged(nameof(CountText));
    }

    private void SelectWithoutDocumentUpdate(RecordOptionViewModel? option)
    {
        _synchronizing = true;
        SelectedRecord = option;
        _synchronizing = false;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigDocumentViewModel.SelectedRecord) && !_synchronizing)
            SelectWithoutDocumentUpdate(
                Records.FirstOrDefault(option => ReferenceEquals(option.Record, Context.SelectedRecord)));
    }

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        if (Records.Count != Context.Records.Count ||
            Records.Where((option, index) => !ReferenceEquals(option.Record, Context.Records[index])).Any())
        {
            RebuildRecords();
            return;
        }
        foreach (var option in Records) option.Refresh();
        ApplyFilter();
        RefreshSelection();
    }

    public virtual void Dispose()
    {
        Context.Document.ReferenceOptionsChanged -= OnReferenceOptionsChanged;
        Context.Document.StateChanged -= OnDocumentStateChanged;
        Context.Document.PropertyChanged -= OnDocumentPropertyChanged;
    }
}

public sealed class RecordOptionViewModel(ConfigRecordViewModel record, Func<object, string> name) : ObservableObject
{
    public ConfigRecordViewModel Record { get; } = record;
    public int Id => (int)(Record.Item.GetType().GetProperty("Id")?.GetValue(Record.Item) ?? 0);
    public string Name => name(Record.Item);
    public string Label => $"{Name}  ·  {Id}";
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Label));
    }
}
