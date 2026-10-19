using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using GameDatas;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class FactionEditorViewModel : RecordListEditorViewModel<FactionConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");
    private IReadOnlyList<PersonColorOption> _colorOptions = [];
    private bool _refreshingColor;

    public FactionEditorViewModel(ConfigEditorContext context) : base(context, "势力", "个")
    {
        Groups =
        [
            Group("基本", ("名称", nameof(FactionConfig.Name)), ("君主", nameof(FactionConfig.LeaderID)),
                ("太子", nameof(FactionConfig.PrinceID)), ("首都", nameof(FactionConfig.CapitalID)),
                ("官爵", nameof(FactionConfig.OfficialRank)), ("朝廷贡献", nameof(FactionConfig.CourtContribution)),
                ("声望", nameof(FactionConfig.Reputation))),
            Group("标记", ("异族", nameof(FactionConfig.IsAlien)),
                ("玩家不可选", nameof(FactionConfig.NotPlayerSelectable)),
                ("自动拒绝赎回", nameof(FactionConfig.AutoRefuse)), ("已灭亡", nameof(FactionConfig.Destroyed))),
            // The reserved technique points, preferred technique kinds and the runtime lists/counters the game
            // maintains while playing are left untouched and not shown.
            Group("技巧", ("技巧点数", nameof(FactionConfig.TechniquePoint)),
                ("研究中", nameof(FactionConfig.UpgradingTechnique)),
                ("剩余天数", nameof(FactionConfig.UpgradingDaysLeft)),
                ("计划研究", nameof(FactionConfig.PlanTechniqueString)))
        ];
        ChipLists =
        [
            ChipList("领有建筑", nameof(FactionConfig.ArchitecturesString), "architectures"),
            ChipList("军团", nameof(FactionConfig.SectionsString), "sections"),
            ChipList("编队", nameof(FactionConfig.MilitariesString), "militaries"),
            ChipList("基本兵种", nameof(FactionConfig.BaseMilitaryKindsString), "military-kinds")
        ];
        RebuildTechniqueButtons();
        Initialize();
    }

    // 基本 is followed by the colour, then the remaining groups.
    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public IEnumerable<PersonFieldGroupViewModel> MiddleGroups => Groups.Skip(1);
    public IReadOnlyList<PersonIdChipListViewModel> TerritoryLists => ChipLists;
    public ObservableCollection<FactionTechniqueButtonViewModel> TechniqueButtons { get; } = [];
    public string TechniqueStatus => GetTechniqueStatus();

    public IReadOnlyList<PersonColorOption> ColorOptions => _colorOptions;
    public PersonColorOption? SelectedColor
    {
        get => SelectedConfig is { } faction ? _colorOptions.FirstOrDefault(option => option.Id == faction.ColorIndex) : null;
        set
        {
            if (value is null || _refreshingColor || SelectedRecord is not { } selected ||
                ((FactionConfig)selected.Record.Item).ColorIndex == value.Id) return;
            Context.SetPropertyValue(selected.Record, nameof(FactionConfig.ColorIndex), value.Id, "修改势力颜色");
            RefreshColor();
        }
    }
    public IBrush? ColorSwatch => SelectedColor?.Swatch;

    protected override string GetName(FactionConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(FactionConfig.LeaderID) => () => TargetOptions("persons"),
        nameof(FactionConfig.PrinceID) => () => TargetOptions("persons", None),
        nameof(FactionConfig.CapitalID) => () => TargetOptions("architectures"),
        nameof(FactionConfig.OfficialRank) => () => TargetOptions("official-title-kinds"),
        nameof(FactionConfig.UpgradingTechnique) or nameof(FactionConfig.PlanTechniqueString) =>
            () => TargetOptions("techniques", None),
        _ => null
    };

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        RefreshColor();
        RefreshTechniques();
    }

    protected override void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        base.OnReferenceOptionsChanged(sender, e);
        RefreshColor();
        RebuildTechniqueButtons();
    }

    // Researched techniques are toggled like a person's skills, in the order of the technique tree.
    private void RebuildTechniqueButtons()
    {
        TechniqueButtons.Clear();
        var techniques = Targets("techniques").GroupBy(target => target.Id).Select(group => group.First())
            .Select(target => (target.Id, target.DisplayName, Technique: target.Item as TechniqueConfig))
            .OrderBy(item => item.Technique?.Kind ?? int.MaxValue)
            .ThenBy(item => item.Technique?.DisplayRow ?? 0)
            .ThenBy(item => item.Technique?.DisplayCol ?? 0)
            .ThenBy(item => item.Id);
        foreach (var (id, name, technique) in techniques)
            TechniqueButtons.Add(new FactionTechniqueButtonViewModel(id, name, TechniqueTip(technique), this));
        RefreshTechniques();
    }

    private static string? TechniqueTip(TechniqueConfig? technique) => technique is null
        ? null
        : string.Join("\n", new[]
        {
            technique.Description,
            string.Create(CultureInfo.InvariantCulture,
                $"技巧点数 {technique.PointCost} · 资金 {technique.FundCost} · 声望 {technique.Reputation} · {technique.Days} 天")
        }.Where(line => !string.IsNullOrWhiteSpace(line)));

    private ConfigStructuredStringParseResult<int>? ParseTechniques() => SelectedConfig is { } faction
        ? ConfigStructuredStringCodec.ParseIds(faction.AvailableTechniquesString)
        : null;

    internal bool CanToggleTechniques => ParseTechniques() is { IsValid: true } && TechniqueButtons.Count > 0;

    internal bool HasTechnique(int id) => ParseTechniques() is { IsValid: true } parsed && parsed.Items.Contains(id);

    internal void ToggleTechnique(int id, bool selected)
    {
        if (!CanToggleTechniques || SelectedRecord is not { } record) return;
        var ids = ParseTechniques()!.Items;
        if (ids.Contains(id) == selected) return;
        var updated = selected ? ids.Append(id) : ids.Where(value => value != id);
        Context.SetPropertyValue(record.Record, nameof(FactionConfig.AvailableTechniquesString),
            ConfigStructuredStringCodec.FormatIds(updated), $"{(selected ? "添加" : "移除")}势力技巧");
        RefreshTechniques();
    }

    private string GetTechniqueStatus()
    {
        if (SelectedConfig is null) return "请先选择势力。";
        if (TechniqueButtons.Count == 0) return "打开 CommonData 后可按名称选择技巧。";
        var parsed = ParseTechniques()!;
        if (!parsed.IsValid)
            return $"技巧 ID 列表格式有误：{string.Join("；", parsed.Errors)} 请先在通用编辑器中修正。";
        var known = TechniqueButtons.Select(button => button.Id).ToHashSet();
        var unknown = parsed.Items.Where(id => !known.Contains(id)).Distinct().ToArray();
        var summary = $"已研究 {parsed.Items.Distinct().Count(known.Contains)} / {TechniqueButtons.Count}。点击技巧即可添加或移除，亮色表示已研究。";
        return unknown.Length == 0
            ? summary
            : $"{summary}未收录在技巧目录中的 ID：{string.Join("、", unknown)}（编辑时会保留）。";
    }

    private void RefreshTechniques()
    {
        OnPropertyChanged(nameof(TechniqueStatus));
        foreach (var button in TechniqueButtons) button.Refresh();
    }

    // Colours come from the CommonData colour table; ColorIndex is the table position.
    private void RefreshColor()
    {
        var options = Targets("colors")
            .GroupBy(target => target.Id).Select(group => group.First()).OrderBy(target => target.Id)
            .Select(target => target.Item is FactionColorConfig color
                ? new PersonColorOption(target.Id, $"{target.Id}  {color.Name}",
                    new SolidColorBrush(Color.FromArgb(255, (byte)Math.Clamp(color.R, 0, 255),
                        (byte)Math.Clamp(color.G, 0, 255), (byte)Math.Clamp(color.B, 0, 255))))
                : new PersonColorOption(target.Id, $"{target.Id}", null))
            .ToList();
        if (SelectedConfig is { } faction && options.All(option => option.Id != faction.ColorIndex))
            options.Insert(0, new PersonColorOption(faction.ColorIndex, $"{faction.ColorIndex} （未收录）", null));
        // Swatch brushes are new instances each time, so compare by ID and label only; keeping the same list
        // stops the ComboBox from writing the previous faction's colour into the newly selected one.
        if (!options.Select(option => (option.Id, option.Label))
                .SequenceEqual(_colorOptions.Select(option => (option.Id, option.Label))))
        {
            _refreshingColor = true;
            try
            {
                _colorOptions = options;
                OnPropertyChanged(nameof(ColorOptions));
            }
            finally
            {
                _refreshingColor = false;
            }
        }
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(ColorSwatch));
    }
}

public sealed class FactionTechniqueButtonViewModel(int id, string name, string? tip, FactionEditorViewModel owner)
    : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string? Tip { get; } = tip;
    public bool IsSelected
    {
        get => owner.HasTechnique(Id);
        set => owner.ToggleTechnique(Id, value);
    }
    public bool CanToggle => owner.CanToggleTechniques;
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanToggle));
    }
}
