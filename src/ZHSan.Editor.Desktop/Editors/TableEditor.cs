using System.Collections.ObjectModel;
using System.Globalization;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Describes an editor for a table that needs no logic of its own: Chinese labels grouped into sections.
/// <see cref="TableEditorViewModel"/> picks each field's control from its type and the reference metadata.
/// </summary>
public sealed record TableEditorSpec(
    string Key, Type ItemType, string Subject, string Intro, IReadOnlyList<TableSectionSpec> Sections)
{
    /// <summary>Writable properties deliberately left out of the editor, so they are kept as loaded.</summary>
    public IReadOnlyList<string> Untouched { get; init; } = [];

    /// <summary>How a record is named in the list, for tables whose names are empty or not shown by the game.</summary>
    public Func<object, TableRecordLookup, string>? RecordName { get; init; }

    public IEnumerable<TableFieldSpec> Fields => Sections.SelectMany(section => section.Fields);
}

public sealed record TableSectionSpec(string Title, string? Hint, IReadOnlyList<TableFieldSpec> Fields)
{
    /// <summary>Fields per row, so related fields (min and max, base and unit) line up; null fills the width.</summary>
    public int? Columns { get; init; }
}

/// <param name="Wide">A text field given its own full-width row, for descriptions and file names.</param>
/// <param name="Choices">Fixed choices for a number field whose values the game defines in code.</param>
/// <param name="ChoiceName">Names the referenced records in the choices, for tables without names of their own.</param>
public sealed record TableFieldSpec(
    string Label, string Property, string? Hint = null, bool Wide = false,
    IReadOnlyList<PersonChoiceOption>? Choices = null, Func<object, TableRecordLookup, string>? ChoiceName = null);

/// <summary>
/// Looks up records of other tables, so specs can name records after what they reference. Every record of the
/// list is named this way (13 000 person relations name two persons each), so each table is indexed by ID once.
/// </summary>
public sealed class TableRecordLookup(Func<string, IReadOnlyList<ConfigReferenceTarget>> targets)
{
    private readonly Dictionary<string, Dictionary<int, ConfigReferenceTarget>> _byId = [];

    public object? Item(string key, int id) => Find(key, id)?.Item;

    public string Name(string key, int id) => Find(key, id)?.DisplayName ?? $"#{id}";

    // The referenced tables changed; index them again on the next lookup.
    public void Clear() => _byId.Clear();

    private ConfigReferenceTarget? Find(string key, int id)
    {
        if (!_byId.TryGetValue(key, out var byId))
        {
            byId = [];
            foreach (var target in targets(key)) byId.TryAdd(target.Id, target);
            _byId[key] = byId;
        }
        return byId.GetValueOrDefault(id);
    }
}

public sealed class TableEditorProvider(IEnumerable<TableEditorSpec> specs) : IConfigEditorProvider
{
    private readonly IReadOnlyList<TableEditorSpec> _specs = [..specs];

    public string Id => "table";
    public string DisplayName => "专用编辑";
    public int Priority => 50;

    public bool CanEdit(ConfigDefinition definition) => Find(definition) is not null;

    public object CreateViewModel(ConfigEditorContext context) =>
        new TableEditorViewModel(context, Find(context.Definition)!);

    private TableEditorSpec? Find(ConfigDefinition definition) =>
        _specs.FirstOrDefault(spec => spec.Key == definition.Key && spec.ItemType == definition.ItemType);
}

public sealed class TableEditorViewModel : RecordListEditorViewModel<object>
{
    private readonly TableEditorSpec _spec;
    private readonly TableRecordLookup _lookup;
    private readonly List<TableRuleFieldViewModel> _ruleFields = [];

    public TableEditorViewModel(ConfigEditorContext context, TableEditorSpec spec) : base(context, spec.Subject, "条")
    {
        _spec = spec;
        _lookup = new TableRecordLookup(Targets);
        Intro = spec.Intro;
        var groups = new List<PersonFieldGroupViewModel>();
        var chipLists = new List<PersonIdChipListViewModel>();
        var sections = new List<TableSectionViewModel>();
        foreach (var section in spec.Sections)
        {
            var fields = new List<PersonBasicFieldViewModel>();
            var wide = new List<object>();
            foreach (var field in section.Fields)
            {
                var property = Property(field.Property);
                if (IsIdList(property) && IdTargetKey(property) is { } targetKey)
                {
                    var list = ChipList(field.Label, field.Property, targetKey, field.Hint);
                    chipLists.Add(list);
                    wide.Add(list);
                }
                else if (property.StructuredString is { } structured)
                {
                    var rule = new TableRuleFieldViewModel(field.Label, field.Hint, structured, this, field.Property);
                    _ruleFields.Add(rule);
                    wide.Add(rule);
                }
                else
                {
                    var editor = Field(field.Label, field.Property);
                    if (!editor.IsSupported)
                        throw new ArgumentException($"{spec.Key}.{field.Property} 的类型 {property.PropertyType.Name} 不能在专用编辑器中编辑");
                    if ((field.Wide && editor.IsText) || editor.IsTextList) wide.Add(new TableWideTextViewModel(editor, field.Hint));
                    else fields.Add(editor);
                }
            }
            var group = new PersonFieldGroupViewModel(section.Title, fields);
            groups.Add(group);
            sections.Add(new TableSectionViewModel(section.Title, section.Hint, group, fields.Count > 0, wide)
            {
                FieldsMaxWidth = section.Columns is { } columns ? columns * TableSectionViewModel.CellWidth : double.PositiveInfinity
            });
        }
        Groups = groups;
        ChipLists = chipLists;
        Sections = sections;
        Initialize();
    }

    public string Subject => _spec.Subject;
    public string Intro { get; }
    public IReadOnlyList<TableSectionViewModel> Sections { get; }
    public override IEnumerable<PersonBasicFieldViewModel> AllFields =>
        base.AllFields.Concat(Sections.SelectMany(section => section.WideItems.OfType<TableWideTextViewModel>())
            .Select(item => item.Field));
    protected override Type ItemType => _spec.ItemType;

    protected override string GetName(object config) =>
        _spec.RecordName is { } recordName ? recordName(config, _lookup) :
        ItemType.GetProperty("Name")?.GetValue(config) as string is { Length: > 0 } name
            ? name
            : $"#{ItemType.GetProperty("Id")?.GetValue(config)}";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName)
    {
        var field = _spec.Fields.First(field => field.Property == propertyName);
        if (field.Choices is { } choices) return () => choices;
        if (Property(propertyName).Reference is not { } reference) return null;
        var none = reference.EmptyValue is { } empty ? new PersonChoiceOption(empty, $"{empty} 无") : null;
        return () =>
        {
            var targets = Targets(reference.TargetAddress);
            var options = (field.ChoiceName is { } name
                    ? targets.GroupBy(target => target.Id).Select(group => group.First()).OrderBy(target => target.Id)
                        .Select(target => new PersonChoiceOption(target.Id, $"{target.Id} {name(target.Item, _lookup)}"))
                    : ReferenceOptions(targets))
                .Where(option => option.Id != none?.Id);
            return none is null ? [..options] : [none, ..options];
        };
    }

    // Number lists (List<int>, int[]) are shown to the chip lists as the space separated text they edit.
    public override object? GetValue(string propertyName) => base.GetValue(propertyName) switch
    {
        IEnumerable<int> ids when Property(propertyName).PropertyType != typeof(string) =>
            ConfigStructuredStringCodec.FormatIds(ids),
        var value => value
    };

    public override void SetValue(string propertyName, object value, string description)
    {
        var type = Property(propertyName).PropertyType;
        if (value is string text && type != typeof(string) &&
            ConfigStructuredStringCodec.ParseIds(text) is { IsValid: true } parsed)
            value = type == typeof(int[]) ? parsed.Items.ToArray() : parsed.Items.ToList();
        base.SetValue(propertyName, value, description);
    }

    internal IReadOnlyList<ConfigReferenceTarget> Targets(ConfigAddress address) => Context.GetReferenceTargets(address);

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        foreach (var rule in _ruleFields) rule.Refresh();
    }

    protected override void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        _lookup.Clear();
        base.OnReferenceOptionsChanged(sender, e);
        foreach (var rule in _ruleFields) rule.ReloadOptions();
    }

    private ConfigPropertyDefinition Property(string name) =>
        Context.Document.Properties.FirstOrDefault(property => property.Name == name)
        ?? throw new ArgumentException($"{_spec.Key} 没有字段 {name}");

    private static bool IsIdList(ConfigPropertyDefinition property) =>
        property.StructuredString?.Kind == ConfigStructuredStringKind.ReferenceIds ||
        property.PropertyType == typeof(List<int>) || property.PropertyType == typeof(int[]);

    private static string? IdTargetKey(ConfigPropertyDefinition property) =>
        property.StructuredString is { Kind: ConfigStructuredStringKind.ReferenceIds } structured
            ? structured.TargetConfigKey
            : property.Reference?.TargetConfigKey;
}

public sealed record TableSectionViewModel(
    string Title, string? Hint, PersonFieldGroupViewModel Group, bool HasFields, IReadOnlyList<object> WideItems)
{
    // Matches the field cell width in TableEditorView.
    public const double CellWidth = 280;

    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public double FieldsMaxWidth { get; init; } = double.PositiveInfinity;
}

public sealed record TableWideTextViewModel(PersonBasicFieldViewModel Field, string? Hint)
{
    public bool HasHint => !string.IsNullOrEmpty(Hint);
}

/// <summary>A condition, influence or weighted condition string, edited with the shared rule string editor.</summary>
public sealed class TableRuleFieldViewModel
{
    private readonly ConfigStructuredStringDefinition _definition;
    private readonly TableEditorViewModel _owner;
    private readonly string _propertyName;
    private readonly ObservableCollection<ReferenceOptionViewModel> _options = [];

    public TableRuleFieldViewModel(string label, string? hint, ConfigStructuredStringDefinition definition,
        TableEditorViewModel owner, string propertyName)
    {
        Label = label;
        Hint = hint;
        _definition = definition;
        _owner = owner;
        _propertyName = propertyName;
        ReloadOptionList();
        Editor = new StructuredRuleStringEditorViewModel(definition,
            () => owner.GetValue(propertyName) as string ?? "",
            value => owner.SetValue(propertyName, value, $"修改{label}"),
            _options, null);
    }

    public string Label { get; }
    public string? Hint { get; }
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public StructuredRuleStringEditorViewModel Editor { get; }

    public void Refresh()
    {
        if (_owner.HasSelection) Editor.ReloadFromValue(_owner.GetValue(_propertyName) as string ?? "");
    }

    public void ReloadOptions()
    {
        ReloadOptionList();
        Editor.RefreshReferenceOptions();
    }

    private void ReloadOptionList()
    {
        _options.Clear();
        foreach (var target in _owner.Targets(_definition.TargetAddress).GroupBy(target => target.Id)
                     .Select(group => group.First()).OrderBy(target => target.Id))
            _options.Add(new ReferenceOptionViewModel(target.Id,
                $"#{target.Id.ToString(CultureInfo.InvariantCulture)} · {target.DisplayName}", false, target));
    }
}
