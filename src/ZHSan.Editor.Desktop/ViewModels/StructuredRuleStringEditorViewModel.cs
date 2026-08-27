using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class StructuredRuleStringEditorViewModel : ObservableObject
{
    private const int NegateNextConditionId = 996;
    private const int OrConditionId = 997;
    private readonly ConfigStructuredStringDefinition _definition;
    private readonly Func<string> _getValue;
    private readonly Action<string> _setValue;
    private readonly IReadOnlyList<ReferenceOptionViewModel> _options;
    private readonly Action<ConfigReferenceTarget>? _navigateReference;
    private IReadOnlyList<string> _parseErrors = [];
    private bool _isSynchronizing;

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
        _navigateReference = navigateReference;
        AddPicker = new ReferencePickerViewModel(
            _options,
            () => null,
            option => Add(option.Id));
        ReloadFromValue(_getValue());
    }

    public ObservableCollection<StructuredRuleStringEntryViewModel> Entries { get; } = [];
    public ReferencePickerViewModel AddPicker { get; }
    public string FormatDescription => _definition.FormatDescription;
    public bool IsWeighted => _definition.Kind == ConfigStructuredStringKind.WeightedConditionPairs;
    public bool IsInfluenceList => _definition.Kind == ConfigStructuredStringKind.InfluenceIds;
    public bool CanUseStructuredEditor => _parseErrors.Count == 0;
    public bool HasIssues => IssueCount > 0;
    public int IssueCount => GetIssues().Count;
    public string IssueSummary => string.Join(Environment.NewLine, GetIssues().Select(issue => $"• {issue}"));
    public string Summary => IsWeighted
        ? $"{Entries.Count} 组条件权重"
        : $"{Entries.Count} 个{(_definition.Kind == ConfigStructuredStringKind.InfluenceIds ? "影响" : "条件")}";

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
        _isSynchronizing = true;
        Entries.Clear();
        if (IsWeighted)
        {
            var parsed = ConfigStructuredStringCodec.ParseWeightedConditions(value);
            _parseErrors = parsed.Errors;
            foreach (var item in parsed.Items)
            {
                Entries.Add(CreateEntry(item.ConditionId, item.Weight));
            }
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
        AddPicker.RefreshOptions();
        foreach (var entry in Entries)
        {
            entry.ReferencePicker.RefreshOptions();
        }

        RefreshState();
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

        Entries.Add(CreateEntry(id, IsWeighted ? 1f : null));
        CommitEntries();
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

        if (_definition.Kind == ConfigStructuredStringKind.ConditionIds)
        {
            for (var index = 0; index < Entries.Count; index++)
            {
                if (Entries[index].Id == OrConditionId &&
                    (index == 0 || index == Entries.Count - 1 || Entries[index - 1].Id == OrConditionId))
                {
                    issues.Add("997（或以下条件）不能位于开头、结尾或紧邻另一个 997。");
                }

                if (Entries[index].Id == NegateNextConditionId &&
                    (index == Entries.Count - 1 || Entries[index + 1].Id == OrConditionId))
                {
                    issues.Add("996（否定下一项）后必须紧跟一个普通条件。");
                }
            }
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private void RefreshState()
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            Entries[index].Refresh(index, Entries.Count);
        }

        OnPropertyChanged(nameof(RawText));
        OnPropertyChanged(nameof(CanUseStructuredEditor));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssueCount));
        OnPropertyChanged(nameof(IssueSummary));
        OnPropertyChanged(nameof(Summary));
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
