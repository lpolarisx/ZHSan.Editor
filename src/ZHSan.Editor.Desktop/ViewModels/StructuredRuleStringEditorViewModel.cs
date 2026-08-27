using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class StructuredRuleStringEditorViewModel : ObservableObject
{
    private readonly ConfigStructuredStringDefinition _definition;
    private readonly Func<string> _getValue;
    private readonly Action<string> _setValue;
    private readonly IReadOnlyList<ReferenceOptionViewModel> _options;
    private readonly ObservableCollection<ReferenceOptionViewModel> _conditionOptions = [];
    private readonly Action<ConfigReferenceTarget>? _navigateReference;
    private IReadOnlyList<string> _parseErrors = [];
    private bool _isSynchronizing;
    private ConditionExpressionGroupViewModel? _selectedAddGroup;
    private bool _addAsAlternativeGroup;
    private bool _isRawEditorExpanded;
    private StructuredRuleStringEntryViewModel? _selectedWeightedEntry;
    private IReadOnlyList<string> _pasteErrors = [];

    public StructuredRuleStringEditorViewModel(
        ConfigStructuredStringDefinition definition,
        Func<string> getValue,
        Action<string> setValue,
        IReadOnlyList<ReferenceOptionViewModel> options,
        Action<ConfigReferenceTarget>? navigateReference)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        _setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ReloadConditionOptions();
        _navigateReference = navigateReference;
        ToggleRawEditorCommand = new RelayCommand(ToggleRawEditor);
        AddPicker = new ReferencePickerViewModel(
            IsConditionExpression ? _conditionOptions : _options,
            () => null,
            option => Add(option.Id));
        ReloadFromValue(_getValue());
    }

    public ObservableCollection<StructuredRuleStringEntryViewModel> Entries { get; } = [];
    public ObservableCollection<ConditionExpressionGroupViewModel> ConditionGroups { get; } = [];
    public ReferencePickerViewModel AddPicker { get; }
    public ICommand ToggleRawEditorCommand { get; }
    public string FormatDescription => _definition.FormatDescription;
    public bool IsWeighted => _definition.Kind == ConfigStructuredStringKind.WeightedConditionPairs;
    public bool IsInfluenceList => _definition.Kind == ConfigStructuredStringKind.InfluenceIds;
    public bool IsConditionExpression => _definition.Kind == ConfigStructuredStringKind.ConditionIds;
    public bool ShowFlatList => !IsConditionExpression && !IsWeighted;
    public bool ShowWeightedTable => IsWeighted;
    public bool HasConditionGroups => ConditionGroups.Count > 0;
    public bool HasParseErrors => _parseErrors.Count > 0;
    public bool CanToggleRawEditor => IsConditionExpression || IsWeighted;
    public bool ShowRawEditor => IsInfluenceList || HasParseErrors || _isRawEditorExpanded;
    public string RawEditorToggleText => ShowRawEditor ? "隐藏底层文本" : "显示底层文本（高级）";
    public bool CanUseStructuredEditor => _parseErrors.Count == 0;
    public bool HasIssues => IssueCount > 0;
    public bool HasPasteIssues => _pasteErrors.Count > 0;
    public int IssueCount => GetIssues().Count;
    public string IssueSummary => string.Join(Environment.NewLine, GetIssues().Select(issue => $"• {issue}"));
    public string PasteIssueSummary => string.Join(Environment.NewLine, _pasteErrors.Select(error => $"• {error}"));
    public string Summary => IsConditionExpression
        ? $"{ConditionGroups.Count} 个或分组，{ConditionGroups.Sum(group => group.Terms.Count)} 个条件"
        : IsWeighted
            ? $"{Entries.Count} 组条件权重"
            : $"{Entries.Count} 个影响";

    public ConditionExpressionGroupViewModel? SelectedAddGroup
    {
        get => _selectedAddGroup;
        set => SetProperty(ref _selectedAddGroup, value);
    }

    public bool AddAsAlternativeGroup
    {
        get => _addAsAlternativeGroup;
        set => SetProperty(ref _addAsAlternativeGroup, value);
    }

    public StructuredRuleStringEntryViewModel? SelectedWeightedEntry
    {
        get => _selectedWeightedEntry;
        set => SetProperty(ref _selectedWeightedEntry, value);
    }

    public string RawText
    {
        get => _getValue();
        set
        {
            value ??= string.Empty;
            if (_isSynchronizing || string.Equals(value, _getValue(), StringComparison.Ordinal))
            {
                return;
            }

            _setValue(value);
        }
    }

    public void ReloadFromValue(string value)
    {
        var selectedWeightedId = SelectedWeightedEntry?.Id;
        _pasteErrors = [];
        _isSynchronizing = true;
        Entries.Clear();
        ConditionGroups.Clear();
        if (IsConditionExpression)
        {
            var parsed = ConfigStructuredStringCodec.ParseConditionExpression(value);
            _parseErrors = parsed.Errors;
            foreach (var group in parsed.Items)
            {
                ConditionGroups.Add(CreateConditionGroup(group));
            }

            SelectedAddGroup = ConditionGroups.LastOrDefault();
        }
        else if (IsWeighted)
        {
            var parsed = ConfigStructuredStringCodec.ParseWeightedConditions(value);
            _parseErrors = parsed.Errors;
            foreach (var item in parsed.Items)
            {
                Entries.Add(CreateEntry(item.ConditionId, item.Weight));
            }

            SelectedWeightedEntry = selectedWeightedId.HasValue
                ? Entries.FirstOrDefault(entry => entry.Id == selectedWeightedId.Value)
                : null;
        }
        else
        {
            var parsed = ConfigStructuredStringCodec.ParseIds(value);
            _parseErrors = parsed.Errors;
            foreach (var id in parsed.Items)
            {
                Entries.Add(CreateEntry(id, null));
            }
        }

        _isSynchronizing = false;
        RefreshState();
    }

    public void RefreshReferenceOptions()
    {
        ReloadConditionOptions();
        AddPicker.RefreshOptions();
        foreach (var entry in Entries)
        {
            entry.ReferencePicker.RefreshOptions();
        }

        foreach (var term in ConditionGroups.SelectMany(group => group.Terms))
        {
            term.ReferencePicker.RefreshOptions();
        }

        RefreshState();
    }

    public bool PasteWeightedEntries(string? text)
    {
        if (!IsWeighted || !CanUseStructuredEditor)
        {
            return false;
        }

        var parsed = ConfigStructuredStringCodec.ParseWeightedConditions(text);
        var errors = new List<string>(parsed.Errors);
        if (string.IsNullOrWhiteSpace(text) ||
            (parsed.Items.Count == 0 && parsed.Errors.Count == 0))
        {
            errors.Add("剪贴板中没有可粘贴的条件权重数据。");
        }

        foreach (var duplicate in parsed.Items.GroupBy(item => item.ConditionId).Where(group => group.Count() > 1))
        {
            errors.Add($"粘贴内容中的条件 ID {duplicate.Key} 重复。");
        }

        var existingIds = Entries.Select(entry => entry.Id).ToHashSet();
        foreach (var duplicateId in parsed.Items
                     .Select(item => item.ConditionId)
                     .Where(existingIds.Contains)
                     .Distinct())
        {
            errors.Add($"条件 ID {duplicateId} 已在表格中存在。");
        }

        if (errors.Count > 0)
        {
            SetPasteErrors(errors);
            return false;
        }

        var combined = Entries
            .Select(entry => new WeightedConditionValue(entry.Id, entry.Weight ?? 1f))
            .Concat(parsed.Items)
            .ToArray();
        _setValue(ConfigStructuredStringCodec.FormatWeightedConditions(combined));
        SelectedWeightedEntry = Entries.FirstOrDefault(entry => entry.Id == parsed.Items[^1].ConditionId);
        SetPasteErrors([]);
        return true;
    }

    public string FormatWeightedEntriesForClipboard(
        IEnumerable<StructuredRuleStringEntryViewModel> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!IsWeighted)
        {
            return string.Empty;
        }

        var selected = entries
            .Where(Entries.Contains)
            .Distinct()
            .OrderBy(Entries.IndexOf)
            .ToArray();
        return string.Join(
            Environment.NewLine,
            selected.Select(entry =>
                $"{entry.Id.ToString(CultureInfo.InvariantCulture)}\t{(entry.Weight ?? 1f).ToString("R", CultureInfo.InvariantCulture)}"));
    }

    public void RemoveWeightedEntries(IEnumerable<StructuredRuleStringEntryViewModel> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!IsWeighted || !CanUseStructuredEditor)
        {
            return;
        }

        var removed = entries.Where(Entries.Contains).ToHashSet();
        if (removed.Count == 0)
        {
            return;
        }

        var remaining = Entries
            .Where(entry => !removed.Contains(entry))
            .Select(entry => new WeightedConditionValue(entry.Id, entry.Weight ?? 1f));
        _setValue(ConfigStructuredStringCodec.FormatWeightedConditions(remaining));
        SetPasteErrors([]);
    }

    public void MoveWeightedEntry(StructuredRuleStringEntryViewModel entry, int offset)
    {
        if (!IsWeighted || !Entries.Contains(entry) || offset is not (-1 or 1))
        {
            return;
        }

        SelectedWeightedEntry = entry;
        Move(entry, offset);
    }

    private void SetPasteErrors(IReadOnlyList<string> errors)
    {
        _pasteErrors = errors;
        OnPropertyChanged(nameof(HasPasteIssues));
        OnPropertyChanged(nameof(PasteIssueSummary));
    }

    private void ToggleRawEditor()
    {
        if (!CanToggleRawEditor || HasParseErrors)
        {
            return;
        }

        _isRawEditorExpanded = !_isRawEditorExpanded;
        OnPropertyChanged(nameof(ShowRawEditor));
        OnPropertyChanged(nameof(RawEditorToggleText));
    }

    private void ReloadConditionOptions()
    {
        _conditionOptions.Clear();
        foreach (var option in _options.Where(option =>
                     option.Id != ConfigStructuredStringCodec.NegateNextConditionId &&
                     option.Id != ConfigStructuredStringCodec.OrConditionId))
        {
            _conditionOptions.Add(option);
        }
    }

    private StructuredRuleStringEntryViewModel CreateEntry(int id, float? weight) =>
        new(
            id,
            weight,
            IsWeighted,
            canReplaceReference: !IsInfluenceList,
            canReorder: !IsInfluenceList,
            _options,
            _navigateReference,
            entry => ReplaceId(entry, entry.Id),
            entry => ReplaceWeight(entry, entry.Weight),
            Remove,
            MoveUp,
            MoveDown);

    private void Add(int id)
    {
        if (!CanUseStructuredEditor)
        {
            return;
        }

        if (IsConditionExpression)
        {
            if (AddAsAlternativeGroup || SelectedAddGroup is null || !ConditionGroups.Contains(SelectedAddGroup))
            {
                var group = CreateConditionGroup(
                    new ConditionExpressionGroupValue([new ConditionExpressionTermValue(id, false)]));
                ConditionGroups.Add(group);
                SelectedAddGroup = group;
                AddAsAlternativeGroup = false;
            }
            else
            {
                SelectedAddGroup.Terms.Add(CreateConditionTerm(id, false, SelectedAddGroup));
            }

            CommitConditionGroups();
        }
        else
        {
            Entries.Add(CreateEntry(id, IsWeighted ? 1f : null));
            CommitEntries();
        }
    }

    private void ReplaceId(StructuredRuleStringEntryViewModel entry, int id)
    {
        if (!CanUseStructuredEditor || !Entries.Contains(entry))
        {
            return;
        }

        entry.ApplyId(id);
        CommitEntries();
    }

    private void ReplaceWeight(StructuredRuleStringEntryViewModel entry, float? weight)
    {
        if (!CanUseStructuredEditor || !Entries.Contains(entry) || !weight.HasValue)
        {
            return;
        }

        entry.ApplyWeight(weight.Value);
        CommitEntries();
    }

    private void Remove(StructuredRuleStringEntryViewModel entry)
    {
        if (!CanUseStructuredEditor || !Entries.Remove(entry))
        {
            return;
        }

        CommitEntries();
    }

    private void MoveUp(StructuredRuleStringEntryViewModel entry) => Move(entry, -1);

    private void MoveDown(StructuredRuleStringEntryViewModel entry) => Move(entry, 1);

    private void Move(StructuredRuleStringEntryViewModel entry, int offset)
    {
        if (!CanUseStructuredEditor || IsInfluenceList)
        {
            return;
        }

        var oldIndex = Entries.IndexOf(entry);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Entries.Count)
        {
            return;
        }

        Entries.Move(oldIndex, newIndex);
        CommitEntries();
    }

    private ConditionExpressionGroupViewModel CreateConditionGroup(ConditionExpressionGroupValue value)
    {
        ConditionExpressionGroupViewModel? group = null;
        group = new ConditionExpressionGroupViewModel(
            remove: () => RemoveConditionGroup(group!),
            moveUp: () => MoveConditionGroup(group!, -1),
            moveDown: () => MoveConditionGroup(group!, 1));
        foreach (var term in value.Terms)
        {
            group.Terms.Add(CreateConditionTerm(term.ConditionId, term.IsNegated, group));
        }

        return group;
    }

    private ConditionExpressionTermViewModel CreateConditionTerm(
        int id,
        bool isNegated,
        ConditionExpressionGroupViewModel group)
    {
        ConditionExpressionTermViewModel? term = null;
        term = new ConditionExpressionTermViewModel(
            id,
            isNegated,
            _conditionOptions,
            _navigateReference,
            changed: CommitConditionGroups,
            remove: () => RemoveConditionTerm(group, term!),
            moveUp: () => MoveConditionTerm(group, term!, -1),
            moveDown: () => MoveConditionTerm(group, term!, 1));
        return term;
    }

    private void RemoveConditionGroup(ConditionExpressionGroupViewModel group)
    {
        if (!CanUseStructuredEditor || !ConditionGroups.Remove(group))
        {
            return;
        }

        SelectedAddGroup = ConditionGroups.LastOrDefault();
        CommitConditionGroups();
    }

    private void MoveConditionGroup(ConditionExpressionGroupViewModel group, int offset)
    {
        if (!CanUseStructuredEditor)
        {
            return;
        }

        var oldIndex = ConditionGroups.IndexOf(group);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= ConditionGroups.Count)
        {
            return;
        }

        ConditionGroups.Move(oldIndex, newIndex);
        CommitConditionGroups();
    }

    private void RemoveConditionTerm(
        ConditionExpressionGroupViewModel group,
        ConditionExpressionTermViewModel term)
    {
        if (!CanUseStructuredEditor || !ConditionGroups.Contains(group) || !group.Terms.Remove(term))
        {
            return;
        }

        if (group.Terms.Count == 0)
        {
            ConditionGroups.Remove(group);
            SelectedAddGroup = ConditionGroups.LastOrDefault();
        }

        CommitConditionGroups();
    }

    private void MoveConditionTerm(
        ConditionExpressionGroupViewModel group,
        ConditionExpressionTermViewModel term,
        int offset)
    {
        if (!CanUseStructuredEditor || !ConditionGroups.Contains(group))
        {
            return;
        }

        var oldIndex = group.Terms.IndexOf(term);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= group.Terms.Count)
        {
            return;
        }

        group.Terms.Move(oldIndex, newIndex);
        CommitConditionGroups();
    }

    private void CommitConditionGroups()
    {
        var value = ConfigStructuredStringCodec.FormatConditionExpression(
            ConditionGroups.Select(group => new ConditionExpressionGroupValue(
                group.Terms.Select(term => new ConditionExpressionTermValue(term.Id, term.IsNegated)).ToArray())));
        _setValue(value);
    }

    private void CommitEntries()
    {
        var value = IsWeighted
            ? ConfigStructuredStringCodec.FormatWeightedConditions(
                Entries.Select(entry => new WeightedConditionValue(entry.Id, entry.Weight ?? 1f)))
            : ConfigStructuredStringCodec.FormatIds(Entries.Select(entry => entry.Id));
        _setValue(value);
    }

    private IReadOnlyList<string> GetIssues()
    {
        var issues = new List<string>(_parseErrors);
        if (_parseErrors.Count > 0)
        {
            issues.Add("请先在原始文本框中修复语法，再使用结构化操作。");
            return issues;
        }

        if (IsConditionExpression)
        {
            var terms = ConditionGroups.SelectMany(group => group.Terms).ToArray();
            foreach (var term in terms.Where(term => term.IsMissing))
            {
                issues.Add($"条件 ID {term.Id} 的目标不存在或 ID 重复。");
            }

            foreach (var duplicate in terms.GroupBy(term => term.Id).Where(group => group.Count() > 1))
            {
                issues.Add($"条件 ID {duplicate.Key} 重复，游戏加载时只保留第一次出现的位置。");
            }

            return issues.Distinct(StringComparer.Ordinal).ToArray();
        }

        foreach (var entry in Entries.Where(entry => entry.IsMissing))
        {
            issues.Add($"ID {entry.Id} 的目标不存在或 ID 重复。");
        }

        foreach (var duplicate in Entries.GroupBy(entry => entry.Id).Where(group => group.Count() > 1))
        {
            issues.Add(IsWeighted
                ? $"条件 ID {duplicate.Key} 重复，会导致游戏加载该权重字段失败。"
                : $"ID {duplicate.Key} 重复，游戏加载时只保留第一次出现的位置。");
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private void RefreshState()
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            Entries[index].Refresh(index, Entries.Count);
        }

        for (var index = 0; index < ConditionGroups.Count; index++)
        {
            ConditionGroups[index].Refresh(index, ConditionGroups.Count);
        }

        OnPropertyChanged(nameof(RawText));
        OnPropertyChanged(nameof(HasConditionGroups));
        OnPropertyChanged(nameof(HasParseErrors));
        OnPropertyChanged(nameof(ShowRawEditor));
        OnPropertyChanged(nameof(RawEditorToggleText));
        OnPropertyChanged(nameof(CanUseStructuredEditor));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssueCount));
        OnPropertyChanged(nameof(IssueSummary));
        OnPropertyChanged(nameof(HasPasteIssues));
        OnPropertyChanged(nameof(PasteIssueSummary));
        OnPropertyChanged(nameof(Summary));
    }
}

public sealed class ConditionExpressionGroupViewModel : ObservableObject
{
    private int _position;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public ConditionExpressionGroupViewModel(Action remove, Action moveUp, Action moveDown)
    {
        RemoveCommand = new RelayCommand(remove);
        MoveUpCommand = new RelayCommand(moveUp, () => CanMoveUp);
        MoveDownCommand = new RelayCommand(moveDown, () => CanMoveDown);
    }

    public ObservableCollection<ConditionExpressionTermViewModel> Terms { get; } = [];
    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public int Position => _position;
    public string Label => $"第 {Position} 组";
    public string Heading => Position == 1 ? "满足以下所有条件（与）" : "或者，满足以下所有条件（与）";
    public bool CanMoveUp => _canMoveUp;
    public bool CanMoveDown => _canMoveDown;

    internal void Refresh(int index, int count)
    {
        _position = index + 1;
        _canMoveUp = index > 0;
        _canMoveDown = index + 1 < count;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();

        for (var termIndex = 0; termIndex < Terms.Count; termIndex++)
        {
            Terms[termIndex].Refresh(termIndex, Terms.Count);
        }
    }
}

public sealed class ConditionExpressionTermViewModel : ObservableObject
{
    private readonly IReadOnlyList<ReferenceOptionViewModel> _options;
    private readonly Action _changed;
    private int _id;
    private bool _isNegated;
    private int _position;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public ConditionExpressionTermViewModel(
        int id,
        bool isNegated,
        IReadOnlyList<ReferenceOptionViewModel> options,
        Action<ConfigReferenceTarget>? navigateReference,
        Action changed,
        Action remove,
        Action moveUp,
        Action moveDown)
    {
        _id = id;
        _isNegated = isNegated;
        _options = options;
        _changed = changed;
        Action<ReferenceOptionViewModel>? navigate = navigateReference is null
            ? null
            : option =>
            {
                if (option.Target is not null)
                {
                    navigateReference(option.Target);
                }
            };
        ReferencePickerViewModel? picker = null;
        picker = new ReferencePickerViewModel(
            options,
            () => Id,
            option =>
            {
                if (option.Id == Id)
                {
                    return;
                }

                _id = option.Id;
                OnPropertyChanged(nameof(Id));
                OnPropertyChanged(nameof(IsMissing));
                picker!.RefreshSelection();
                _changed();
            },
            navigate);
        ReferencePicker = picker;
        RemoveCommand = new RelayCommand(remove);
        MoveUpCommand = new RelayCommand(moveUp, () => CanMoveUp);
        MoveDownCommand = new RelayCommand(moveDown, () => CanMoveDown);
    }

    public ReferencePickerViewModel ReferencePicker { get; }
    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public int Id => _id;
    public int Position => _position;
    public bool CanMoveUp => _canMoveUp;
    public bool CanMoveDown => _canMoveDown;
    public bool IsMissing => _options.FirstOrDefault(option => option.Id == Id)?.IsMissing != false;

    public bool IsNegated
    {
        get => _isNegated;
        set
        {
            if (SetProperty(ref _isNegated, value))
            {
                _changed();
            }
        }
    }

    internal void Refresh(int index, int count)
    {
        _position = index + 1;
        _canMoveUp = index > 0;
        _canMoveDown = index + 1 < count;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(IsMissing));
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
        ReferencePicker.RefreshOptions();
    }
}

public sealed class StructuredRuleStringEntryViewModel : ObservableObject
{
    private readonly Action<StructuredRuleStringEntryViewModel> _idChanged;
    private readonly Action<StructuredRuleStringEntryViewModel> _weightChanged;
    private int _id;
    private float? _weight;
    private string _weightText;
    private int _position;
    private bool _canMoveUp;
    private bool _canMoveDown;
    private bool _hasWeightError;

    public StructuredRuleStringEntryViewModel(
        int id,
        float? weight,
        bool showWeight,
        bool canReplaceReference,
        bool canReorder,
        IReadOnlyList<ReferenceOptionViewModel> options,
        Action<ConfigReferenceTarget>? navigateReference,
        Action<StructuredRuleStringEntryViewModel> idChanged,
        Action<StructuredRuleStringEntryViewModel> weightChanged,
        Action<StructuredRuleStringEntryViewModel> remove,
        Action<StructuredRuleStringEntryViewModel> moveUp,
        Action<StructuredRuleStringEntryViewModel> moveDown)
    {
        _id = id;
        _weight = weight;
        _weightText = weight?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
        _idChanged = idChanged;
        _weightChanged = weightChanged;
        ShowWeight = showWeight;
        CanReplaceReference = canReplaceReference;
        CanReorder = canReorder;
        Options = options;
        Action<ReferenceOptionViewModel>? navigate = navigateReference is null
            ? null
            : option =>
            {
                if (option.Target is not null)
                {
                    navigateReference(option.Target);
                }
            };
        ReferencePicker = new ReferencePickerViewModel(
            options,
            () => Id,
            option =>
            {
                if (CanReplaceReference && option.Id != Id)
                {
                    _id = option.Id;
                    _idChanged(this);
                }
            },
            navigate);
        RemoveCommand = new RelayCommand(() => remove(this));
        MoveUpCommand = new RelayCommand(() => moveUp(this), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => moveDown(this), () => CanMoveDown);
    }

    public IReadOnlyList<ReferenceOptionViewModel> Options { get; }
    public ReferencePickerViewModel ReferencePicker { get; }
    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public int Id => _id;
    public float? Weight => _weight;
    public bool ShowWeight { get; }
    public bool CanReplaceReference { get; }
    public bool ShowReadOnlyReference => !CanReplaceReference;
    public bool CanReorder { get; }
    public string ReferenceLabel =>
        ReferencePicker.SelectedOption?.Label ?? $"#{Id} · [目标不存在]";
    public int Position => _position;
    public bool CanMoveUp => _canMoveUp;
    public bool CanMoveDown => _canMoveDown;
    public bool HasWeightError => _hasWeightError;
    public bool IsMissing => Options.FirstOrDefault(option => option.Id == Id)?.IsMissing != false;

    public string WeightText
    {
        get => _weightText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _weightText, value))
            {
                return;
            }

            var isValid = float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) && float.IsFinite(parsed);
            SetProperty(ref _hasWeightError, !isValid, nameof(HasWeightError));

            if (isValid && _weight != parsed)
            {
                _weight = parsed;
                _weightChanged(this);
            }
        }
    }

    internal void ApplyId(int id)
    {
        _id = id;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(IsMissing));
        ReferencePicker.RefreshSelection();
        OnPropertyChanged(nameof(ReferenceLabel));
    }

    internal void ApplyWeight(float weight)
    {
        _weight = weight;
        _weightText = weight.ToString("R", CultureInfo.InvariantCulture);
        _hasWeightError = false;
        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(WeightText));
        OnPropertyChanged(nameof(HasWeightError));
    }

    internal void Refresh(int index, int count)
    {
        _position = index + 1;
        _canMoveUp = CanReorder && index > 0;
        _canMoveDown = CanReorder && index + 1 < count;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(IsMissing));
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
        ReferencePicker.RefreshOptions();
        OnPropertyChanged(nameof(ReferenceLabel));
    }
}
