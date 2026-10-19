using GameDatas;
using GameEnums;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Edits TroopEvents.json: events a troop triggers in battle. Target persons, person effects and area effects
/// are pair lists and are edited as rows.
/// </summary>
public sealed class TroopEventEditorViewModel : RecordListEditorViewModel<TroopEventConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");
    private static readonly PersonChoiceOption AnyTroop = new(-1, "-1 任何部队");

    // Labels follow the game's EffectAreaKind and EventCheckAreaKind values.
    private static readonly IReadOnlyList<PersonChoiceOption> Areas =
    [
        new(0, "0 视野内敌军"), new(1, "1 视野内友军"), new(2, "2 周围八格敌军"),
        new(3, "3 周围八格友军"), new(4, "4 攻击范围内敌军"), new(5, "5 攻击范围内友军")
    ];
    private static readonly IReadOnlyList<PersonChoiceOption> Relations = [new(0, "0 非友好"), new(1, "1 友好")];
    private static readonly IReadOnlyList<PersonChoiceOption> CheckAreas =
        [new(0, "0 视野内"), new(1, "1 周围八格"), new(2, "2 攻击范围内")];
    private bool _refreshingCheckArea;

    public TroopEventEditorViewModel(ConfigEditorContext context) : base(context, "部队事件", "个")
    {
        Groups =
        [
            Group("基本", ("名称", nameof(TroopEventConfig.Name)), ("发动人物", nameof(TroopEventConfig.LaunchPersonString)),
                    ("发动几率", nameof(TroopEventConfig.HappenChance)),
                    ("前置事件", nameof(TroopEventConfig.AfterEventHappened)),
                    ("可以重复", nameof(TroopEventConfig.Repeatable)), ("已发生过", nameof(TroopEventConfig.Happened)))
                .WithNote(GetNote),
            Group("演出", ("图片", nameof(TroopEventConfig.Image)), ("音效", nameof(TroopEventConfig.Sound)),
                ("效果说明", nameof(TroopEventConfig.TryToShowString)))
        ];
        ChipLists =
        [
            ChipList("发动条件", nameof(TroopEventConfig.ConditionsString), "conditions",
                "发动事件的部队必须满足的条件；留空表示不限。"),
            ChipList("自身效果", nameof(TroopEventConfig.SelfEffectsString), "troop-effects",
                "事件发生后对发动部队执行的效果。")
        ];
        TargetPersons = new EventPairListViewModel(this, nameof(TroopEventConfig.TargetPersonsString), "目标人物",
            "在下方“搜索范围”内必须能找到这些人物的部队，并且关系相符，事件才会发生；留空表示不检查。",
            "关系", () => Relations, false, "人物", PersonOptions, true);
        EffectPersons = new EventPairListViewModel(this, nameof(TroopEventConfig.EffectPersonsString), "特定人物效果",
            "事件发生后对指定人物所在部队执行的效果。",
            "人物", PersonOptions, true, "效果", () => TargetOptions("troop-effects"), true);
        EffectAreas = new EventPairListViewModel(this, nameof(TroopEventConfig.EffectAreasString), "范围效果",
            "事件发生后对发动部队周围某个范围内的部队执行的效果。",
            "范围", () => Areas, false, "效果", () => TargetOptions("troop-effects"), true);
        Dialog = new EventDialogListViewModel(this, nameof(TroopEventConfig.DialogString), "对话",
            "按顺序播放。说话人可以是任意人物，“-1”为发动部队的主将。游戏以空格分隔，台词中不能有空格。",
            () => [AnySpeaker, ..TargetOptions("persons")]);
        Initialize();
    }

    private static readonly PersonChoiceOption AnySpeaker = new(-1, "-1 发动部队的主将");

    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public PersonFieldGroupViewModel MediaGroup => Groups[1];
    public PersonIdChipListViewModel Conditions => ChipLists[0];
    public PersonIdChipListViewModel SelfEffects => ChipLists[1];
    public EventPairListViewModel TargetPersons { get; }
    public EventPairListViewModel EffectPersons { get; }
    public EventPairListViewModel EffectAreas { get; }
    public EventDialogListViewModel Dialog { get; }
    public IReadOnlyList<PersonChoiceOption> CheckAreaOptions => CheckAreas;

    public PersonChoiceOption? SelectedCheckArea
    {
        get => SelectedConfig is { } config ? CheckAreas.FirstOrDefault(option => option.Id == (int)config.CheckArea) : null;
        set
        {
            if (value is null || _refreshingCheckArea || SelectedRecord is not { } selected ||
                (int)((TroopEventConfig)selected.Record.Item).CheckArea == value.Id) return;
            Context.SetPropertyValue(selected.Record, nameof(TroopEventConfig.CheckArea),
                (EventCheckAreaKind)value.Id, "修改部队事件搜索范围");
        }
    }

    private IReadOnlyList<PersonChoiceOption> PersonOptions() => TargetOptions("persons");

    protected override string GetName(TroopEventConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(TroopEventConfig.LaunchPersonString) => () => TargetOptions("persons", AnyTroop),
        nameof(TroopEventConfig.AfterEventHappened) => () => TargetOptions("troop-events", None),
        _ => null
    };

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        RefreshLists();
    }

    protected override void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        base.OnReferenceOptionsChanged(sender, e);
        RefreshLists();
    }

    private void RefreshLists()
    {
        TargetPersons.Refresh();
        EffectPersons.Refresh();
        EffectAreas.Refresh();
        Dialog.Refresh();
        _refreshingCheckArea = true;
        try
        {
            OnPropertyChanged(nameof(SelectedCheckArea));
        }
        finally
        {
            _refreshingCheckArea = false;
        }
    }

    private PersonGroupNote? GetNote() => SelectedConfig is { } config && (config.HappenChance < 0 || config.HappenChance > 100)
        ? new PersonGroupNote("发动几率应在 0–100 之间", true)
        : SelectedConfig is null ? null : new PersonGroupNote($"条件满足时有 {SelectedConfig.HappenChance}% 的机会发生", false);
}
