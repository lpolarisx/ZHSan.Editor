using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using GameDatas;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class FacilityKindLevelEditorViewModel : ObservableObject, IDisposable
{
    private const string FacilityKindsKey = "facility-kinds";
    private readonly ConfigEditorContext _context;
    private FacilityKindOptionViewModel? _selectedKind;
    private FacilityLevelViewModel? _selectedLevel;
    private string _statusMessage = string.Empty;
    private bool _isSynchronizingSelection;
    private bool _isDisposed;

    public FacilityKindLevelEditorViewModel(ConfigEditorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        AddLevelCommand = new RelayCommand(AddLevel, CanAddLevel);
        _context.Document.StateChanged += DocumentOnStateChanged;
        _context.Document.PropertyChanged += DocumentOnPropertyChanged;
        _context.Document.ReferenceOptionsChanged += DocumentOnReferenceOptionsChanged;
        Rebuild();
    }

    public ObservableCollection<FacilityKindOptionViewModel> KindOptions { get; } = [];
    public ObservableCollection<FacilityLevelViewModel> Levels { get; } = [];
    public ICommand AddLevelCommand { get; }

    public int KindCount => KindOptions.Count(option => !option.IsMissing);
    public int LevelCount => _context.Records.Count(record => record.Item is FacilityKindLevelConfig);
    public int VisibleLevelCount => Levels.Count;
    public int IssueCount => Levels.Sum(level => level.Issues.Count);
    public string Summary => $"{KindCount} 个设施种类 · {LevelCount} 条等级配置 · {IssueCount} 个问题";
    public bool HasKindSelection => SelectedKind is not null;
    public bool HasLevelSelection => SelectedLevel is not null;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public FacilityKindOptionViewModel? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (ReferenceEquals(_selectedKind, value))
            {
                return;
            }

            _selectedKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasKindSelection));
            RebuildLevels();
            ((RelayCommand)AddLevelCommand).RaiseCanExecuteChanged();
        }
    }

    public FacilityLevelViewModel? SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (ReferenceEquals(_selectedLevel, value))
            {
                return;
            }

            _selectedLevel = value;
            OnPropertyChanged();
            RefreshSelectedLevelProperties();
            if (!_isSynchronizingSelection)
            {
                _context.SelectedRecord = value?.Record;
            }
        }
    }

    public string SelectedId => SelectedLevel?.Config.Id.ToString() ?? string.Empty;

    public FacilityKindOptionViewModel? SelectedKindForLevel
    {
        get => SelectedLevel is null
            ? null
            : KindOptions.FirstOrDefault(option => option.Id == SelectedLevel.Config.KindId);
        set
        {
            if (value is not null && !value.IsMissing)
            {
                SetSelectedInt(nameof(FacilityKindLevelConfig.KindId), value.Id, "调整设施所属种类");
            }
        }
    }

    public decimal? SelectedLevelNumber
    {
        get => SelectedLevel?.Config.Level;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.Level), value, "修改设施等级");
    }

    public decimal? SelectedPositionOccupied
    {
        get => SelectedLevel?.Config.PositionOccupied;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.PositionOccupied), value, "修改占地");
    }

    public decimal? SelectedPointCost
    {
        get => SelectedLevel?.Config.PointCost;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.PointCost), value, "修改点数成本");
    }

    public decimal? SelectedFundCost
    {
        get => SelectedLevel?.Config.FundCost;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.FundCost), value, "修改资金成本");
    }

    public decimal? SelectedMaintenanceCost
    {
        get => SelectedLevel?.Config.MaintenanceCost;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.MaintenanceCost), value, "修改维护成本");
    }

    public decimal? SelectedDays
    {
        get => SelectedLevel?.Config.Days;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.Days), value, "修改建设天数");
    }

    public decimal? SelectedEndurance
    {
        get => SelectedLevel?.Config.Endurance;
        set => SetSelectedInt(nameof(FacilityKindLevelConfig.Endurance), value, "修改耐久");
    }

    public string SelectedIssueSummary => SelectedLevel is null
        ? string.Empty
        : SelectedLevel.Issues.Count == 0
            ? "当前设施种类与等级组合正常"
            : string.Join(Environment.NewLine, SelectedLevel.Issues.Select(issue => $"• {issue}"));

    public void Rebuild()
    {
        var selectedRecord = SelectedLevel?.Record ?? _context.SelectedRecord;
        var previousKindId = SelectedKind?.Id;
        var configs = GetConfigs();
        var targets = _context.GetReferenceTargets(FacilityKindsKey);

        KindOptions.Clear();
        foreach (var targetGroup in targets.GroupBy(target => target.Id).OrderBy(group => group.Key))
        {
            var target = targetGroup.First();
            var duplicate = targetGroup.Count() > 1;
            KindOptions.Add(new FacilityKindOptionViewModel(
                target.Id,
                duplicate
                    ? $"#{target.Id} · {target.DisplayName} [种类 ID 重复]"
                    : $"#{target.Id} · {target.DisplayName}",
                duplicate,
                configs.Count(item => item.Config.KindId == target.Id)));
        }

        foreach (var missingKindId in configs.Select(item => item.Config.KindId)
                     .Distinct()
                     .Where(id => KindOptions.All(option => option.Id != id))
                     .Order())
        {
            KindOptions.Add(new FacilityKindOptionViewModel(
                missingKindId,
                $"#{missingKindId} · [设施种类不存在]",
                true,
                configs.Count(item => item.Config.KindId == missingKindId)));
        }

        var selectedConfig = selectedRecord?.Item as FacilityKindLevelConfig;
        var desiredKindId = selectedConfig?.KindId ?? previousKindId;
        _selectedKind = desiredKindId.HasValue
            ? KindOptions.FirstOrDefault(option => option.Id == desiredKindId.Value)
            : null;
        _selectedKind ??= KindOptions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(HasKindSelection));
        RebuildLevels(selectedRecord);
        OnPropertyChanged(nameof(KindCount));
        OnPropertyChanged(nameof(LevelCount));
        OnPropertyChanged(nameof(IssueCount));
        OnPropertyChanged(nameof(Summary));
        ((RelayCommand)AddLevelCommand).RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _context.Document.StateChanged -= DocumentOnStateChanged;
        _context.Document.PropertyChanged -= DocumentOnPropertyChanged;
        _context.Document.ReferenceOptionsChanged -= DocumentOnReferenceOptionsChanged;
    }

    private void RebuildLevels(ConfigRecordViewModel? selectedRecord = null)
    {
        selectedRecord ??= SelectedLevel?.Record;
        Levels.Clear();
        if (SelectedKind is null)
        {
            SetSelectedLevel(null);
            RefreshCountsAndStatus();
            return;
        }

        var all = GetConfigs();
        var duplicateIds = all.GroupBy(item => item.Config.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var selectedKindConfigs = all
            .Where(item => item.Config.KindId == SelectedKind.Id)
            .OrderBy(item => item.Config.Level)
            .ThenBy(item => item.Config.Id)
            .ToArray();
        var duplicateLevels = selectedKindConfigs.GroupBy(item => item.Config.Level)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var positiveLevels = selectedKindConfigs.Select(item => item.Config.Level)
            .Where(level => level > 0)
            .Distinct()
            .ToHashSet();

        foreach (var (record, config) in selectedKindConfigs)
        {
            var issues = new List<string>();
            if (SelectedKind.IsMissing)
            {
                issues.Add($"设施种类 #{config.KindId} 不存在或 ID 重复。");
            }
            if (duplicateIds.Contains(config.Id))
            {
                issues.Add($"记录 ID {config.Id} 重复。");
            }
            if (config.Level <= 0)
            {
                issues.Add("设施等级必须大于 0。");
            }
            if (duplicateLevels.Contains(config.Level))
            {
                issues.Add($"设施种类 #{config.KindId} 已存在等级 {config.Level}。");
            }
            if (config.Level > 1 &&
                positiveLevels.Count(level => level < config.Level) != (long)config.Level - 1)
            {
                issues.Add("等级序列不连续，存在缺失的前置等级。");
            }

            Levels.Add(new FacilityLevelViewModel(record, config, issues));
        }

        SetSelectedLevel(Levels.FirstOrDefault(level => ReferenceEquals(level.Record, selectedRecord))
            ?? Levels.FirstOrDefault());
        RefreshCountsAndStatus();
    }

    private void SetSelectedLevel(FacilityLevelViewModel? level)
    {
        _isSynchronizingSelection = true;
        SelectedLevel = level;
        _isSynchronizingSelection = false;
        _context.SelectedRecord = level?.Record;
    }

    private void RefreshCountsAndStatus()
    {
        OnPropertyChanged(nameof(VisibleLevelCount));
        OnPropertyChanged(nameof(IssueCount));
        OnPropertyChanged(nameof(Summary));
        StatusMessage = SelectedKind is null
            ? "没有可用的设施种类"
            : Levels.Count == 0
                ? $"{SelectedKind.Label} 尚未配置等级"
                : IssueCount == 0
                    ? $"{SelectedKind.Label} 的等级组合正常"
                    : $"当前种类检测到 {IssueCount} 个组合问题";
    }

    private void AddLevel()
    {
        if (!CanAddLevel() || SelectedKind is null)
        {
            return;
        }

        var configs = GetConfigs();
        var maxId = configs.Select(item => item.Config.Id).DefaultIfEmpty(0).Max();
        if (maxId == int.MaxValue)
        {
            StatusMessage = "无法自动分配新的记录 ID";
            return;
        }

        var nextLevel = configs.Where(item => item.Config.KindId == SelectedKind.Id)
            .Select(item => item.Config.Level)
            .Where(level => level > 0)
            .DefaultIfEmpty(0)
            .Max();
        if (nextLevel == int.MaxValue)
        {
            StatusMessage = "无法自动分配新的设施等级";
            return;
        }

        nextLevel++;
        _context.AddRecord(
            new Dictionary<string, object?>
            {
                [nameof(FacilityKindLevelConfig.Id)] = Math.Max(1, maxId + 1),
                [nameof(FacilityKindLevelConfig.KindId)] = SelectedKind.Id,
                [nameof(FacilityKindLevelConfig.Level)] = nextLevel,
            },
            $"新增 {SelectedKind.Label} 的等级 {nextLevel}");
        StatusMessage = $"已新增等级 {nextLevel}";
    }

    private bool CanAddLevel() => SelectedKind is { IsMissing: false };

    private void SetSelectedInt(string propertyName, decimal? value, string description)
    {
        if (SelectedLevel is null || !value.HasValue)
        {
            return;
        }

        int converted;
        try
        {
            converted = decimal.ToInt32(value.Value);
        }
        catch (OverflowException)
        {
            StatusMessage = "输入值超出 32 位整数范围";
            RefreshSelectedLevelProperties();
            return;
        }

        _context.SetPropertyValue(SelectedLevel.Record, propertyName, converted, description);
        StatusMessage = $"已{description}";
    }

    private void RefreshSelectedLevelProperties()
    {
        OnPropertyChanged(nameof(HasLevelSelection));
        OnPropertyChanged(nameof(SelectedId));
        OnPropertyChanged(nameof(SelectedKindForLevel));
        OnPropertyChanged(nameof(SelectedLevelNumber));
        OnPropertyChanged(nameof(SelectedPositionOccupied));
        OnPropertyChanged(nameof(SelectedPointCost));
        OnPropertyChanged(nameof(SelectedFundCost));
        OnPropertyChanged(nameof(SelectedMaintenanceCost));
        OnPropertyChanged(nameof(SelectedDays));
        OnPropertyChanged(nameof(SelectedEndurance));
        OnPropertyChanged(nameof(SelectedIssueSummary));
    }

    private (ConfigRecordViewModel Record, FacilityKindLevelConfig Config)[] GetConfigs() =>
        _context.Records
            .Where(record => record.Item is FacilityKindLevelConfig)
            .Select(record => (record, (FacilityKindLevelConfig)record.Item))
            .ToArray();

    private void DocumentOnStateChanged(object? sender, EventArgs eventArgs) => Rebuild();
    private void DocumentOnReferenceOptionsChanged(object? sender, EventArgs eventArgs) => Rebuild();

    private void DocumentOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(ConfigDocumentViewModel.SelectedRecord) || _isSynchronizingSelection)
        {
            return;
        }

        if (_context.SelectedRecord?.Item is not FacilityKindLevelConfig config)
        {
            SetSelectedLevel(null);
            return;
        }

        if (SelectedKind?.Id != config.KindId)
        {
            _selectedKind = KindOptions.FirstOrDefault(option => option.Id == config.KindId);
            OnPropertyChanged(nameof(SelectedKind));
            OnPropertyChanged(nameof(HasKindSelection));
            RebuildLevels(_context.SelectedRecord);
            return;
        }

        _isSynchronizingSelection = true;
        SelectedLevel = Levels.FirstOrDefault(level => ReferenceEquals(level.Record, _context.SelectedRecord));
        _isSynchronizingSelection = false;
    }
}

public sealed record FacilityKindOptionViewModel(
    int Id,
    string Label,
    bool IsMissing,
    int LevelCount)
{
    public string CountLabel => $"{LevelCount} 级";
}

public sealed class FacilityLevelViewModel(
    ConfigRecordViewModel record,
    FacilityKindLevelConfig config,
    IReadOnlyList<string> issues)
{
    public ConfigRecordViewModel Record { get; } = record;
    public FacilityKindLevelConfig Config { get; } = config;
    public IReadOnlyList<string> Issues { get; } = issues;
    public int Id => Config.Id;
    public int Level => Config.Level;
    public bool HasIssues => Issues.Count > 0;
    public string Title => $"等级 {Level}";
    public string CostSummary => $"点数 {Config.PointCost} · 资金 {Config.FundCost} · 维护 {Config.MaintenanceCost}";
    public string BuildSummary => $"占地 {Config.PositionOccupied} · 工期 {Config.Days} 天 · 耐久 {Config.Endurance}";
    public string ToolTip => HasIssues ? string.Join(Environment.NewLine, Issues) : $"记录 #{Id}";
}
