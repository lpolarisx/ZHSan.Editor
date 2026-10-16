using System.Windows.Input;
using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Edits Regions.json. The game links each state to the region listing it (the last one wins), so a state is
/// kept in one region: adding it here removes it from its old region in the same undo step.
/// </summary>
public sealed class RegionEditorViewModel : RecordListEditorViewModel<RegionConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");

    public RegionEditorViewModel(ConfigEditorContext context) : base(context, "地区", "个")
    {
        Groups = [Group("基本", ("名称", nameof(RegionConfig.Name)), ("核心城市", nameof(RegionConfig.RegionCoreID)))
            .WithNote(GetNote)];
        ChipLists = [ChipList("所辖州", nameof(RegionConfig.StatesListString), "states")];
        Initialize();
    }

    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public PersonIdChipListViewModel States => ChipLists[0];
    public string ArchitectureSummary => SelectedConfig is { } region
        ? Territory.Describe(Territory.ArchitecturesIn(Targets("architectures"), Territory.Ids(region.StatesListString)))
        : "";

    protected override string GetName(RegionConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) =>
        propertyName == nameof(RegionConfig.RegionCoreID) ? () => TargetOptions("architectures", None) : null;

    public override void SetValue(string propertyName, object value, string description)
    {
        if (propertyName != nameof(RegionConfig.StatesListString) || SelectedRecord is not { } selected ||
            value is not string text)
        {
            base.SetValue(propertyName, value, description);
            return;
        }

        var added = Territory.Ids(text).Except(Territory.Ids(((RegionConfig)selected.Record.Item).StatesListString)).ToHashSet();
        var changes = new List<ConfigEditorPropertyChange> { new(selected.Record, propertyName, text) };
        foreach (var record in Context.Records.Where(record => !ReferenceEquals(record, selected.Record)))
        {
            if (record.Item is not RegionConfig other) continue;
            var parsed = ConfigStructuredStringCodec.ParseIds(other.StatesListString);
            if (parsed.IsValid && parsed.Items.Any(added.Contains))
                changes.Add(new(record, propertyName,
                    ConfigStructuredStringCodec.FormatIds(parsed.Items.Where(id => !added.Contains(id)))));
        }
        Context.SetPropertyValues(changes, changes.Count > 1 ? $"{description}（并从原地区移出）" : description);
        foreach (var list in ChipLists) list.Refresh();
    }

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        OnPropertyChanged(nameof(ArchitectureSummary));
    }

    private PersonGroupNote? GetNote()
    {
        if (SelectedConfig is not { } region) return null;
        var states = Territory.Ids(region.StatesListString);
        var warnings = new List<string>();
        if (region.RegionCoreID != -1 &&
            Territory.StateOf(Targets("architectures"), region.RegionCoreID) is { } coreState && !states.Contains(coreState))
            warnings.Add("核心城市不在所辖州内");
        var shared = Context.Records.Select(record => record.Item).OfType<RegionConfig>()
            .Where(other => !ReferenceEquals(other, region))
            .SelectMany(other => Territory.Ids(other.StatesListString).Intersect(states)
                .Select(id => $"{Territory.Label(Targets("states"), id)}（{other.Name}）"))
            .ToArray();
        if (shared.Length > 0) warnings.Add($"以下州同时属于其他地区：{string.Join("、", shared)}");
        return warnings.Count > 0
            ? new PersonGroupNote(string.Join("；", warnings), true)
            : new PersonGroupNote($"所辖 {states.Count} 州", false);
    }
}

/// <summary>
/// Edits States.json. The game reads each state's neighbour list on its own, so adding or removing a
/// neighbour updates that neighbour's list too, in the same undo step.
/// </summary>
public sealed class StateEditorViewModel : RecordListEditorViewModel<StateConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");

    public StateEditorViewModel(ConfigEditorContext context) : base(context, "州", "个")
    {
        Groups = [Group("基本", ("名称", nameof(StateConfig.Name)), ("州治", nameof(StateConfig.StateAdminID)))
            .WithNote(GetNote)];
        ChipLists = [ChipList("相邻州", nameof(StateConfig.ContactStatesString), "states")];
        MakeMutualCommand = new RelayCommand(MakeMutual, () => OneSidedNeighbours().Any());
        Initialize();
    }

    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public PersonIdChipListViewModel Neighbours => ChipLists[0];
    public ICommand MakeMutualCommand { get; }
    public bool HasOneSidedNeighbours => OneSidedNeighbours().Any();
    public string NeighbourStatus => SelectedConfig is null ? "" : GetNeighbourStatus();
    public string RegionText => SelectedConfig is { } state
        ? Targets("regions").Select(target => target.Item).OfType<RegionConfig>()
            .Where(region => Territory.Ids(region.StatesListString).Contains(state.Id))
            .Select(region => $"{region.Id} {region.Name}").DefaultIfEmpty("（未归入任何地区）")
            .Aggregate((left, right) => $"{left}、{right}")
        : "";
    public string ArchitectureSummary => SelectedConfig is { } state
        ? Territory.Describe(Territory.ArchitecturesIn(Targets("architectures"), new HashSet<int> { state.Id }))
        : "";

    protected override string GetName(StateConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) =>
        propertyName == nameof(StateConfig.StateAdminID) ? () => TargetOptions("architectures", None) : null;

    public override void SetValue(string propertyName, object value, string description)
    {
        if (propertyName != nameof(StateConfig.ContactStatesString) || SelectedRecord is not { } selected ||
            value is not string text)
        {
            base.SetValue(propertyName, value, description);
            return;
        }

        var self = (StateConfig)selected.Record.Item;
        var before = Territory.Ids(self.ContactStatesString);
        var after = Territory.Ids(text);
        var changes = new List<ConfigEditorPropertyChange> { new(selected.Record, propertyName, text) };
        foreach (var record in Context.Records.Where(record => !ReferenceEquals(record, selected.Record)))
        {
            if (record.Item is not StateConfig other) continue;
            var parsed = ConfigStructuredStringCodec.ParseIds(other.ContactStatesString);
            if (!parsed.IsValid) continue;
            if (after.Contains(other.Id) && !before.Contains(other.Id) && !parsed.Items.Contains(self.Id))
                changes.Add(new(record, propertyName, ConfigStructuredStringCodec.FormatIds(parsed.Items.Append(self.Id))));
            else if (before.Contains(other.Id) && !after.Contains(other.Id) && parsed.Items.Contains(self.Id))
                changes.Add(new(record, propertyName,
                    ConfigStructuredStringCodec.FormatIds(parsed.Items.Where(id => id != self.Id))));
        }
        Context.SetPropertyValues(changes, changes.Count > 1 ? $"{description}（双向）" : description);
        foreach (var list in ChipLists) list.Refresh();
    }

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        OnPropertyChanged(nameof(RegionText));
        OnPropertyChanged(nameof(ArchitectureSummary));
        OnPropertyChanged(nameof(NeighbourStatus));
        OnPropertyChanged(nameof(HasOneSidedNeighbours));
        ((RelayCommand)MakeMutualCommand).RaiseCanExecuteChanged();
    }

    // Neighbours listed on only one side: (record, its list after adding the missing side).
    private IEnumerable<(ConfigRecordViewModel Record, List<int> Fixed)> OneSidedNeighbours()
    {
        if (SelectedRecord is not { } selected) yield break;
        var self = (StateConfig)selected.Record.Item;
        var mine = ConfigStructuredStringCodec.ParseIds(self.ContactStatesString);
        if (!mine.IsValid) yield break;
        var myFixed = mine.Items.ToList();
        var others = new List<(ConfigRecordViewModel, List<int>)>();
        foreach (var record in Context.Records.Where(record => !ReferenceEquals(record, selected.Record)))
        {
            if (record.Item is not StateConfig other) continue;
            var theirs = ConfigStructuredStringCodec.ParseIds(other.ContactStatesString);
            if (!theirs.IsValid) continue;
            var iList = mine.Items.Contains(other.Id);
            var theyList = theirs.Items.Contains(self.Id);
            if (iList && !theyList) others.Add((record, [..theirs.Items, self.Id]));
            if (theyList && !iList) myFixed.Add(other.Id);
        }
        if (myFixed.Count != mine.Items.Count) yield return (selected.Record, myFixed);
        foreach (var other in others) yield return other;
    }

    private void MakeMutual()
    {
        var changes = OneSidedNeighbours()
            .Select(item => new ConfigEditorPropertyChange(item.Record, nameof(StateConfig.ContactStatesString),
                ConfigStructuredStringCodec.FormatIds(item.Fixed)))
            .ToArray();
        if (changes.Length == 0) return;
        Context.SetPropertyValues(changes, "补全双向相邻州");
        foreach (var list in ChipLists) list.Refresh();
    }

    private string GetNeighbourStatus()
    {
        var self = SelectedConfig!;
        var states = Targets("states");
        var mine = Territory.Ids(self.ContactStatesString);
        var notBack = Context.Records.Select(record => record.Item).OfType<StateConfig>()
            .Where(other => other.Id != self.Id && mine.Contains(other.Id) &&
                !Territory.Ids(other.ContactStatesString).Contains(self.Id))
            .Select(other => Territory.Label(states, other.Id)).ToArray();
        var onlyBack = Context.Records.Select(record => record.Item).OfType<StateConfig>()
            .Where(other => other.Id != self.Id && !mine.Contains(other.Id) &&
                Territory.Ids(other.ContactStatesString).Contains(self.Id))
            .Select(other => Territory.Label(states, other.Id)).ToArray();
        var notes = new List<string> { "添加或移除相邻州时，会同时修改对方的相邻州列表。" };
        if (notBack.Length > 0) notes.Add($"{string.Join("、", notBack)} 未把本州列为相邻。");
        if (onlyBack.Length > 0) notes.Add($"{string.Join("、", onlyBack)} 把本州列为相邻，但本州未列出对方。");
        return string.Join(" ", notes);
    }

    private PersonGroupNote? GetNote()
    {
        if (SelectedConfig is not { } state) return null;
        return state.StateAdminID != -1 &&
               Territory.StateOf(Targets("architectures"), state.StateAdminID) is { } adminState && adminState != state.Id
            ? new PersonGroupNote("州治不在本州内", true)
            : null;
    }
}

internal static class Territory
{
    public static HashSet<int> Ids(string? text) =>
        ConfigStructuredStringCodec.ParseIds(text) is { IsValid: true } parsed ? [..parsed.Items] : [];

    public static int? StateOf(IEnumerable<ConfigReferenceTarget> architectures, int id) =>
        architectures.FirstOrDefault(target => target.Id == id)?.Item is ArchitectureConfig architecture
            ? architecture.StateId
            : null;

    public static ArchitectureConfig[] ArchitecturesIn(
        IEnumerable<ConfigReferenceTarget> architectures, IReadOnlySet<int> states) =>
        architectures.Select(target => target.Item).OfType<ArchitectureConfig>()
            .Where(architecture => states.Contains(architecture.StateId))
            .OrderBy(architecture => architecture.Id).ToArray();

    public static string Describe(IReadOnlyList<ArchitectureConfig> architectures) => architectures.Count == 0
        ? "没有建筑"
        : $"共 {architectures.Count} 座：{string.Join("、", architectures.Select(architecture => architecture.Name))}";

    public static string Label(IEnumerable<ConfigReferenceTarget> states, int id) =>
        states.FirstOrDefault(target => target.Id == id) is { } target ? target.DisplayName : $"#{id}";
}
