using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Edits Architectures.json. The game places each person in the architecture whose person lists name them,
/// so a person is kept in one list of one architecture: adding them here removes them from any other list,
/// in the same undo step.
/// </summary>
public sealed class ArchitectureEditorViewModel : RecordListEditorViewModel<ArchitectureConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");

    private static readonly string[] PersonLists =
    [
        nameof(ArchitectureConfig.PersonsString),
        nameof(ArchitectureConfig.NoFactionPersonsString),
        nameof(ArchitectureConfig.MovingPersonsString),
        nameof(ArchitectureConfig.NoFactionMovingPersonsString),
        nameof(ArchitectureConfig.ConcubineString)
    ];

    public ArchitectureEditorViewModel(ConfigEditorContext context) : base(context, "建筑", "座")
    {
        Groups =
        [
            Group("基本", ("名称", nameof(ArchitectureConfig.Name)), ("类型", nameof(ArchitectureConfig.KindId)),
                ("所属州", nameof(ArchitectureConfig.StateId)), ("太守", nameof(ArchitectureConfig.MayorId)),
                ("名牌图", nameof(ArchitectureConfig.CaptionId)), ("原势力名", nameof(ArchitectureConfig.OldFactionName)),
                ("战略要地", nameof(ArchitectureConfig.IsStrategicCenter)), ("皇帝所在", nameof(ArchitectureConfig.HasEmperor)))
                .WithNote(GetNote),
            Group("内政", ("人口", nameof(ArchitectureConfig.Population)), ("兵役人口", nameof(ArchitectureConfig.MilitaryPopulation)),
                ("农业", nameof(ArchitectureConfig.Agriculture)), ("商业", nameof(ArchitectureConfig.Commerce)),
                ("技术", nameof(ArchitectureConfig.Technology)), ("统治", nameof(ArchitectureConfig.Domination)),
                ("民心", nameof(ArchitectureConfig.Morale)), ("耐久", nameof(ArchitectureConfig.Endurance)),
                ("资金", nameof(ArchitectureConfig.Fund)), ("粮草", nameof(ArchitectureConfig.Food))),
            Group("地图", ("占地坐标", nameof(ArchitectureConfig.ArchitectureAreaString)))
        ];
        // The runtime counters, plans, transfers, packs and auto-management switches the game maintains while
        // playing are left untouched and not shown, as are the people on the move and the captives, which the
        // game fills in during play.
        ChipLists =
        [
            ChipList("特色", nameof(ArchitectureConfig.CharacteristicsString), "influences",
                "城市自带的效果，取自 CommonData 的“影响”表，例如地貌、内政上限、出征加成。"),
            ChipList("资源", nameof(ArchitectureConfig.ResourceKindsString), "resource-kinds",
                "城中出产的资源种类，取自 CommonData 的“资源类型”表。"),
            ChipList("设施", nameof(ArchitectureConfig.FacilitiesString), "facilities",
                "城中已建成的设施，对应剧本“设施”表中的记录；新设施要先在“设施”中建立记录。"),
            ChipList("编队", nameof(ArchitectureConfig.MilitariesString), "militaries",
                "驻扎在城中的部队编队，对应剧本“军事单位”表中的记录。"),
            ChipList("陆路连接", nameof(ArchitectureConfig.AILandLinksString), "architectures",
                "电脑势力认为从本城走陆路可以直接到达的城。"),
            ChipList("水路连接", nameof(ArchitectureConfig.AIWaterLinksString), "architectures",
                "电脑势力认为从本城走水路可以直接到达的城。"),
            ChipList("武将", nameof(ArchitectureConfig.PersonsString), "persons",
                "身在本城、已出仕占领势力的武将。"),
            ChipList("在野武将", nameof(ArchitectureConfig.NoFactionPersonsString), "persons",
                "身在本城但尚未出仕的人物，可被登用。"),
            ChipList("妃子", nameof(ArchitectureConfig.ConcubineString), "persons",
                "住在本城后宫的人物。")
        ];
        Initialize();
    }

    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public PersonFieldGroupViewModel InteriorGroup => Groups[1];
    public PersonFieldGroupViewModel MapGroup => Groups[2];
    public IReadOnlyList<PersonIdChipListViewModel> FeatureLists => ChipLists.Take(4).ToArray();
    public IReadOnlyList<PersonIdChipListViewModel> LinkLists => ChipLists.Skip(4).Take(2).ToArray();
    public IReadOnlyList<PersonIdChipListViewModel> PeopleLists => ChipLists.Skip(6).ToArray();

    private static readonly Dictionary<string, string> PersonListNames = new()
    {
        [nameof(ArchitectureConfig.PersonsString)] = "武将",
        [nameof(ArchitectureConfig.NoFactionPersonsString)] = "在野",
        [nameof(ArchitectureConfig.MovingPersonsString)] = "移动中",
        [nameof(ArchitectureConfig.NoFactionMovingPersonsString)] = "在野移动中",
        [nameof(ArchitectureConfig.ConcubineString)] = "妃子"
    };

    // Person candidates say where the person is now, since adding them moves them here.
    public override IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey)
    {
        var candidates = base.GetChipCandidates(targetKey);
        if (targetKey != "persons") return candidates;
        var locations = new Dictionary<int, string>();
        foreach (var architecture in Context.Records.Select(record => record.Item).OfType<ArchitectureConfig>())
            foreach (var list in PersonLists)
                foreach (var id in Territory.Ids(GetList(architecture, list)))
                    locations.TryAdd(id, $"{architecture.Name}·{PersonListNames[list]}");
        return [..candidates.Select(option => locations.TryGetValue(option.Id, out var where)
            ? option with { Label = $"{option.Label}（现在 {where}）" }
            : option)];
    }

    public string FactionText => SelectedConfig is { } architecture
        ? Targets("factions").Select(target => target.Item).OfType<FactionConfig>()
            .Where(faction => Territory.Ids(faction.ArchitecturesString).Contains(architecture.Id))
            .Select(faction => $"{faction.Id} {faction.Name}").DefaultIfEmpty("（无势力占领）")
            .Aggregate((left, right) => $"{left}、{right}")
        : "";

    public string RegionText => SelectedConfig is { } architecture
        ? Targets("regions").Select(target => target.Item).OfType<RegionConfig>()
            .Where(region => Territory.Ids(region.StatesListString).Contains(architecture.StateId))
            .Select(region => $"{region.Id} {region.Name}").DefaultIfEmpty("（所属州未归入任何地区）")
            .Aggregate((left, right) => $"{left}、{right}")
        : "";

    public string LinkStatus => SelectedConfig is null ? "" : GetLinkStatus();

    protected override string GetName(ArchitectureConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(ArchitectureConfig.KindId) => () => TargetOptions("architecture-kinds"),
        nameof(ArchitectureConfig.StateId) => () => TargetOptions("states"),
        nameof(ArchitectureConfig.MayorId) => () => TargetOptions("persons", None),
        _ => null
    };

    protected override string ChipLabel(string targetKey, ConfigReferenceTarget target) => target.Item switch
    {
        FacilityConfig facility when Targets("facility-kind-levels").FirstOrDefault(level => level.Id == facility.LevelId)
            ?.Item is FacilityKindLevelConfig level => $"{target.Id} {KindName(level.KindId)} Lv{level.Level}",
        CaptiveConfig captive => $"{target.Id} {GetChipLabel("persons", captive.CaptivePersonId)}",
        _ => base.ChipLabel(targetKey, target)
    };

    private string KindName(int kindId) =>
        Targets("facility-kinds").FirstOrDefault(kind => kind.Id == kindId)?.DisplayName ?? $"设施类型 {kindId}";

    public override void SetValue(string propertyName, object value, string description)
    {
        if (!PersonLists.Contains(propertyName) || SelectedRecord is not { } selected || value is not string text)
        {
            base.SetValue(propertyName, value, description);
            return;
        }

        var self = (ArchitectureConfig)selected.Record.Item;
        var added = Territory.Ids(text).Except(Territory.Ids(GetList(self, propertyName))).ToHashSet();
        var changes = new List<ConfigEditorPropertyChange> { new(selected.Record, propertyName, text) };
        foreach (var record in Context.Records)
        {
            if (record.Item is not ArchitectureConfig other) continue;
            foreach (var list in PersonLists)
            {
                if (ReferenceEquals(other, self) && list == propertyName) continue;
                var parsed = ConfigStructuredStringCodec.ParseIds(GetList(other, list));
                if (parsed.IsValid && parsed.Items.Any(added.Contains))
                    changes.Add(new(record, list,
                        ConfigStructuredStringCodec.FormatIds(parsed.Items.Where(id => !added.Contains(id)))));
            }
        }
        Context.SetPropertyValues(changes, changes.Count > 1 ? $"{description}（并从原所在处移出）" : description);
        foreach (var list in ChipLists) list.Refresh();
    }

    private static string? GetList(ArchitectureConfig architecture, string propertyName) =>
        typeof(ArchitectureConfig).GetProperty(propertyName)!.GetValue(architecture) as string;

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        OnPropertyChanged(nameof(FactionText));
        OnPropertyChanged(nameof(RegionText));
        OnPropertyChanged(nameof(LinkStatus));
    }

    protected override void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        base.OnReferenceOptionsChanged(sender, e);
        OnPropertyChanged(nameof(FactionText));
        OnPropertyChanged(nameof(RegionText));
    }

    // AI routes are read from each architecture separately; one-sided links are reported, not changed.
    private string GetLinkStatus()
    {
        var self = SelectedConfig!;
        var architectures = Context.Records.Select(record => record.Item).OfType<ArchitectureConfig>().ToArray();
        var notes = new List<string>();
        foreach (var (label, property) in new[]
                 {
                     ("陆路", nameof(ArchitectureConfig.AILandLinksString)),
                     ("水路", nameof(ArchitectureConfig.AIWaterLinksString))
                 })
        {
            var mine = Territory.Ids(GetList(self, property));
            var notBack = architectures.Where(other => other.Id != self.Id && mine.Contains(other.Id) &&
                    !Territory.Ids(GetList(other, property)).Contains(self.Id))
                .Select(other => other.Name).ToArray();
            var onlyBack = architectures.Where(other => other.Id != self.Id && !mine.Contains(other.Id) &&
                    Territory.Ids(GetList(other, property)).Contains(self.Id))
                .Select(other => other.Name).ToArray();
            if (notBack.Length > 0) notes.Add($"{label}：{string.Join("、", notBack)} 未连回本城。");
            if (onlyBack.Length > 0) notes.Add($"{label}：{string.Join("、", onlyBack)} 连到本城，但本城未连回。");
        }
        return notes.Count == 0 ? "AI 行军路线，各城分别读取自己的连接列表。" : string.Join(" ", notes);
    }

    private PersonGroupNote? GetNote()
    {
        if (SelectedConfig is not { } architecture) return null;
        var warnings = new List<string>();
        if (Targets("states").Count > 0 && Targets("states").All(target => target.Id != architecture.StateId))
            warnings.Add($"所属州 {architecture.StateId} 不存在");
        if (architecture.MayorId != -1 &&
            !PersonLists.Any(list => Territory.Ids(GetList(architecture, list)).Contains(architecture.MayorId)))
            warnings.Add("太守不在本城");
        var duplicated = Context.Records.Select(record => record.Item).OfType<ArchitectureConfig>()
            .Where(other => !ReferenceEquals(other, architecture))
            .SelectMany(other => PersonLists.SelectMany(list => Territory.Ids(GetList(other, list)))
                .Intersect(PersonLists.SelectMany(list => Territory.Ids(GetList(architecture, list))))
                .Select(id => $"{GetChipLabel("persons", id)}（{other.Name}）"))
            .ToArray();
        if (duplicated.Length > 0) warnings.Add($"以下人物同时在其他建筑：{string.Join("、", duplicated)}");
        return warnings.Count > 0 ? new PersonGroupNote(string.Join("；", warnings), true) : null;
    }
}
