using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Edits DiplomaticRelations.json one faction at a time: the selected faction's relation and truce with
/// every other faction. A pair is stored once in either order; editing a missing pair creates its record.
/// </summary>
public sealed class DiplomacyEditorViewModel : ObservableObject, IDisposable
{
    // GlobalVariables.FriendlyDiplomacyThreshold defaults to 300: at or above it the two factions are allied.
    public const int AllianceThreshold = 300;

    private readonly ConfigEditorContext _context;
    private DiplomacyFactionViewModel? _selectedFaction;
    private string _searchText = string.Empty;
    private bool _hideNeutral;
    private DiplomacyRelationRowViewModel? _selectedRow;
    private bool _synchronizing;

    public DiplomacyEditorViewModel(ConfigEditorContext context)
    {
        _context = context;
        _context.Document.ReferenceOptionsChanged += OnReferenceOptionsChanged;
        _context.Document.StateChanged += OnDocumentStateChanged;
        _context.Document.PropertyChanged += OnDocumentPropertyChanged;
        RebuildFactions();
    }

    public ObservableCollection<DiplomacyFactionViewModel> Factions { get; } = [];
    public ObservableCollection<DiplomacyFactionViewModel> FilteredFactions { get; } = [];
    public ObservableCollection<DiplomacyRelationRowViewModel> Rows { get; } = [];
    public bool HasSelection => SelectedFaction is not null;
    public string SelectedTitle => SelectedFaction is { } faction ? $"{faction.Name} 的外交关系" : "请选择势力";
    public string SelectedId => SelectedFaction is null ? "" : $"ID {SelectedFaction.Id}";
    public string CountText => $"{FilteredFactions.Count} / {Factions.Count} 个势力 · {_context.Records.Count} 条关系";
    public string RowStatus => SelectedFaction is null
        ? ""
        : $"同盟 {Rows.Count(row => row.IsAlliance)} · 敌对 {Rows.Count(row => row.Relation < 0)} · 停战 {Rows.Count(row => row.Truce > 0)} · 未建立记录 {Rows.Count(row => !row.HasRecord)}";
    public bool CanEdit => !_context.Document.HasNullRecords;
    public string Hint => CanEdit
        ? $"关系值 ≥ {AllianceThreshold} 为同盟（GlobalVariables.FriendlyDiplomacyThreshold），小于 0 为敌对；停战天数每天减 1，大于 0 时不会互相攻击。没有记录的势力对按 0 处理，修改后自动新建记录。"
        : "外交关系包含 null 记录，请先在通用编辑器中处理。";

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    // Selecting a row selects its record in the document, so switching to the generic table shows it.
    public DiplomacyRelationRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value)) value?.Focus();
        }
    }

    public bool HideNeutral
    {
        get => _hideNeutral;
        set { if (SetProperty(ref _hideNeutral, value)) RebuildRows(); }
    }

    public DiplomacyFactionViewModel? SelectedFaction
    {
        get => _selectedFaction;
        set
        {
            if (!SetProperty(ref _selectedFaction, value)) return;
            RebuildRows();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedId));
        }
    }

    internal ConfigRecordViewModel[] FindRecords(int first, int second) =>
        _context.Records.Where(record => record.Item is DiplomaticRelationConfig relation &&
            (relation.RelationFaction1ID == first && relation.RelationFaction2ID == second ||
             relation.RelationFaction1ID == second && relation.RelationFaction2ID == first)).ToArray();

    internal void Select(ConfigRecordViewModel? record)
    {
        if (record is null) return;
        _synchronizing = true;
        _context.SelectedRecord = record;
        _synchronizing = false;
    }

    internal void SetValue(DiplomacyRelationRowViewModel row, string propertyName, int value, string description)
    {
        if (!CanEdit || SelectedFaction is not { } faction) return;
        if (FindRecords(faction.Id, row.OtherId).FirstOrDefault() is { } record)
        {
            _context.SetPropertyValue(record, propertyName, value, description);
        }
        else
        {
            if (_context.Document.CanInitializeEntry)
                _context.Document.InitializeEntryCommand.Execute(null);
            // The game's own files list the higher faction ID first.
            var values = new Dictionary<string, object?>
            {
                [nameof(DiplomaticRelationConfig.RelationFaction1ID)] = Math.Max(faction.Id, row.OtherId),
                [nameof(DiplomaticRelationConfig.RelationFaction2ID)] = Math.Min(faction.Id, row.OtherId),
                [nameof(DiplomaticRelationConfig.Relation)] = 0,
                [nameof(DiplomaticRelationConfig.Truce)] = 0,
                [propertyName] = value
            };
            _context.AddRecord(values, $"新增 {faction.Name} 与 {row.OtherName} 的外交关系");
        }
    }

    private void RebuildFactions()
    {
        var selectedId = SelectedFaction?.Id;
        var names = _context.GetReferenceTargets(new ConfigAddress(ConfigScope.Scenario, "factions"))
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.First().DisplayName);
        // Factions only named in relations (e.g. a deleted faction) are still listed so their rows can be fixed.
        var ids = names.Keys.Concat(_context.Records.Select(record => record.Item)
                .OfType<DiplomaticRelationConfig>()
                .SelectMany(relation => new[] { relation.RelationFaction1ID, relation.RelationFaction2ID }))
            .Distinct().Order()
            .Select(id => new DiplomacyFactionViewModel(id, names.TryGetValue(id, out var name) ? name : "（未知势力）",
                names.ContainsKey(id)))
            .ToArray();
        // The main window refreshes reference options after every edit. Rebuilding the lists then would replace
        // the row being typed into (losing focus after one character), so unchanged factions only refresh rows.
        if (ids.Select(faction => (faction.Id, faction.Name, faction.Exists))
            .SequenceEqual(Factions.Select(faction => (faction.Id, faction.Name, faction.Exists))))
        {
            RefreshRows();
            return;
        }

        Factions.Clear();
        foreach (var faction in ids) Factions.Add(faction);
        ApplyFilter();
        var selection = Factions.FirstOrDefault(faction => faction.Id == selectedId) ?? FindFactionOfSelectedRecord()
            ?? Factions.FirstOrDefault();
        if (ReferenceEquals(selection, SelectedFaction)) RebuildRows();
        else SelectedFaction = selection;
    }

    private DiplomacyFactionViewModel? FindFactionOfSelectedRecord() =>
        _context.SelectedRecord?.Item is DiplomaticRelationConfig relation
            ? Factions.FirstOrDefault(faction => faction.Id == relation.RelationFaction1ID)
            : null;

    private void RebuildRows()
    {
        Rows.Clear();
        if (SelectedFaction is { } faction)
        {
            foreach (var other in Factions.Where(other => other.Id != faction.Id))
            {
                var row = new DiplomacyRelationRowViewModel(other.Id, other.Name, this);
                // Same rule as the 状态 column: hide 中立 (0 to the alliance threshold) unless under truce.
                if (!HideNeutral || row.IsAlliance || row.IsHostile || row.IsDuplicated || row.Truce > 0)
                    Rows.Add(row);
            }
        }
        OnPropertyChanged(nameof(RowStatus));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Hint));
    }

    internal int CurrentFactionId => SelectedFaction?.Id ?? -1;

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredFactions.Clear();
        foreach (var faction in Factions.Where(faction => query.Length == 0 ||
                     faction.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     faction.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)))
            FilteredFactions.Add(faction);
        OnPropertyChanged(nameof(CountText));
    }

    private void OnReferenceOptionsChanged(object? sender, EventArgs e) => RebuildFactions();

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        var known = Factions.Select(faction => faction.Id).ToHashSet();
        if (_context.Records.Select(record => record.Item).OfType<DiplomaticRelationConfig>()
            .Any(relation => !known.Contains(relation.RelationFaction1ID) || !known.Contains(relation.RelationFaction2ID)))
        {
            RebuildFactions();
            return;
        }
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var row in Rows) row.Refresh();
        OnPropertyChanged(nameof(RowStatus));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Hint));
    }

    // Selecting a relation in the generic table shows its first faction here.
    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConfigDocumentViewModel.SelectedRecord) || _synchronizing) return;
        if (_context.SelectedRecord?.Item is DiplomaticRelationConfig relation &&
            relation.RelationFaction1ID != CurrentFactionId && relation.RelationFaction2ID != CurrentFactionId &&
            FindFactionOfSelectedRecord() is { } faction)
            SelectedFaction = faction;
    }

    public void Dispose()
    {
        _context.Document.ReferenceOptionsChanged -= OnReferenceOptionsChanged;
        _context.Document.StateChanged -= OnDocumentStateChanged;
        _context.Document.PropertyChanged -= OnDocumentPropertyChanged;
    }
}

public sealed class DiplomacyFactionViewModel(int id, string name, bool exists)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public bool Exists { get; } = exists;
    public string Label => $"{Name}  ·  {Id}";
}

public sealed class DiplomacyRelationRowViewModel : ObservableObject
{
    private readonly DiplomacyEditorViewModel _owner;
    private DiplomaticRelationConfig[] _relations = [];

    public DiplomacyRelationRowViewModel(int otherId, string otherName, DiplomacyEditorViewModel owner)
    {
        OtherId = otherId;
        OtherName = otherName;
        _owner = owner;
        LoadRelations();
    }

    public int OtherId { get; }
    public string OtherName { get; }
    public string OtherLabel => $"{OtherId} {OtherName}";
    private DiplomaticRelationConfig[] Relations => _relations;
    public bool HasRecord => Relations.Length > 0;
    public bool IsDuplicated => Relations.Length > 1;
    public int Relation => Relations.FirstOrDefault()?.Relation ?? 0;
    public int Truce => Relations.FirstOrDefault()?.Truce ?? 0;
    public bool IsAlliance => Relation >= DiplomacyEditorViewModel.AllianceThreshold;
    public bool IsHostile => Relation < 0;
    public string State => IsDuplicated ? "重复记录" : !HasRecord ? "无记录" : IsAlliance ? "同盟" : IsHostile ? "敌对" : "中立";

    public decimal? RelationValue
    {
        get => Relation;
        set
        {
            if (value is { } number && number == decimal.Truncate(number) && number >= int.MinValue &&
                number <= int.MaxValue && (int)number != Relation)
                _owner.SetValue(this, nameof(DiplomaticRelationConfig.Relation), (int)number, $"修改与 {OtherName} 的关系值");
        }
    }

    public decimal? TruceValue
    {
        get => Truce;
        set
        {
            if (value is { } number && number == decimal.Truncate(number) && number >= int.MinValue &&
                number <= int.MaxValue && (int)number != Truce)
                _owner.SetValue(this, nameof(DiplomaticRelationConfig.Truce), (int)number, $"修改与 {OtherName} 的停战天数");
        }
    }

    internal void Focus() => _owner.Select(_owner.FindRecords(_owner.CurrentFactionId, OtherId).FirstOrDefault());

    private void LoadRelations() => _relations = _owner.FindRecords(_owner.CurrentFactionId, OtherId)
        .Select(record => (DiplomaticRelationConfig)record.Item).ToArray();

    public void Refresh()
    {
        LoadRelations();
        OnPropertyChanged(nameof(HasRecord));
        OnPropertyChanged(nameof(IsDuplicated));
        OnPropertyChanged(nameof(RelationValue));
        OnPropertyChanged(nameof(TruceValue));
        OnPropertyChanged(nameof(IsAlliance));
        OnPropertyChanged(nameof(IsHostile));
        OnPropertyChanged(nameof(State));
    }
}
