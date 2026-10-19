using GameDatas;
using GameEnums;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Edits Sections.json, the game's 军势, shown to players as 军团: a group of a faction's architectures run by an
/// AI commission. The game links each architecture to the last section listing it, so an architecture is kept in
/// one section: adding it here removes it from its old section in the same undo step.
/// </summary>
public sealed class SectionEditorViewModel : RecordListEditorViewModel<SectionConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");

    private static readonly IReadOnlyDictionary<SectionOrientationKind, (string Label, string Property)> Orientations =
        new Dictionary<SectionOrientationKind, (string, string)>
        {
            [SectionOrientationKind.Section] = ("目标军团", nameof(SectionConfig.OrientationSectionID)),
            [SectionOrientationKind.Faction] = ("目标势力", nameof(SectionConfig.OrientationFactionID)),
            [SectionOrientationKind.State] = ("目标州域", nameof(SectionConfig.OrientationStateID)),
            [SectionOrientationKind.Architecture] = ("目标建筑", nameof(SectionConfig.OrientationArchitectureID))
        };

    public SectionEditorViewModel(ConfigEditorContext context) : base(context, "军团", "个")
    {
        Groups =
        [
            Group("基本", ("名称", nameof(SectionConfig.Name)), ("委任类型", nameof(SectionConfig.AIDetailIDString)))
                .WithNote(GetBasicNote),
            Group("目标", ("目标军团", nameof(SectionConfig.OrientationSectionID)),
                    ("目标势力", nameof(SectionConfig.OrientationFactionID)),
                    ("目标州域", nameof(SectionConfig.OrientationStateID)),
                    ("目标建筑", nameof(SectionConfig.OrientationArchitectureID)))
                .WithNote(GetOrientationNote)
        ];
        ChipLists = [ChipList("所辖建筑", nameof(SectionConfig.ArchitecturesString), "architectures")];
        Initialize();
    }

    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public PersonFieldGroupViewModel OrientationGroup => Groups[1];
    public PersonIdChipListViewModel Architectures => ChipLists[0];
    public string FactionText => SelectedConfig is { } section ? GetFactionText(section) : "";
    public bool IsFactionWarning => SelectedConfig is { } section && OwningFactions(section).Length != 1;
    public string ArchitectureNote => SelectedConfig is { } section ? GetArchitectureNote(section) : "";
    public bool HasArchitectureNote => ArchitectureNote.Length > 0;

    protected override string GetName(SectionConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(SectionConfig.AIDetailIDString) => () => TargetOptions("section-ai-details"),
        nameof(SectionConfig.OrientationSectionID) => () => TargetOptions("sections", None),
        nameof(SectionConfig.OrientationFactionID) => () => TargetOptions("factions", None),
        nameof(SectionConfig.OrientationStateID) => () => TargetOptions("states", None),
        nameof(SectionConfig.OrientationArchitectureID) => () => TargetOptions("architectures", None),
        _ => null
    };

    public override void SetValue(string propertyName, object value, string description)
    {
        if (propertyName != nameof(SectionConfig.ArchitecturesString) || SelectedRecord is not { } selected ||
            value is not string text)
        {
            base.SetValue(propertyName, value, description);
            return;
        }

        var added = Territory.Ids(text)
            .Except(Territory.Ids(((SectionConfig)selected.Record.Item).ArchitecturesString)).ToHashSet();
        var changes = new List<ConfigEditorPropertyChange> { new(selected.Record, propertyName, text) };
        foreach (var record in Context.Records.Where(record => !ReferenceEquals(record, selected.Record)))
        {
            if (record.Item is not SectionConfig other) continue;
            var parsed = ConfigStructuredStringCodec.ParseIds(other.ArchitecturesString);
            if (parsed.IsValid && parsed.Items.Any(added.Contains))
                changes.Add(new(record, propertyName,
                    ConfigStructuredStringCodec.FormatIds(parsed.Items.Where(id => !added.Contains(id)))));
        }
        Context.SetPropertyValues(changes, changes.Count > 1 ? $"{description}（并从原军团移出）" : description);
        foreach (var list in ChipLists) list.Refresh();
    }

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        OnPropertyChanged(nameof(FactionText));
        OnPropertyChanged(nameof(IsFactionWarning));
        OnPropertyChanged(nameof(ArchitectureNote));
        OnPropertyChanged(nameof(HasArchitectureNote));
    }

    protected override void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        base.OnReferenceOptionsChanged(sender, e);
        RefreshSelection();
    }

    private SectionAIDetailConfig? AIDetail(SectionConfig section) =>
        Targets("section-ai-details").FirstOrDefault(target => target.Id == section.AIDetailIDString)?.Item
            as SectionAIDetailConfig;

    private FactionConfig[] OwningFactions(SectionConfig section) =>
        Targets("factions").Select(target => target.Item).OfType<FactionConfig>()
            .Where(faction => Territory.Ids(faction.SectionsString).Contains(section.Id)).ToArray();

    private string GetFactionText(SectionConfig section) => OwningFactions(section) switch
    {
        [] => "未被任何势力列入，游戏中不会使用这个军团。在势力配置的“军团”列表中添加。",
        [var faction] => $"{faction.Id} {faction.Name}",
        var factions => $"同时被多个势力列入：{string.Join("、", factions.Select(faction => $"{faction.Id} {faction.Name}"))}",
    };

    private string GetArchitectureNote(SectionConfig section)
    {
        if (OwningFactions(section) is not [var faction]) return "";
        var own = Territory.Ids(faction.ArchitecturesString);
        var outside = Territory.Ids(section.ArchitecturesString).Where(id => !own.Contains(id)).Order()
            .Select(id => GetChipLabel("architectures", id)).ToArray();
        return outside.Length == 0 ? "" : $"以下建筑不属于{faction.Name}：{string.Join("、", outside)}";
    }

    private PersonGroupNote? GetBasicNote()
    {
        if (SelectedConfig is not { } section) return null;
        if (Targets("section-ai-details").Count == 0) return null;
        return AIDetail(section) is { } detail
            ? new PersonGroupNote(string.IsNullOrWhiteSpace(detail.Description) ? detail.Name : detail.Description, false)
            : new PersonGroupNote($"委任类型 {section.AIDetailIDString} 不存在，游戏载入时会报错", true);
    }

    // Only the target matching the commission's OrientationKind is read by the game.
    private PersonGroupNote? GetOrientationNote()
    {
        if (SelectedConfig is not { } section || AIDetail(section) is not { } detail) return null;
        if (!Orientations.TryGetValue(detail.OrientationKind, out var orientation))
            return new PersonGroupNote($"委任类型“{detail.Name}”不需要目标，以下设置不起作用", false);
        var value = (int)typeof(SectionConfig).GetProperty(orientation.Property)!.GetValue(section)!;
        return value == -1
            ? new PersonGroupNote($"委任类型“{detail.Name}”需要设置{orientation.Label}", true)
            : new PersonGroupNote($"委任类型“{detail.Name}”只使用{orientation.Label}", false);
    }
}
