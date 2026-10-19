using System.Collections.ObjectModel;
using System.Windows.Input;
using GameDatas;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Edits Events.json. An event involves numbered person slots %0, %1, …: PersonString, PersonCondString and the
/// person effect strings are "slot value" pairs, and dialogue is "slot text" pairs. Each slot is edited as a card.
/// </summary>
public sealed class EventEditorViewModel : RecordListEditorViewModel<EventConfig>
{
    internal const string SlotPersonsKey = "event-slot-persons";
    private static readonly PersonChoiceOption None = new(-1, "-1 无");
    private static readonly PersonChoiceOption AnyPerson = new(-1, "-1 任何武将");

    private static readonly string[] SlotProperties =
    [
        nameof(EventConfig.PersonString), nameof(EventConfig.PersonCondString), nameof(EventConfig.EffectString),
        nameof(EventConfig.YesEffectString), nameof(EventConfig.NoEffectString)
    ];

    public EventEditorViewModel(ConfigEditorContext context) : base(context, "事件", "个")
    {
        Groups =
        [
            Group("基本", ("名称", nameof(EventConfig.Name)), ("前置事件", nameof(EventConfig.AfterEventHappened)),
                    ("发动几率", nameof(EventConfig.HappenChance)), ("可以重复", nameof(EventConfig.Repeatable)),
                    ("不重要", nameof(EventConfig.Minor)), ("全势力可见", nameof(EventConfig.GloballyDisplayed)),
                    ("已发生过", nameof(EventConfig.Happened)))
                .WithNote(GetNote),
            Group("时间", ("开始年", nameof(EventConfig.StartYear)), ("开始月", nameof(EventConfig.StartMonth)),
                ("结束年", nameof(EventConfig.EndYear)), ("结束月", nameof(EventConfig.EndMonth))),
            Group("演出", ("图片", nameof(EventConfig.Image)), ("音效", nameof(EventConfig.Sound)))
        ];
        // NextScenario is unused by the game and TryToShowString is runtime display text; both are kept untouched.
        ChipLists =
        [
            ChipList("建筑", nameof(EventConfig.ArchitectureString), "architectures",
                "武将们必须都在其中一座建筑里；留空表示任何建筑。"),
            ChipList("建筑条件", nameof(EventConfig.ArchitectureCondString), "conditions",
                "武将所在建筑必须满足的条件；如果用的是武将条件，检查的是该建筑的太守。"),
            ChipList("势力", nameof(EventConfig.FactionString), "factions",
                "武将们必须都属于其中一个势力；留空表示任何势力。"),
            ChipList("势力条件", nameof(EventConfig.FactionCondString), "conditions",
                "武将所在势力必须满足的条件；如果用的是武将条件，检查的是该势力的君主。"),
            ChipList("建筑效果", nameof(EventConfig.ArchitectureEffectString), "architecture-effects",
                "事件发生后对武将所在建筑执行的效果；如果用的是武将效果，作用于该建筑的太守。"),
            ChipList("势力效果", nameof(EventConfig.FactionEffectIDString), "architecture-effects",
                "事件发生后对武将所在势力执行的效果；如果用的是武将效果，作用于该势力的君主。"),
            ChipList("选是建筑效果", nameof(EventConfig.YesArchitectureEffectString), "architecture-effects",
                "玩家选“是”后对武将所在建筑执行的效果。"),
            ChipList("选否建筑效果", nameof(EventConfig.NoArchitectureEffectString), "architecture-effects",
                "玩家选“否”后对武将所在建筑执行的效果。")
        ];
        const string dialogHint = "按顺序播放。说话人是武将位；台词里可以用 %0、%1… 代替对应武将的姓名。游戏以空格分隔，台词中不能有空格。";
        Dialogs =
        [
            new EventDialogListViewModel(this, nameof(EventConfig.DialogString), "对话", dialogHint, SpeakerOptions),
            new EventDialogListViewModel(this, nameof(EventConfig.YesDialogString), "选是对话",
                "玩家选“是”后播放，规则同上。", SpeakerOptions),
            new EventDialogListViewModel(this, nameof(EventConfig.NoDialogString), "选否对话",
                "玩家选“否”后播放，规则同上。", SpeakerOptions),
            new EventDialogListViewModel(this, nameof(EventConfig.ScenBiographyString), "武将列传",
                "事件发生后写入对应武将列传的文字，规则同上。", SpeakerOptions)
        ];
        AddSlotCommand = new RelayCommand(AddSlot, () => HasSelection && SlotsReadable);
        Initialize();
    }

    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public PersonFieldGroupViewModel TimeGroup => Groups[1];
    public PersonFieldGroupViewModel MediaGroup => Groups[2];
    public IReadOnlyList<PersonIdChipListViewModel> ScopeLists => ChipLists.Take(4).ToArray();
    public IReadOnlyList<PersonIdChipListViewModel> EffectLists => ChipLists.Skip(4).ToArray();
    public ObservableCollection<EventSlotViewModel> Slots { get; } = [];
    public IReadOnlyList<EventDialogListViewModel> Dialogs { get; }
    public ICommand AddSlotCommand { get; }
    public bool HasNoSlots => HasSelection && Slots.Count == 0;

    protected override string GetName(EventConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) =>
        propertyName == nameof(EventConfig.AfterEventHappened) ? () => TargetOptions("events", None) : null;

    public override IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey) =>
        targetKey == SlotPersonsKey ? [AnyPerson, ..base.GetChipCandidates("persons")] : base.GetChipCandidates(targetKey);

    public override string GetChipLabel(string targetKey, int id) => targetKey == SlotPersonsKey
        ? id == -1 ? AnyPerson.Label : base.GetChipLabel("persons", id)
        : base.GetChipLabel(targetKey, id);

    private bool SlotsReadable => SelectedConfig is { } config &&
        SlotProperties.All(property => EventPairs.TryParse(GetString(config, property), out _));

    private static string? GetString(EventConfig config, string property) =>
        typeof(EventConfig).GetProperty(property)!.GetValue(config) as string;

    // Slots run from %0 to the highest slot any person, condition or effect pair names.
    private int SlotCount()
    {
        if (SelectedConfig is not { } config) return 0;
        var max = -1;
        foreach (var property in SlotProperties)
            if (EventPairs.TryParse(GetString(config, property), out var pairs) && pairs.Count > 0)
                max = Math.Max(max, pairs.Max(pair => pair.First));
        return max + 1;
    }

    private IReadOnlyList<PersonChoiceOption> SpeakerOptions() =>
        [..Slots.Select(slot => new PersonChoiceOption(slot.Key, $"%{slot.Key} {SlotSummary(slot.Key)}"))];

    private string SlotSummary(int key)
    {
        if (SelectedConfig is not { } config || !EventPairs.TryParse(config.PersonString, out var pairs)) return "";
        var names = pairs.Where(pair => pair.First == key)
            .Select(pair => pair.Second == -1 ? "任何武将" : PersonName(pair.Second)).ToArray();
        return names.Length == 0 ? "（未指定武将）" : string.Join("／", names.Take(3)) + (names.Length > 3 ? "…" : "");
    }

    private string PersonName(int id) =>
        Targets("persons").FirstOrDefault(target => target.Id == id)?.DisplayName ?? $"#{id}";

    private void AddSlot()
    {
        if (SelectedRecord is not { } selected || !EventPairs.TryParse(SelectedConfig!.PersonString, out var pairs)) return;
        Context.SetPropertyValue(selected.Record, nameof(EventConfig.PersonString),
            EventPairs.Format([..pairs, (Slots.Count, -1)]), "添加武将位");
    }

    // Only the last slot can be removed, so the other slots keep their numbers (dialogue refers to them as %k).
    private void RemoveSlot(int key)
    {
        if (SelectedRecord is not { } selected || key != Slots.Count - 1 || !SlotsReadable) return;
        var config = SelectedConfig!;
        var changes = new List<ConfigEditorPropertyChange>();
        foreach (var property in SlotProperties)
        {
            EventPairs.TryParse(GetString(config, property), out var pairs);
            if (pairs.Any(pair => pair.First == key))
                changes.Add(new(selected.Record, property, EventPairs.Format(pairs.Where(pair => pair.First != key))));
        }
        foreach (var property in new[]
                 {
                     nameof(EventConfig.DialogString), nameof(EventConfig.YesDialogString),
                     nameof(EventConfig.NoDialogString), nameof(EventConfig.ScenBiographyString)
                 })
            if (EventPairs.TryParseDialog(GetString(config, property), out var lines) && lines.Any(line => line.Speaker == key))
                changes.Add(new(selected.Record, property,
                    EventPairs.FormatDialog(lines.Where(line => line.Speaker != key))));
        if (changes.Count > 0) Context.SetPropertyValues(changes, $"删除武将位 %{key}");
    }

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        RefreshSlots();
    }

    protected override void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        base.OnReferenceOptionsChanged(sender, e);
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        var count = SlotCount();
        if (count != Slots.Count)
        {
            Slots.Clear();
            for (var key = 0; key < count; key++)
            {
                var slot = key;
                Slots.Add(new EventSlotViewModel(slot, this,
                    new RelayCommand(() => RemoveSlot(slot), () => slot == Slots.Count - 1)));
            }
        }
        foreach (var slot in Slots) slot.Refresh();
        foreach (var dialog in Dialogs) dialog.Refresh();
        OnPropertyChanged(nameof(HasNoSlots));
        ((RelayCommand)AddSlotCommand).RaiseCanExecuteChanged();
    }

    private PersonGroupNote? GetNote()
    {
        if (SelectedConfig is not { } config) return null;
        if (config.HappenChance <= 0) return new PersonGroupNote("发动几率必须大于 0（实际几率为 1 除以此数）", true);
        return new PersonGroupNote(config.HappenChance == 1
            ? "条件满足时必定发生"
            : $"条件满足时每次检查有 1/{config.HappenChance} 的机会发生", false);
    }
}
