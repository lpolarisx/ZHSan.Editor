using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class PersonBasicEditorViewModel : ObservableObject, IDisposable
{
    private readonly ConfigEditorContext _context;
    private readonly ConfigDocumentViewModel? _biographyDocument;
    private ConfigRecordViewModel? _biographyRecord;
    private PersonOptionViewModel? _selectedPerson;
    private string _searchText = string.Empty;
    private bool _synchronizing;

    public PersonBasicEditorViewModel(ConfigEditorContext context)
    {
        _context = context;
        _biographyDocument = context.FindDocument("biographies");
        BiographyFields = [
            new BiographyTextFieldViewModel("简介", nameof(BiographyConfig.Brief), () => _biographyRecord, _biographyDocument),
            new BiographyTextFieldViewModel("演义", nameof(BiographyConfig.Romance), () => _biographyRecord, _biographyDocument),
            new BiographyTextFieldViewModel("历史", nameof(BiographyConfig.History), () => _biographyRecord, _biographyDocument),
            new BiographyTextFieldViewModel("游戏内列传", nameof(BiographyConfig.InGame), () => _biographyRecord, _biographyDocument)
        ];
        AddBiographyCommand = new RelayCommand(AddBiography, () => CanAddBiography);
        Groups =
        [
            Group("身份", ("姓", nameof(PersonConfig.SurName)), ("名", nameof(PersonConfig.GivenName)),
                ("字", nameof(PersonConfig.CalledName)), ("头像序号", nameof(PersonConfig.PictureIndex)),
                ("性别", nameof(PersonConfig.Sex))),
            Group("生平", ("出生地", nameof(PersonConfig.BornRegion)),
                ("出仕年", nameof(PersonConfig.YearAvailable)), ("出生年", nameof(PersonConfig.YearBorn)),
                ("死亡年", nameof(PersonConfig.YearDead)), ("死因", nameof(PersonConfig.DeadReason)),
                ("世代", nameof(PersonConfig.Generation))),
            Group("能力", ("武勇", nameof(PersonConfig.BaseStrength)), ("统率", nameof(PersonConfig.BaseCommand)),
                ("智谋", nameof(PersonConfig.BaseIntelligence)), ("政治", nameof(PersonConfig.BasePolitics)),
                ("魅力", nameof(PersonConfig.BaseGlamour))),
            Group("能力经验", ("武经", nameof(PersonConfig.StrengthExperience)),
                ("统经", nameof(PersonConfig.CommandExperience)),
                ("智经", nameof(PersonConfig.IntelligenceExperience)),
                ("政经", nameof(PersonConfig.PoliticsExperience)),
                ("魅经", nameof(PersonConfig.GlamourExperience))),
            Group("性格与倾向", ("勇猛度", nameof(PersonConfig.BaseBraveness)),
                ("冷静度", nameof(PersonConfig.BaseCalmness)),
                ("忠诚度", nameof(PersonConfig.PersonalLoyalty)),
                ("野心", nameof(PersonConfig.Ambition)), ("性格", nameof(PersonConfig.PCharacter)),
                ("义理", nameof(PersonConfig.Ideal)),
                ("理想倾向", nameof(PersonConfig.IdealTendencyIDString)),
                ("资格", nameof(PersonConfig.Qualification)),
                ("战略倾向", nameof(PersonConfig.StrategyTendency)),
                ("重视政务", nameof(PersonConfig.ValuationOnGovernment))),
            Group("仕官与状态", ("仕官年份", nameof(PersonConfig.YearJoin)),
                ("登场地点", nameof(PersonConfig.AvailableLocation)),
                ("名声", nameof(PersonConfig.Reputation)),
                ("功绩", nameof(PersonConfig.OfficerMerit)),
                ("存活", nameof(PersonConfig.Alive)), ("可用", nameof(PersonConfig.Available)))
        ];
        AssociationGroups =
        [
            AssociationGroup("称号",
                ("已有称号", nameof(PersonConfig.RealTitlesString)),
                ("专属称号", nameof(PersonConfig.UniqueTitlesString)),
                ("个人称号", nameof(PersonConfig.PersonalTitleString)),
                ("战斗称号", nameof(PersonConfig.CombatTitleString)),
                ("正在学习的称号", nameof(PersonConfig.StudyingTitleString))),
            AssociationGroup("战法经验与兵种",
                ("战法经验", nameof(PersonConfig.TacticsExperience)),
                ("计略经验", nameof(PersonConfig.StratagemExperience)),
                ("专属兵种", nameof(PersonConfig.UniqueMilitaryKindsString)),
                ("步兵经验", nameof(PersonConfig.BubingExperience)),
                ("骑兵经验", nameof(PersonConfig.QibingExperience)),
                ("弩兵经验", nameof(PersonConfig.NubingExperience)),
                ("器械经验", nameof(PersonConfig.QixieExperience)),
                ("水军经验", nameof(PersonConfig.ShuijunExperience)))
        ];
        RebuildSkillButtons();
        _context.Document.ReferenceOptionsChanged += OnReferenceOptionsChanged;
        if (_biographyDocument is not null) _biographyDocument.StateChanged += OnBiographyStateChanged;
        _context.Document.StateChanged += OnDocumentStateChanged;
        _context.Document.PropertyChanged += OnDocumentPropertyChanged;
        RebuildPeople();
    }

    public ObservableCollection<PersonOptionViewModel> People { get; } = [];
    public ObservableCollection<PersonOptionViewModel> FilteredPeople { get; } = [];
    public IReadOnlyList<PersonFieldGroupViewModel> Groups { get; }
    public IReadOnlyList<PersonAssociationGroupViewModel> AssociationGroups { get; }
    public ObservableCollection<PersonSkillButtonViewModel> SkillButtons { get; } = [];
    public string SkillStatus => GetSkillStatus();
    public IReadOnlyList<BiographyTextFieldViewModel> BiographyFields { get; }
    public ICommand AddBiographyCommand { get; }
    public bool HasBiography => _biographyRecord is not null;
    public bool CanAddBiography => SelectedPerson is not null && _biographyDocument is not null &&
        !_biographyDocument.HasNullRecords && _biographyDocument.Records.All(record =>
            record.Item is not BiographyConfig biography || biography.Id != SelectedPerson.Config.Id);
    public string BiographyStatus => GetBiographyStatus();
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

    private PersonFieldGroupViewModel Group(string title, params (string Label, string Property)[] fields) =>
        new(title, fields.Select(field => new PersonBasicFieldViewModel(
            field.Label, field.Property, () => SelectedPerson?.Record, _context)).ToArray());

    private PersonAssociationGroupViewModel AssociationGroup(
        string title, params (string Label, string Property)[] fields) =>
        new(title, fields.Select(field => new PersonAssociationFieldViewModel(
            field.Label, field.Property, _context.Document)).ToArray());
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

    private void OnReferenceOptionsChanged(object? sender, EventArgs e) => RebuildSkillButtons();

    private void RebuildPeople()
    {
        var selectedRecord = _context.SelectedRecord ?? SelectedPerson?.Record;
        People.Clear();
        foreach (var record in _context.Records.Where(record => record.Item is PersonConfig))
            People.Add(new PersonOptionViewModel(record));
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
        foreach (var field in Groups.SelectMany(group => group.Fields)) field.Refresh();
        foreach (var field in AssociationGroups.SelectMany(group => group.Fields)) field.Refresh();
        RefreshSkills();
        RefreshBiography();
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
        ((RelayCommand)AddBiographyCommand).RaiseCanExecuteChanged();
        foreach (var field in BiographyFields) field.Refresh();
    }

    private string GetBiographyStatus()
    {
        if (SelectedPerson is null) return "请先选择人物。";
        if (_biographyDocument is null) return "当前档案没有列传配置。";
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
        ApplyFilter();
        RefreshSelection();
    }

    public void Dispose()
    {
        if (_biographyDocument is not null) _biographyDocument.StateChanged -= OnBiographyStateChanged;
        _context.Document.ReferenceOptionsChanged -= OnReferenceOptionsChanged;
        _context.Document.StateChanged -= OnDocumentStateChanged;
        _context.Document.PropertyChanged -= OnDocumentPropertyChanged;
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
public sealed record PersonFieldGroupViewModel(string Title, IReadOnlyList<PersonBasicFieldViewModel> Fields);

public sealed class PersonBasicFieldViewModel : ObservableObject
{
    private readonly PropertyInfo _property;
    private readonly Func<ConfigRecordViewModel?> _record;
    private readonly ConfigEditorContext _context;

    public PersonBasicFieldViewModel(string label, string propertyName,
        Func<ConfigRecordViewModel?> record, ConfigEditorContext context)
    {
        Label = label;
        _property = typeof(PersonConfig).GetProperty(propertyName)
            ?? throw new ArgumentException($"未知人物字段：{propertyName}");
        _record = record;
        _context = context;
        IsText = _property.PropertyType == typeof(string);
        IsSex = propertyName == nameof(PersonConfig.Sex);
        IsBoolean = _property.PropertyType == typeof(bool) && !IsSex;
        IsEnum = _property.PropertyType.IsEnum;
        IsNumber = _property.PropertyType == typeof(int);
        Options = IsEnum ? Enum.GetNames(_property.PropertyType) : [];
    }

    public string Label { get; }
    public bool IsText { get; }
    public bool IsBoolean { get; }
    public bool IsSex { get; }
    public IReadOnlyList<string> SexOptions { get; } = ["男", "女"];
    public bool IsEnum { get; }
    public bool IsNumber { get; }
    public IReadOnlyList<string> Options { get; }
    private object? Value => _record() is { } record ? _property.GetValue(record.Item) : null;

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
        _context.SetPropertyValue(record, _property.Name, value, $"修改人物{Label}");
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(NumericValue));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(SelectedSex));
    }
}

public sealed record PersonAssociationGroupViewModel(
    string Title, IReadOnlyList<PersonAssociationFieldViewModel> Fields);

public sealed class PersonAssociationFieldViewModel(
    string label, string propertyName, ConfigDocumentViewModel document) : ObservableObject
{
    public string Label { get; } = label;
    public PropertyEditorViewModel? Editor =>
        document.PropertyEditors.FirstOrDefault(editor => editor.Definition.Name == propertyName);

    public void Refresh() => OnPropertyChanged(nameof(Editor));
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
