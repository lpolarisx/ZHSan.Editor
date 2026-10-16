using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Configuration;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class PersonBasicEditorViewModel : ObservableObject, IIdChipListOwner, IDisposable
{
    private readonly ConfigEditorContext _context;
    private readonly ConfigDocumentViewModel? _biographyDocument;
    private readonly PersonPortraitDirectoryStore _portraits;
    private Bitmap? _portrait;
    private string? _portraitPath;
    private bool _portraitLoading;
    private int _portraitVersion;
    private bool _disposed;
    private ConfigRecordViewModel? _biographyRecord;
    private readonly ConfigDocumentViewModel? _familyDocument;
    private ConfigRecordViewModel? _familyRecord;
    private IReadOnlyList<PersonColorOption> _factionColorOptions = [];
    private bool _refreshingFactionColor;
    private IReadOnlyList<PersonChoiceOption> _personChoices = [];
    private PersonOptionViewModel? _selectedPerson;
    private string _searchText = string.Empty;
    private bool _synchronizing;
    private int _selectedTabIndex;
    private readonly HashSet<int> _staleTabs = [];
    private bool _catalogsStale;

    public PersonBasicEditorViewModel(ConfigEditorContext context, PersonPortraitDirectoryStore? portraits = null)
    {
        _context = context;
        _portraits = portraits ?? PersonPortraitDirectoryStore.InMemory();
        _biographyDocument = context.FindDocument("biographies");
        _familyDocument = context.FindDocument("person-families");
        BiographyFields = [
            new BiographyTextFieldViewModel("简介", nameof(BiographyConfig.Brief), () => _biographyRecord, _biographyDocument),
            new BiographyTextFieldViewModel("演义", nameof(BiographyConfig.Romance), () => _biographyRecord, _biographyDocument),
            new BiographyTextFieldViewModel("历史", nameof(BiographyConfig.History), () => _biographyRecord, _biographyDocument),
            new BiographyTextFieldViewModel("游戏列传", nameof(BiographyConfig.InGame), () => _biographyRecord, _biographyDocument)
        ];
        AddBiographyCommand = new RelayCommand(AddBiography, () => CanAddBiography);
        Groups =
        [
            Group("身份", ("姓", nameof(PersonConfig.SurName)), ("名", nameof(PersonConfig.GivenName)),
                ("字", nameof(PersonConfig.CalledName)), ("头像序号", nameof(PersonConfig.PictureIndex)),
                ("性别", nameof(PersonConfig.Sex)), ("相性", nameof(PersonConfig.Ideal)),
                ("仕官倾向", nameof(PersonConfig.IdealTendencyIDString))),
            Group("生平", ("出生地", nameof(PersonConfig.BornRegion)),
                ("出场年", nameof(PersonConfig.YearAvailable)), ("出生年", nameof(PersonConfig.YearBorn)),
                ("死亡年", nameof(PersonConfig.YearDead)), ("死因", nameof(PersonConfig.DeadReason)),
                ("血缘", nameof(PersonConfig.Strain)), ("世代", nameof(PersonConfig.Generation)))
                .WithNote(GetLifeNote),
            Group("性格与倾向", ("勇猛度", nameof(PersonConfig.BaseBraveness)),
                ("冷静度", nameof(PersonConfig.BaseCalmness)),
                ("性格", nameof(PersonConfig.PCharacter)),
                ("义理", nameof(PersonConfig.PersonalLoyalty)),
                ("野心", nameof(PersonConfig.Ambition)),
                ("起用", nameof(PersonConfig.Qualification)),
                ("汉室", nameof(PersonConfig.ValuationOnGovernment)),
                ("战略倾向", nameof(PersonConfig.StrategyTendency))),
            Group("仕官与状态", ("仕官势力", nameof(PersonConfig.JoinFactionID)),
                ("登场地点", nameof(PersonConfig.AvailableLocation)),
                ("仕官年份", nameof(PersonConfig.YearJoin)),
                ("名声", nameof(PersonConfig.Reputation)),
                ("功绩", nameof(PersonConfig.OfficerMerit)),
                ("存活", nameof(PersonConfig.Alive)), ("可用", nameof(PersonConfig.Available)))
        ];
        AbilityColumns =
        [
            AbilityColumn("武勇", nameof(PersonConfig.BaseStrength), "武经", nameof(PersonConfig.StrengthExperience)),
            AbilityColumn("统率", nameof(PersonConfig.BaseCommand), "统经", nameof(PersonConfig.CommandExperience)),
            AbilityColumn("智谋", nameof(PersonConfig.BaseIntelligence), "智经", nameof(PersonConfig.IntelligenceExperience)),
            AbilityColumn("政治", nameof(PersonConfig.BasePolitics), "政经", nameof(PersonConfig.PoliticsExperience)),
            AbilityColumn("魅力", nameof(PersonConfig.BaseGlamour), "魅经", nameof(PersonConfig.GlamourExperience))
        ];
        FamilyReferenceFields =
        [
            new PersonReferenceFieldViewModel("父亲", nameof(PersonFamilyConfig.FatherId), this),
            new PersonReferenceFieldViewModel("母亲", nameof(PersonFamilyConfig.MotherId), this),
            new PersonReferenceFieldViewModel("配偶", nameof(PersonFamilyConfig.SpouseId), this)
        ];
        FamilyListFields =
        [
            new PersonIdListFieldViewModel("义兄", nameof(PersonFamilyConfig.BrotherIdsString), this),
            new PersonIdListFieldViewModel("亲近人物", nameof(PersonFamilyConfig.CloseIdsString), this),
            new PersonIdListFieldViewModel("厌恶人物", nameof(PersonFamilyConfig.HatedIdsString), this)
        ];
        // PersonalTitleString / CombatTitleString are legacy fields the game no longer reads; they are
        // left untouched and not shown.
        StudyingTitleField = Field("学习中", nameof(PersonConfig.StudyingTitleString));
        ExclusiveLists =
        [
            new PersonIdChipListViewModel("专属称号", nameof(PersonConfig.UniqueTitlesString), "titles", this),
            new PersonIdChipListViewModel("专属兵种", nameof(PersonConfig.UniqueMilitaryKindsString), "military-kinds", this)
        ];
        ExperienceGroup = Group("经验",
            ("战法", nameof(PersonConfig.TacticsExperience)),
            ("计略", nameof(PersonConfig.StratagemExperience)),
            ("步兵", nameof(PersonConfig.BubingExperience)),
            ("骑兵", nameof(PersonConfig.QibingExperience)),
            ("弩兵", nameof(PersonConfig.NubingExperience)),
            ("器械", nameof(PersonConfig.QixieExperience)),
            ("水军", nameof(PersonConfig.ShuijunExperience)));
        Messages = new PersonMessagesViewModel(context, () => SelectedPerson?.Config);
        RebuildSkillButtons();
        RebuildTitleCatalog();
        _context.Document.ReferenceOptionsChanged += OnReferenceOptionsChanged;
        if (_biographyDocument is not null) _biographyDocument.StateChanged += OnBiographyStateChanged;
        if (_familyDocument is not null) _familyDocument.StateChanged += OnFamilyStateChanged;
        _context.Document.StateChanged += OnDocumentStateChanged;
        _context.Document.PropertyChanged += OnDocumentPropertyChanged;
        RebuildPeople();
    }

    public ObservableCollection<PersonOptionViewModel> People { get; } = [];
    public ObservableCollection<PersonOptionViewModel> FilteredPeople { get; } = [];
    public IReadOnlyList<PersonFieldGroupViewModel> Groups { get; }
    public IReadOnlyList<PersonAbilityColumnViewModel> AbilityColumns { get; }
    // 身份 sits beside the portrait and 生平 follows above the ability table; the remaining groups come after it.
    public PersonFieldGroupViewModel IdentityGroup => Groups[0];
    public IEnumerable<PersonFieldGroupViewModel> LeadingGroups => Groups.Skip(1).Take(1);
    public IEnumerable<PersonFieldGroupViewModel> TrailingGroups => Groups.Skip(2);
    public IEnumerable<PersonBasicFieldViewModel> AllFields => BasicTabFields.Concat(SkillTabFields);
    private IEnumerable<PersonBasicFieldViewModel> BasicTabFields => Groups.SelectMany(group => group.Fields)
        .Concat(AbilityColumns.SelectMany(column => new[] { column.Ability, column.Experience }));
    private IEnumerable<PersonBasicFieldViewModel> SkillTabFields => ExperienceGroup.Fields.Append(StudyingTitleField);

    public const int BasicTab = 0;
    public const int SkillTab = 1;
    public const int MessageTab = 2;

    // Only the visible tab is refreshed when the person or the referenced data changes; the others are
    // refreshed when they are shown.
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value) && _staleTabs.Remove(value)) RefreshTab(value);
        }
    }
    public ObservableCollection<PersonTitleSlotViewModel> TitleSlots { get; } = [];
    public string TitleStatus => GetTitleStatus();
    public PersonBasicFieldViewModel StudyingTitleField { get; }
    public IReadOnlyList<PersonIdChipListViewModel> ExclusiveLists { get; }
    public PersonFieldGroupViewModel ExperienceGroup { get; }
    public ObservableCollection<PersonSkillButtonViewModel> SkillButtons { get; } = [];
    public string SkillStatus => GetSkillStatus();
    public IReadOnlyList<BiographyTextFieldViewModel> BiographyFields { get; }
    public PersonMessagesViewModel Messages { get; }
    public ICommand AddBiographyCommand { get; }
    public bool HasBiography => _biographyRecord is not null;
    public bool CanAddBiography => SelectedPerson is not null && _biographyDocument is not null &&
        !_biographyDocument.HasNullRecords && _biographyDocument.Records.All(record =>
            record.Item is not BiographyConfig biography || biography.Id != SelectedPerson.Config.Id);
    public string BiographyStatus => GetBiographyStatus();
    public decimal? FactionColor
    {
        get => _biographyRecord?.Item is BiographyConfig biography ? biography.FactionColor : null;
        set
        {
            if (value is not { } number || number != decimal.Truncate(number) || number < 0 || number > int.MaxValue ||
                _biographyRecord is not { Item: BiographyConfig biography } record || _biographyDocument is null ||
                biography.FactionColor == (int)number) return;
            _biographyDocument.SetPropertyValue(record, nameof(BiographyConfig.FactionColor), (int)number, "修改势力颜色");
            RefreshFactionColor();
        }
    }
    public IReadOnlyList<PersonColorOption> FactionColorOptions => _factionColorOptions;
    public PersonColorOption? SelectedFactionColor
    {
        get => FactionColor is { } id ? _factionColorOptions.FirstOrDefault(option => option.Id == id) : null;
        set { if (value is not null && !_refreshingFactionColor) FactionColor = value.Id; }
    }
    public IReadOnlyList<PersonReferenceFieldViewModel> FamilyReferenceFields { get; }
    public IReadOnlyList<PersonIdListFieldViewModel> FamilyListFields { get; }
    public bool CanEditFamily => SelectedPerson is not null && _familyDocument is not null &&
        !_familyDocument.HasNullRecords && FindFamilyRecords().Length <= 1;
    public string FamilyStatus => GetFamilyStatus();
    internal IReadOnlyList<PersonChoiceOption> PersonChoices => _personChoices;
    public bool HasSelection => SelectedPerson is not null;
    public string SelectedTitle => SelectedPerson?.Name ?? "请选择人物";
    public string SelectedId => SelectedPerson is null ? "" : $"ID {SelectedPerson.Config.Id}";
    public string CountText => $"{FilteredPeople.Count} / {People.Count} 人";

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    public PersonOptionViewModel? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (!SetProperty(ref _selectedPerson, value)) return;
            if (!_synchronizing) _context.SelectedRecord = value?.Record;
            RefreshSelection();
        }
    }

    public Bitmap? Portrait => _portrait;
    public bool HasPortrait => _portrait is not null;
    public string? PortraitDirectory
    {
        get => _portraits.Directory;
        set
        {
            if (string.Equals(_portraits.Directory, value, StringComparison.OrdinalIgnoreCase)) return;
            _portraits.Directory = value;
            OnPropertyChanged();
            RefreshPortrait();
        }
    }
    // The game keeps a large portrait ({index}.jpg) and a small one ({index}s.jpg); only the large one is shown.
    public string? PortraitFileName => SelectedPerson is { } person
        ? $"{person.Config.PictureIndex.ToString(CultureInfo.InvariantCulture)}.jpg"
        : null;
    public string? PortraitPath => PortraitDirectory is { Length: > 0 } directory && PortraitFileName is { } fileName
        ? Path.Combine(directory, fileName)
        : null;
    public string PortraitHint => PortraitDirectory is not { Length: > 0 }
        ? "点击选择头像文件夹"
        : HasPortrait || _portraitLoading ? "" : $"未找到 {PortraitFileName}\n点击更换文件夹";
    public string PortraitTip => PortraitPath is { } path
        ? $"{path}\n点击更换头像文件夹"
        : "选择游戏的头像文件夹，例如 Content\\Textures\\GameComponents\\PersonPortrait\\Images\\Default";

    // Portraits are 1500×1500 JPEGs; decoding happens off the UI thread so switching people stays responsive.
    private void RefreshPortrait()
    {
        var path = PortraitPath;
        if (path != _portraitPath || _portrait is null && !_portraitLoading)
        {
            var previous = _portrait;
            _portrait = null;
            _portraitPath = path;
            var version = ++_portraitVersion;
            _portraitLoading = path is not null && File.Exists(path);
            RaisePortraitChanged();
            previous?.Dispose();
            if (_portraitLoading) _ = LoadPortraitAsync(path!, version);
            return;
        }

        RaisePortraitChanged();
    }

    private async Task LoadPortraitAsync(string path, int version)
    {
        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 320);
            });
        }
        catch
        {
            bitmap = null;
        }

        if (_disposed || version != _portraitVersion)
        {
            bitmap?.Dispose();
            return;
        }

        _portrait = bitmap;
        _portraitLoading = false;
        RaisePortraitChanged();
    }

    private void RaisePortraitChanged()
    {
        OnPropertyChanged(nameof(Portrait));
        OnPropertyChanged(nameof(HasPortrait));
        OnPropertyChanged(nameof(PortraitFileName));
        OnPropertyChanged(nameof(PortraitPath));
        OnPropertyChanged(nameof(PortraitHint));
        OnPropertyChanged(nameof(PortraitTip));
    }

    private PersonFieldGroupViewModel Group(string title, params (string Label, string Property)[] fields) =>
        new(title, fields.Select(field => Field(field.Label, field.Property)).ToArray());

    private PersonBasicFieldViewModel Field(string label, string propertyName) =>
        new(label, propertyName, () => SelectedPerson?.Record, _context, GetChoiceProvider(propertyName),
            GetRange(propertyName));

    // Advisory ranges taken from the game: values outside them are flagged, never clamped.
    private const int GameMaxAbility = 150;

    private static PersonValueRange? GetRange(string propertyName) => propertyName switch
    {
        nameof(PersonConfig.BaseStrength) or nameof(PersonConfig.BaseCommand) or
            nameof(PersonConfig.BaseIntelligence) or nameof(PersonConfig.BasePolitics) or
            nameof(PersonConfig.BaseGlamour) =>
            new(0, GameMaxAbility, $"游戏能力上限默认为 {GameMaxAbility}（GlobalVariables.MaxAbility），超出部分不生效。"),
        nameof(PersonConfig.Ideal) => new(0, 150, "相性取值 0–150，游戏按 150 循环计算相性差。"),
        nameof(PersonConfig.BaseBraveness) or nameof(PersonConfig.BaseCalmness) =>
            new(0, 10, "游戏数据及子嗣生成均使用 0–10。"),
        _ => null
    };

    private PersonAbilityColumnViewModel AbilityColumn(
        string abilityLabel, string abilityProperty, string experienceLabel, string experienceProperty) =>
        new(abilityLabel, Field(abilityLabel, abilityProperty), Field(experienceLabel, experienceProperty));

    private PersonGroupNote? GetLifeNote()
    {
        if (SelectedPerson?.Config is not { } person) return null;
        var parts = new List<string>
        {
            $"出场时 {person.YearAvailable - person.YearBorn} 岁",
            $"享年 {person.YearDead - person.YearBorn} 岁"
        };
        var warnings = new List<string>();
        if (person.YearDead < person.YearBorn) warnings.Add("死亡年早于出生年");
        if (person.YearAvailable < person.YearBorn) warnings.Add("出场年早于出生年");
        if (person.YearAvailable > person.YearDead) warnings.Add("出场年晚于死亡年");
        return new PersonGroupNote(string.Join(" · ", parts.Concat(warnings)), warnings.Count > 0);
    }

    private static readonly IReadOnlyList<PersonChoiceOption> LevelOptions =
        [new(0, "很低"), new(1, "低"), new(2, "普通"), new(3, "高"), new(4, "很高")];

    private Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(PersonConfig.PersonalLoyalty) or nameof(PersonConfig.Ambition) => () => LevelOptions,
        nameof(PersonConfig.PCharacter) => () => [..ReferenceOptions(_context.GetReferenceTargets("character-kinds"))],
        nameof(PersonConfig.IdealTendencyIDString) => () => [new(-1, "-1 无"), ..ReferenceOptions(
            _context.GetReferenceTargets("ideal-tendency-kinds"),
            target => target.Item is IdealTendencyKindConfig kind ? $" （偏差 {kind.Offset}）" : "")],
        nameof(PersonConfig.AvailableLocation) => () => [new(-1, "-1 无"), ..ReferenceOptions(
            _context.GetReferenceTargets(new ConfigAddress(ConfigScope.Scenario, "architectures")))],
        nameof(PersonConfig.StudyingTitleString) => () => [new(-1, "无"), .._titles.Values
            .OrderBy(title => title.KindId).ThenBy(title => title.Level).ThenBy(title => title.Id)
            .Select(title => new PersonChoiceOption(title.Id, StudyingTitleLabel(title)))],
        _ => null
    };

    private static IEnumerable<PersonChoiceOption> ReferenceOptions(
        IEnumerable<ConfigReferenceTarget> targets, Func<ConfigReferenceTarget, string>? suffix = null) =>
        targets.GroupBy(target => target.Id).Select(group => group.First()).OrderBy(target => target.Id)
            .Select(target => new PersonChoiceOption(target.Id, $"{IdLabel(target)}{suffix?.Invoke(target)}"));

    // Some kind names already carry their ID (e.g. "2-比较随意"); avoid showing it twice.
    private static string IdLabel(ConfigReferenceTarget target)
    {
        var id = target.Id.ToString(CultureInfo.InvariantCulture);
        return target.DisplayName.StartsWith(id, StringComparison.Ordinal) &&
               (target.DisplayName.Length == id.Length || !char.IsDigit(target.DisplayName[id.Length]))
            ? target.DisplayName
            : $"{id} {target.DisplayName}";
    }

    private IReadOnlyDictionary<int, TitleInfo> _titles = new Dictionary<int, TitleInfo>();

    private sealed record TitleInfo(int Id, int KindId, int Level, string Name);

    // A person holds at most one title per kind (learning a title replaces the one of the same
    // kind), so RealTitlesString is edited as one choice per title kind.
    private void RebuildTitleCatalog()
    {
        var kindNames = _context.GetReferenceTargets("title-kinds")
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.First().DisplayName);
        // Kind names are not unique (e.g. two "合称" kinds); suffix the ID where they collide.
        var duplicateNames = kindNames.Values.GroupBy(name => name).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        foreach (var (id, name) in kindNames.ToArray())
            if (duplicateNames.Contains(name)) kindNames[id] = $"{name} {id}";
        _titles = _context.GetReferenceTargets("titles")
            .Where(target => target.Item is TitleConfig)
            .GroupBy(target => target.Id)
            .Select(group => (TitleConfig)group.First().Item)
            .ToDictionary(title => title.Id, title => new TitleInfo(title.Id, title.KindId, title.Level, title.Name));
        TitleSlots.Clear();
        foreach (var kind in _titles.Values.GroupBy(title => title.KindId).OrderBy(group => group.Key))
        {
            var name = kindNames.TryGetValue(kind.Key, out var kindName) ? kindName : $"种类 {kind.Key}";
            IReadOnlyList<PersonChoiceOption> options =
            [
                PersonTitleSlotViewModel.None,
                ..kind.OrderBy(title => title.Level).ThenBy(title => title.Id)
                    .Select(title => new PersonChoiceOption(title.Id, $"Lv{title.Level} · {title.Name}"))
            ];
            TitleSlots.Add(new PersonTitleSlotViewModel(kind.Key, name, options, this));
        }
        // The 学习中 options are built from this catalogue.
        StudyingTitleField.ReloadChoices();
        StudyingTitleField.Refresh();
        RefreshTitles();
    }

    internal string GetTitleKindName(int id) =>
        TitleSlots.FirstOrDefault(slot => slot.KindId == id)?.KindName ?? $"种类 {id}";

    private string StudyingTitleLabel(TitleInfo title) =>
        $"{GetTitleKindName(title.KindId)} · Lv{title.Level} · {title.Name}";

    internal string ChipLabel(string targetKey, ConfigReferenceTarget target) =>
        targetKey == "titles" && _titles.TryGetValue(target.Id, out var title)
            ? $"{title.Name}（{GetTitleKindName(title.KindId)} Lv{title.Level}）"
            : IdLabel(target);

    private ConfigStructuredStringParseResult<int>? ParseRealTitles() => SelectedPerson is { } person
        ? ConfigStructuredStringCodec.ParseIds(person.Config.RealTitlesString)
        : null;

    internal bool CanEditTitles => ParseRealTitles() is { IsValid: true } && _titles.Count > 0;

    internal int? GetTitleOfKind(int kindId) => ParseRealTitles() is { IsValid: true } parsed
        ? parsed.Items.Where(id => _titles.TryGetValue(id, out var title) && title.KindId == kindId)
            .Select(id => (int?)id).FirstOrDefault()
        : null;

    internal void SetTitleOfKind(int kindId, int titleId)
    {
        if (!CanEditTitles || SelectedPerson is not { } person) return;
        var ids = ParseRealTitles()!.Items;
        bool SameKind(int id) => _titles.TryGetValue(id, out var title) && title.KindId == kindId;
        var updated = new List<int>();
        var placed = titleId < 0;
        foreach (var id in ids)
        {
            if (!SameKind(id)) { updated.Add(id); continue; }
            if (!placed) { updated.Add(titleId); placed = true; }
        }
        if (!placed) updated.Add(titleId);
        if (updated.SequenceEqual(ids)) return;
        _context.SetPropertyValue(person.Record, nameof(PersonConfig.RealTitlesString),
            ConfigStructuredStringCodec.FormatIds(updated), "修改人物称号");
        RefreshTitles();
    }

    private string GetTitleStatus()
    {
        if (SelectedPerson is null) return "请先选择人物。";
        if (_titles.Count == 0) return "打开 CommonData 后可按种类选择称号。";
        var parsed = ParseRealTitles()!;
        if (!parsed.IsValid)
            return $"称号 ID 列表格式有误：{string.Join("；", parsed.Errors)} 请先在通用编辑器中修正。";
        var notes = new List<string> { "每个种类只能有一个称号，选择新称号会替换同种类的旧称号。" };
        var unknown = parsed.Items.Where(id => !_titles.ContainsKey(id)).Distinct().ToArray();
        if (unknown.Length > 0)
            notes.Add($"未收录在称号目录中的 ID：{string.Join("、", unknown)}（编辑时会保留）。");
        var duplicated = parsed.Items.Where(_titles.ContainsKey).GroupBy(id => _titles[id].KindId)
            .Where(group => group.Count() > 1).Select(group => GetTitleKindName(group.Key)).ToArray();
        if (duplicated.Length > 0)
            notes.Add($"{string.Join("、", duplicated)}有多个称号，修改该种类时只保留所选的一个。");
        return string.Join(" ", notes);
    }

    private void RefreshTitles()
    {
        OnPropertyChanged(nameof(TitleStatus));
        foreach (var slot in TitleSlots) slot.Refresh();
    }

    internal object? GetPersonValue(string propertyName) => SelectedPerson is { } person
        ? typeof(PersonConfig).GetProperty(propertyName)!.GetValue(person.Config)
        : null;

    internal void SetPersonValue(string propertyName, object value, string description)
    {
        if (SelectedPerson is not { } person) return;
        _context.SetPropertyValue(person.Record, propertyName, value, description);
        foreach (var list in ExclusiveLists) list.Refresh();
    }

    public IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey) =>
        [.._context.GetReferenceTargets(targetKey).GroupBy(target => target.Id).Select(group => group.First())
            .OrderBy(target => target.Id)
            .Select(target => new PersonChoiceOption(target.Id, ChipLabel(targetKey, target)))];

    public string GetChipLabel(string targetKey, int id) =>
        _context.GetReferenceTargets(targetKey).FirstOrDefault(target => target.Id == id) is { } target
            ? ChipLabel(targetKey, target)
            : $"{id} （未收录）";

    object? IIdChipListOwner.GetValue(string propertyName) => GetPersonValue(propertyName);
    void IIdChipListOwner.SetValue(string propertyName, object value, string description) =>
        SetPersonValue(propertyName, value, description);

    private void RebuildSkillButtons()
    {
        SkillButtons.Clear();
        foreach (var target in _context.GetReferenceTargets("skills").GroupBy(target => target.Id).Select(group => group.First()))
            SkillButtons.Add(new PersonSkillButtonViewModel(target.Id, target.DisplayName, this));
        RefreshSkills();
    }

    private string GetSkillStatus()
    {
        if (SelectedPerson is null) return "请先选择人物。";
        if (SkillButtons.Count == 0) return "打开 CommonData 后可按名称选择技能。";
        var parsed = ConfigStructuredStringCodec.ParseIds(SelectedPerson.Config.SkillsString);
        if (!parsed.IsValid)
            return $"技能 ID 列表格式有误：{string.Join("；", parsed.Errors)} 请先在通用编辑器中修正。";
        var knownIds = SkillButtons.Select(button => button.Id).ToHashSet();
        var unknownIds = parsed.Items.Where(id => !knownIds.Contains(id)).Distinct().ToArray();
        return unknownIds.Length == 0
            ? "点击技能按钮即可添加或移除；亮色表示已掌握。"
            : $"点击技能按钮即可添加或移除；未收录在技能目录中的 ID：{string.Join("、", unknownIds)}（编辑时会保留）。";
    }

    internal bool CanToggleSkills => SelectedPerson is not null && SkillButtons.Count > 0 &&
        ConfigStructuredStringCodec.ParseIds(SelectedPerson.Config.SkillsString).IsValid;

    internal bool HasSkill(int id) => SelectedPerson is not null &&
        ConfigStructuredStringCodec.ParseIds(SelectedPerson.Config.SkillsString).Items.Contains(id);

    internal void ToggleSkill(int id, bool selected)
    {
        if (!CanToggleSkills || SelectedPerson is not { } person) return;
        var ids = ConfigStructuredStringCodec.ParseIds(person.Config.SkillsString).Items;
        if (ids.Contains(id) == selected) return;
        var updated = selected ? ids.Append(id) : ids.Where(value => value != id);
        _context.SetPropertyValue(person.Record, nameof(PersonConfig.SkillsString),
            ConfigStructuredStringCodec.FormatIds(updated), $"{(selected ? "添加" : "移除")}人物技能");
        RefreshSkills();
    }

    private void RefreshSkills()
    {
        OnPropertyChanged(nameof(SkillStatus));
        foreach (var button in SkillButtons) button.Refresh();
    }

    // Raised after every edit in either open file (including CommonData's person messages), so the skill and
    // title catalogues are only rebuilt when their tab is shown.
    private void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        foreach (var field in AllFields) field.ReloadChoices();
        _catalogsStale = true;
        RefreshTabs();
    }

    private void RefreshTabs()
    {
        _staleTabs.UnionWith([BasicTab, SkillTab, MessageTab]);
        _staleTabs.Remove(SelectedTabIndex);
        RefreshTab(SelectedTabIndex);
    }

    private void RefreshTab(int tab)
    {
        switch (tab)
        {
            case BasicTab:
                foreach (var field in BasicTabFields) field.Refresh();
                foreach (var group in Groups) group.Refresh();
                RefreshPortrait();
                RefreshBiography();
                RefreshFamily();
                break;
            case SkillTab:
                if (_catalogsStale)
                {
                    _catalogsStale = false;
                    RebuildSkillButtons();
                    RebuildTitleCatalog();
                }
                foreach (var field in SkillTabFields) field.Refresh();
                RefreshTitles();
                foreach (var list in ExclusiveLists) list.Refresh();
                RefreshSkills();
                break;
            case MessageTab:
                Messages.Refresh();
                break;
        }
    }

    // Colours come from the CommonData colour table; the biography stores the table position.
    private void RefreshFactionColor()
    {
        var options = _context.GetReferenceTargets("colors")
            .GroupBy(target => target.Id).Select(group => group.First()).OrderBy(target => target.Id)
            .Select(target => target.Item is FactionColorConfig color
                ? new PersonColorOption(target.Id, $"{target.Id}  {color.Name}",
                    new SolidColorBrush(Color.FromArgb(255, (byte)Math.Clamp(color.R, 0, 255),
                        (byte)Math.Clamp(color.G, 0, 255), (byte)Math.Clamp(color.B, 0, 255))))
                : new PersonColorOption(target.Id, $"{target.Id}", null))
            .ToList();
        if (FactionColor is { } current && options.All(option => option.Id != current))
            options.Insert(0, new PersonColorOption((int)current, $"{current} （未收录）", null));
        // Swatch brushes are new instances each time, so compare by ID and label only.
        if (!options.Select(option => (option.Id, option.Label))
                .SequenceEqual(_factionColorOptions.Select(option => (option.Id, option.Label))))
        {
            _refreshingFactionColor = true;
            try
            {
                _factionColorOptions = options;
                OnPropertyChanged(nameof(FactionColorOptions));
            }
            finally
            {
                _refreshingFactionColor = false;
            }
        }
        OnPropertyChanged(nameof(FactionColor));
        OnPropertyChanged(nameof(SelectedFactionColor));
    }

    private void RebuildPeople()
    {
        var selectedRecord = _context.SelectedRecord ?? SelectedPerson?.Record;
        People.Clear();
        foreach (var record in _context.Records.Where(record => record.Item is PersonConfig))
            People.Add(new PersonOptionViewModel(record));
        RebuildPersonChoices();
        ApplyFilter();
        SelectWithoutDocumentUpdate(People.FirstOrDefault(person => ReferenceEquals(person.Record, selectedRecord))
            ?? People.FirstOrDefault());
        if (_context.SelectedRecord is null && SelectedPerson is not null)
            _context.SelectedRecord = SelectedPerson.Record;
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var matches = People.Where(person => query.Length == 0 ||
            person.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            person.Config.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal))
            .ToArray();
        for (var index = FilteredPeople.Count - 1; index >= 0; index--)
            if (!matches.Contains(FilteredPeople[index])) FilteredPeople.RemoveAt(index);
        for (var index = 0; index < matches.Length; index++)
        {
            if (index < FilteredPeople.Count && ReferenceEquals(FilteredPeople[index], matches[index]))
                continue;
            var previous = FilteredPeople.IndexOf(matches[index]);
            if (previous >= 0) FilteredPeople.Move(previous, index);
            else FilteredPeople.Insert(index, matches[index]);
        }
        OnPropertyChanged(nameof(CountText));
    }

    private void SelectWithoutDocumentUpdate(PersonOptionViewModel? person)
    {
        _synchronizing = true;
        SelectedPerson = person;
        _synchronizing = false;
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedId));
        RefreshTabs();
    }

    private void RefreshBiography()
    {
        var matches = _biographyDocument?.Records
            .Where(record => record.Item is BiographyConfig biography &&
                biography.Id == SelectedPerson?.Config.Id)
            .ToArray() ?? [];
        _biographyRecord = matches.Length == 1 ? matches[0] : null;
        OnPropertyChanged(nameof(HasBiography));
        OnPropertyChanged(nameof(CanAddBiography));
        OnPropertyChanged(nameof(BiographyStatus));
        RefreshFactionColor();
        ((RelayCommand)AddBiographyCommand).RaiseCanExecuteChanged();
        foreach (var field in BiographyFields) field.Refresh();
    }

    private string GetBiographyStatus()
    {
        if (SelectedPerson is null) return "请先选择人物。";
        if (_biographyDocument is null) return "当前文件没有列传配置。";
        if (_biographyDocument.HasNullRecords) return "列传包含 null 记录，请先在列传配置中处理。";
        var count = _biographyDocument.Records.Count(record =>
            record.Item is BiographyConfig biography && biography.Id == SelectedPerson.Config.Id);
        return count switch
        {
            0 => "这名人物尚无列传，可新建一条。",
            > 1 => "找到重复的人物列传 ID，请先在列传配置中处理。",
            _ => "列传保存在 Biographies.json；在此修改可用主窗口保存按钮一起保存。"
        };
    }

    private void AddBiography()
    {
        if (!CanAddBiography || SelectedPerson is null || _biographyDocument is null) return;
        if (_biographyDocument.CanInitializeEntry)
            _biographyDocument.InitializeEntryCommand.Execute(null);
        _biographyDocument.AddInitializedRecord(
            new Dictionary<string, object?>
            {
                [nameof(BiographyConfig.Id)] = SelectedPerson.Config.Id,
                [nameof(BiographyConfig.Brief)] = string.Empty,
                [nameof(BiographyConfig.Romance)] = string.Empty,
                [nameof(BiographyConfig.History)] = string.Empty,
                [nameof(BiographyConfig.InGame)] = string.Empty
            },
            $"新增人物 {SelectedPerson.Config.Id} 的列传");
        RefreshBiography();
    }

    private void OnBiographyStateChanged(object? sender, EventArgs e) => RefreshBiography();
    private void OnFamilyStateChanged(object? sender, EventArgs e) => RefreshFamily();

    private void RebuildPersonChoices() =>
        _personChoices = People.Select(person => new PersonChoiceOption(
            person.Config.Id, $"{person.Config.Id} {person.Name}")).ToArray();

    internal string GetPersonLabel(int id) =>
        _personChoices.FirstOrDefault(option => option.Id == id)?.Label ?? $"{id} （未知人物）";

    private ConfigRecordViewModel[] FindFamilyRecords() =>
        _familyDocument?.Records
            .Where(record => record.Item is PersonFamilyConfig family && family.Id == SelectedPerson?.Config.Id)
            .ToArray() ?? [];

    private void RefreshFamily()
    {
        var matches = FindFamilyRecords();
        _familyRecord = matches.Length == 1 ? matches[0] : null;
        OnPropertyChanged(nameof(CanEditFamily));
        OnPropertyChanged(nameof(FamilyStatus));
        foreach (var field in FamilyReferenceFields) field.Refresh();
        foreach (var field in FamilyListFields) field.Refresh();
    }

    private string GetFamilyStatus()
    {
        if (SelectedPerson is null) return "请先选择人物。";
        if (_familyDocument is null) return "当前文件没有人物亲属配置（GameScenarios.json）。";
        if (_familyDocument.HasNullRecords) return "人物亲属包含 null 记录，请先在人物亲属配置中处理。";
        return FindFamilyRecords().Length > 1
            ? "找到重复的人物亲属 ID，请先在人物亲属配置中处理。"
            : "亲属与人际关系保存在 GameScenarios.json；修改可撤销，使用主窗口保存按钮写入。";
    }

    internal object? GetFamilyValue(string propertyName) =>
        _familyRecord?.Item is PersonFamilyConfig family
            ? typeof(PersonFamilyConfig).GetProperty(propertyName)!.GetValue(family)
            : null;

    internal void SetFamilyValue(string propertyName, object value, string description)
    {
        if (!CanEditFamily || SelectedPerson is not { } person || _familyDocument is null) return;
        if (_familyRecord is { } record)
        {
            if (Equals(GetFamilyValue(propertyName), value)) return;
            _familyDocument.SetPropertyValue(record, propertyName, value, description);
        }
        else
        {
            if (_familyDocument.CanInitializeEntry)
                _familyDocument.InitializeEntryCommand.Execute(null);
            var defaults = new PersonFamilyConfig { Id = person.Config.Id };
            var values = typeof(PersonFamilyConfig).GetProperties()
                .ToDictionary(property => property.Name, property => property.GetValue(defaults));
            values[propertyName] = value;
            _familyDocument.AddInitializedRecord(values, description);
        }
        RefreshFamily();
    }
    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigDocumentViewModel.SelectedRecord) && !_synchronizing)
            SelectWithoutDocumentUpdate(
                People.FirstOrDefault(person => ReferenceEquals(person.Record, _context.SelectedRecord)));
    }

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        if (People.Count != _context.Records.Count ||
            People.Where((person, index) => !ReferenceEquals(person.Record, _context.Records[index])).Any())
        {
            RebuildPeople();
            return;
        }
        foreach (var person in People) person.Refresh();
        RebuildPersonChoices();
        ApplyFilter();
        RefreshSelection();
    }

    public void Dispose()
    {
        if (_biographyDocument is not null) _biographyDocument.StateChanged -= OnBiographyStateChanged;
        if (_familyDocument is not null) _familyDocument.StateChanged -= OnFamilyStateChanged;
        _context.Document.ReferenceOptionsChanged -= OnReferenceOptionsChanged;
        _context.Document.StateChanged -= OnDocumentStateChanged;
        _context.Document.PropertyChanged -= OnDocumentPropertyChanged;
        _disposed = true;
        _portrait?.Dispose();
        _portrait = null;
    }
}

public sealed class PersonOptionViewModel(ConfigRecordViewModel record) : ObservableObject
{
    public ConfigRecordViewModel Record { get; } = record;
    public PersonConfig Config => (PersonConfig)Record.Item;
    public string Name => $"{Config.SurName}{Config.GivenName}";
    public string Label => $"{Name}  ·  {Config.Id}";
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Label));
    }
}

public sealed class PersonSkillButtonViewModel(int id, string name, PersonBasicEditorViewModel owner) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public bool IsSelected
    {
        get => owner.HasSkill(Id);
        set => owner.ToggleSkill(Id, value);
    }
    public bool CanToggle => owner.CanToggleSkills;
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanToggle));
    }
}
public sealed record PersonGroupNote(string Text, bool IsWarning);

public sealed record PersonValueRange(int Minimum, int Maximum, string Description);

public sealed class PersonFieldGroupViewModel(string title, IReadOnlyList<PersonBasicFieldViewModel> fields)
    : ObservableObject
{
    private Func<PersonGroupNote?>? _noteProvider;
    private PersonGroupNote? _note;

    public string Title { get; } = title;
    public IReadOnlyList<PersonBasicFieldViewModel> Fields { get; } = fields;
    public string Note => _note?.Text ?? "";
    public bool HasNote => _note is not null;
    public bool IsWarning => _note?.IsWarning == true;

    internal PersonFieldGroupViewModel WithNote(Func<PersonGroupNote?> noteProvider)
    {
        _noteProvider = noteProvider;
        return this;
    }

    public void Refresh()
    {
        _note = _noteProvider?.Invoke();
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(IsWarning));
    }
}

public sealed record PersonAbilityColumnViewModel(
    string Label, PersonBasicFieldViewModel Ability, PersonBasicFieldViewModel Experience);

public sealed class PersonBasicFieldViewModel : ObservableObject
{
    private readonly PropertyInfo _property;
    private readonly Func<ConfigRecordViewModel?> _record;
    private readonly ConfigEditorContext _context;
    private readonly Func<IReadOnlyList<PersonChoiceOption>>? _choices;
    private IReadOnlyList<PersonChoiceOption> _choiceOptions = [];
    private IReadOnlyList<PersonChoiceOption>? _baseChoices;
    private readonly PersonValueRange? _range;
    private readonly string _subject;

    private bool _refreshing;

    // Also used by other editors (e.g. treasures): itemType and subject name the edited record kind.
    public PersonBasicFieldViewModel(string label, string propertyName,
        Func<ConfigRecordViewModel?> record, ConfigEditorContext context,
        Func<IReadOnlyList<PersonChoiceOption>>? choices = null, PersonValueRange? range = null,
        Type? itemType = null, string subject = "人物")
    {
        _range = range;
        _subject = subject;
        Label = label;
        itemType ??= typeof(PersonConfig);
        _property = itemType.GetProperty(propertyName)
            ?? throw new ArgumentException($"未知{subject}字段：{propertyName}");
        _record = record;
        _context = context;
        IsText = _property.PropertyType == typeof(string);
        IsSex = itemType == typeof(PersonConfig) && propertyName == nameof(PersonConfig.Sex);
        IsBoolean = _property.PropertyType == typeof(bool) && !IsSex;
        IsEnum = _property.PropertyType.IsEnum;
        _choices = _property.PropertyType == typeof(int) ? choices : null;
        IsChoice = _choices is not null;
        IsNumber = _property.PropertyType == typeof(int) && !IsChoice;
        IsIdList = _property.PropertyType == typeof(List<int>);
        Options = IsEnum ? Enum.GetNames(_property.PropertyType) : [];
        RefreshChoiceOptions();
    }

    public string Label { get; }
    public bool IsText { get; }
    public bool IsBoolean { get; }
    public bool IsSex { get; }
    public IReadOnlyList<string> SexOptions { get; } = ["男", "女"];
    public bool IsEnum { get; }
    public bool IsNumber { get; }
    public IReadOnlyList<string> Options { get; }
    public bool IsChoice { get; }
    public bool IsIdList { get; }
    public double InputWidth => IsChoice || IsEnum || IsIdList ? 180 : 100;
    public IReadOnlyList<PersonChoiceOption> ChoiceOptions => _choiceOptions;
    public bool IsOutOfRange => _range is { } range && Value is int number &&
        (number < range.Minimum || number > range.Maximum);
    public string? RangeTip => _range is { } range
        ? $"{(IsOutOfRange ? "超出游戏取值范围。" : "")}范围 {range.Minimum}–{range.Maximum}：{range.Description}"
        : null;
    private object? Value => _record() is { } record ? _property.GetValue(record.Item) : null;

    public PersonChoiceOption? SelectedChoice
    {
        get => Value is int id ? _choiceOptions.FirstOrDefault(option => option.Id == id) : null;
        set { if (value is not null && !_refreshing) Set(value.Id); }
    }
    public string IdListText
    {
        get => Value is IEnumerable<int> ids ? ConfigStructuredStringCodec.FormatIds(ids) : "";
        set
        {
            var parsed = ConfigStructuredStringCodec.ParseIds(value);
            if (parsed.IsValid && Value is IEnumerable<int> current && !current.SequenceEqual(parsed.Items))
                Set(parsed.Items.ToList());
            OnPropertyChanged();
        }
    }

    public string TextValue
    {
        get => Value as string ?? "";
        set => Set(value ?? "");
    }
    public decimal? NumericValue
    {
        get => Value is int number ? number : null;
        set
        {
            if (value is { } number && number == decimal.Truncate(number) &&
                number >= int.MinValue && number <= int.MaxValue)
                Set((int)number);
        }
    }
    public bool BooleanValue
    {
        get => Value is true;
        set => Set(value);
    }
    public string? SelectedOption
    {
        get => Value?.ToString();
        set { if (value is not null) Set(Enum.Parse(_property.PropertyType, value)); }
    }
    public string? SelectedSex
    {
        get => _record() is null ? null : Value is true ? "女" : "男";
        set
        {
            if (value is "男" or "女") Set(value == "女");
        }
    }

    private void Set(object value)
    {
        if (_record() is not { } record || Equals(_property.GetValue(record.Item), value)) return;
        _context.SetPropertyValue(record, _property.Name, value, $"修改{_subject}{Label}");
        Refresh();
    }

    // Returns true when the option list actually changed. Keeping the same list instance matters:
    // replacing a ComboBox's ItemsSource makes it re-select its previous item by value and write it
    // back, which would copy the previously shown person's choice into the newly selected person.
    // The option list is built once and reused while switching records (the 学习中 title list has ~1300
    // entries); editors call ReloadChoices when the referenced data changes.
    private bool RefreshChoiceOptions()
    {
        if (_choices is null) return false;
        var baseChoices = _baseChoices ??= _choices().ToArray();
        IReadOnlyList<PersonChoiceOption> options = Value is int id && baseChoices.All(option => option.Id != id)
            ? [new PersonChoiceOption(id, $"{id} （未收录）"), ..baseChoices]
            : baseChoices;
        if (ReferenceEquals(options, _choiceOptions) || options.SequenceEqual(_choiceOptions)) return false;
        _choiceOptions = options;
        return true;
    }

    public void ReloadChoices() => _baseChoices = null;

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            if (RefreshChoiceOptions()) OnPropertyChanged(nameof(ChoiceOptions));
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(IsOutOfRange));
        OnPropertyChanged(nameof(RangeTip));
        OnPropertyChanged(nameof(IdListText));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(NumericValue));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(SelectedSex));
    }
}

public sealed record PersonColorOption(int Id, string Label, IBrush? Swatch)
{
    public override string ToString() => Label;
}

public sealed class PersonTitleSlotViewModel(
    int kindId, string kindName, IReadOnlyList<PersonChoiceOption> options, PersonBasicEditorViewModel owner)
    : ObservableObject
{
    internal static readonly PersonChoiceOption None = new(-1, "无");

    public int KindId { get; } = kindId;
    public string KindName { get; } = kindName;
    public IReadOnlyList<PersonChoiceOption> Options { get; } = options;
    public bool CanEdit => owner.CanEditTitles;

    public PersonChoiceOption? SelectedOption
    {
        get => owner.GetTitleOfKind(KindId) is { } id ? Options.FirstOrDefault(option => option.Id == id) : None;
        set { if (value is not null) owner.SetTitleOfKind(KindId, value.Id); }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(SelectedOption));
    }
}

public sealed class PersonIdChipViewModel(int id, string label, ICommand removeCommand)
{
    public int Id { get; } = id;
    public string Label { get; } = label;
    public ICommand RemoveCommand { get; } = removeCommand;
}

/// <summary>The editor whose selected record holds the ID lists edited by <see cref="PersonIdChipListViewModel"/>.</summary>
public interface IIdChipListOwner
{
    bool HasSelection { get; }
    object? GetValue(string propertyName);
    void SetValue(string propertyName, object value, string description);
    IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey);
    string GetChipLabel(string targetKey, int id);
}

public sealed class PersonIdChipListViewModel : ObservableObject
{
    private readonly string _propertyName;
    private readonly string _targetKey;
    private readonly IIdChipListOwner _owner;
    private IReadOnlyList<PersonChoiceOption> _candidates = [];
    private PersonChoiceOption? _candidate;

    public PersonIdChipListViewModel(string label, string propertyName, string targetKey, IIdChipListOwner owner)
    {
        Label = label;
        _propertyName = propertyName;
        _targetKey = targetKey;
        _owner = owner;
        AddCommand = new RelayCommand(Add, () => CanAdd);
    }

    public string Label { get; }
    public ObservableCollection<PersonIdChipViewModel> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public IReadOnlyList<PersonChoiceOption> Candidates => _candidates;
    public ICommand AddCommand { get; }
    public bool CanEdit => ParseCurrent() is { IsValid: true };
    public string Status => ParseCurrent() is { IsValid: false } parsed
        ? $"ID 列表格式有误：{string.Join("；", parsed.Errors)} 请先在通用编辑器中修正。"
        : "";
    private bool CanAdd => CanEdit && Candidate is not null && !ParseCurrent()!.Items.Contains(Candidate.Id);

    public PersonChoiceOption? Candidate
    {
        get => _candidate;
        set { if (SetProperty(ref _candidate, value)) ((RelayCommand)AddCommand).RaiseCanExecuteChanged(); }
    }

    private ConfigStructuredStringParseResult<int>? ParseCurrent() => _owner.HasSelection
        ? ConfigStructuredStringCodec.ParseIds(_owner.GetValue(_propertyName) as string ?? "")
        : null;

    private void Add()
    {
        if (!CanAdd || Candidate is not { } candidate) return;
        _owner.SetValue(_propertyName,
            ConfigStructuredStringCodec.FormatIds(ParseCurrent()!.Items.Append(candidate.Id)), $"添加{Label}");
        Candidate = null;
    }

    private void Remove(int id)
    {
        if (!CanEdit) return;
        _owner.SetValue(_propertyName,
            ConfigStructuredStringCodec.FormatIds(ParseCurrent()!.Items.Where(value => value != id)), $"移除{Label}");
    }

    public void Refresh()
    {
        var candidates = _owner.GetChipCandidates(_targetKey);
        var candidatesChanged = !candidates.SequenceEqual(_candidates);
        if (candidatesChanged) _candidates = candidates;
        Candidate = null;
        Items.Clear();
        if (ParseCurrent() is { IsValid: true } parsed)
            foreach (var id in parsed.Items)
                Items.Add(new PersonIdChipViewModel(id, _owner.GetChipLabel(_targetKey, id),
                    new RelayCommand(() => Remove(id), () => CanEdit)));
        OnPropertyChanged(nameof(IsEmpty));
        if (candidatesChanged) OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Status));
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
    }
}

public sealed record PersonChoiceOption(int Id, string Label)
{
    public override string ToString() => Label;
}

public sealed class PersonReferenceFieldViewModel(
    string label, string propertyName, PersonBasicEditorViewModel owner) : ObservableObject
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");
    private IReadOnlyList<PersonChoiceOption> _options = [None];
    private bool _refreshing;

    public string Label { get; } = label;
    public IReadOnlyList<PersonChoiceOption> Options => _options;
    public bool CanEdit => owner.CanEditFamily;
    private int CurrentId => owner.GetFamilyValue(propertyName) as int? ?? -1;

    public PersonChoiceOption? SelectedOption
    {
        get => _options.FirstOrDefault(option => option.Id == CurrentId);
        set { if (value is not null && !_refreshing) owner.SetFamilyValue(propertyName, value.Id, $"修改人物{Label}"); }
    }

    public void Refresh()
    {
        var current = CurrentId;
        var options = new List<PersonChoiceOption> { None };
        if (current != -1 && owner.PersonChoices.All(option => option.Id != current))
            options.Add(new PersonChoiceOption(current, owner.GetPersonLabel(current)));
        options.AddRange(owner.PersonChoices);
        // See PersonBasicFieldViewModel.RefreshChoiceOptions: never let a list swap write back a stale choice.
        if (!options.SequenceEqual(_options))
        {
            _refreshing = true;
            try
            {
                _options = options;
                OnPropertyChanged(nameof(Options));
            }
            finally
            {
                _refreshing = false;
            }
        }
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(CanEdit));
    }
}

public sealed class PersonIdListFieldViewModel : ObservableObject
{
    private readonly string _propertyName;
    private readonly PersonBasicEditorViewModel _owner;
    private PersonChoiceOption? _selectedItem;
    private PersonChoiceOption? _candidate;

    public PersonIdListFieldViewModel(string label, string propertyName, PersonBasicEditorViewModel owner)
    {
        Label = label;
        _propertyName = propertyName;
        _owner = owner;
        AddCommand = new RelayCommand(Add, () => CanAdd);
        RemoveCommand = new RelayCommand(Remove, () => CanEdit && SelectedItem is not null);
    }

    public string Label { get; }
    public ObservableCollection<PersonChoiceOption> Items { get; } = [];
    public IReadOnlyList<PersonChoiceOption> Candidates => _owner.PersonChoices;
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public bool CanEdit => _owner.CanEditFamily && ParseCurrent().IsValid;
    public string Status => ParseCurrent() is { IsValid: false } parsed
        ? $"ID 列表格式有误：{string.Join("；", parsed.Errors)} 请在人物亲属配置中修正。"
        : "";
    private bool CanAdd => CanEdit && Candidate is not null && !ParseCurrent().Items.Contains(Candidate.Id);

    public PersonChoiceOption? SelectedItem
    {
        get => _selectedItem;
        set { if (SetProperty(ref _selectedItem, value)) RaiseCommands(); }
    }

    public PersonChoiceOption? Candidate
    {
        get => _candidate;
        set { if (SetProperty(ref _candidate, value)) RaiseCommands(); }
    }

    private ConfigStructuredStringParseResult<int> ParseCurrent() =>
        ConfigStructuredStringCodec.ParseIds(_owner.GetFamilyValue(_propertyName) as string);

    private void Add()
    {
        if (!CanAdd || Candidate is not { } candidate) return;
        _owner.SetFamilyValue(_propertyName,
            ConfigStructuredStringCodec.FormatIds(ParseCurrent().Items.Append(candidate.Id)), $"添加{Label}");
    }

    private void Remove()
    {
        if (!CanEdit || SelectedItem is not { } selected) return;
        _owner.SetFamilyValue(_propertyName,
            ConfigStructuredStringCodec.FormatIds(ParseCurrent().Items.Where(id => id != selected.Id)),
            $"移除{Label}");
    }

    public void Refresh()
    {
        Items.Clear();
        foreach (var id in ParseCurrent().Items)
            Items.Add(new PersonChoiceOption(id, _owner.GetPersonLabel(id)));
        SelectedItem = null;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Status));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        ((RelayCommand)AddCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveCommand).RaiseCanExecuteChanged();
    }
}

public sealed class BiographyTextFieldViewModel : ObservableObject
{
    private readonly PropertyInfo _property;
    private readonly Func<ConfigRecordViewModel?> _record;
    private readonly ConfigDocumentViewModel? _document;

    public BiographyTextFieldViewModel(string label, string propertyName,
        Func<ConfigRecordViewModel?> record, ConfigDocumentViewModel? document)
    {
        Label = label;
        _property = typeof(BiographyConfig).GetProperty(propertyName)
            ?? throw new ArgumentException($"未知列传字段：{propertyName}");
        _record = record;
        _document = document;
    }

    public string Label { get; }
    public string TextValue
    {
        get => _record() is { } record ? (string?)_property.GetValue(record.Item) ?? string.Empty : string.Empty;
        set
        {
            if (_record() is not { } record || _document is null) return;
            var normalized = value ?? string.Empty;
            if (Equals(_property.GetValue(record.Item), normalized)) return;
            _document.SetPropertyValue(record, _property.Name, normalized, $"修改列传{Label}");
            Refresh();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(TextValue));
}
