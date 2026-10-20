using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ReferencePickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<ReferenceOptionViewModel> _options;
    private readonly Func<int?> _getCurrentId;
    private readonly Action<ReferenceOptionViewModel> _select;
    private readonly Action<ReferenceOptionViewModel>? _navigate;
    private readonly Func<int, bool>? _isAdded;
    private string _searchText = string.Empty;
    private int _matchCount;

    public ReferencePickerViewModel(
        IReadOnlyList<ReferenceOptionViewModel> options,
        Func<int?> getCurrentId,
        Action<ReferenceOptionViewModel> select,
        Action<ReferenceOptionViewModel>? navigate = null,
        Func<int, bool>? isAdded = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _getCurrentId = getCurrentId ?? throw new ArgumentNullException(nameof(getCurrentId));
        _select = select ?? throw new ArgumentNullException(nameof(select));
        _navigate = navigate;
        _isAdded = isAdded;
        ClearSearchCommand = new RelayCommand(
            () => SearchText = string.Empty,
            () => SearchText.Length > 0);
        NavigateCommand = new RelayCommand(Navigate, CanNavigate);
        RefreshOptions();
    }

    /// <summary>Most suggestions an add box lists; typing narrows the rest down.</summary>
    public const int MaxSuggestions = 100;

    public ObservableCollection<ReferenceOptionViewModel> FilteredOptions { get; } = [];

    /// <summary>The matches an add box offers, marking those already added. Only filled when adding.</summary>
    public ObservableCollection<ReferenceSuggestionViewModel> Suggestions { get; } = [];
    public string Placeholder { get; init; } = "输入名称、ID 或描述添加…";
    public bool HasSuggestions => Suggestions.Count > 0;
    public string SuggestionSummary => _matchCount == 0
        ? "没有匹配项"
        : _matchCount > Suggestions.Count
            ? $"共 {_matchCount} 项匹配，显示前 {Suggestions.Count} 项；继续输入以缩小范围"
            : $"共 {_matchCount} 项匹配";
    public ICommand ClearSearchCommand { get; }
    public ICommand NavigateCommand { get; }
    public bool SupportsNavigation => _navigate is not null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ((RelayCommand)ClearSearchCommand).RaiseCanExecuteChanged();
                ApplyFilter();
            }
        }
    }

    public ReferenceOptionViewModel? SelectedOption
    {
        get
        {
            var currentId = _getCurrentId();
            return currentId.HasValue
                ? _options.FirstOrDefault(option => option.Id == currentId.Value)
                : null;
        }
        set
        {
            if (value is null || value.Id == _getCurrentId())
            {
                return;
            }

            _select(value);
            RefreshSelection();
        }
    }

    public bool HasMissingSelection => SelectedOption?.IsMissing == true;
    public bool HasNoResults => FilteredOptions.Count == 0;
    public string ResultSummary => SearchText.Length == 0
        ? $"{_options.Count} 个可选目标"
        : $"显示 {FilteredOptions.Count} / {_options.Count} 个目标";

    /// <summary>Adds the suggestion unless it is already added, then clears the search for the next one.</summary>
    public bool Add(ReferenceOptionViewModel option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (_isAdded?.Invoke(option.Id) == true)
        {
            return false;
        }

        _select(option);
        SearchText = string.Empty;
        RefreshSuggestions();
        return true;
    }

    public void RefreshSuggestions()
    {
        if (_isAdded is null)
        {
            return;
        }

        Suggestions.Clear();
        foreach (var option in FilteredOptions.Take(MaxSuggestions))
        {
            Suggestions.Add(new ReferenceSuggestionViewModel(option, _isAdded(option.Id)));
        }

        _matchCount = FilteredOptions.Count;
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(SuggestionSummary));
    }

    public void RefreshOptions()
    {
        ApplyFilter();
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(HasMissingSelection));
        ((RelayCommand)NavigateCommand).RaiseCanExecuteChanged();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var selectedId = _getCurrentId();
        FilteredOptions.Clear();
        foreach (var option in _options.Where(option =>
                     query.Length == 0 ||
                     option.Id == selectedId ||
                     option.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     option.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                     option.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true))
        {
            FilteredOptions.Add(option);
        }

        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(HasMissingSelection));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(ResultSummary));
        RefreshSuggestions();
    }

    private bool CanNavigate() =>
        _navigate is not null && SelectedOption is { IsMissing: false, Target: not null };

    private void Navigate()
    {
        if (SelectedOption is { IsMissing: false, Target: not null } option)
        {
            _navigate?.Invoke(option);
        }
    }
}

public sealed record ReferenceSuggestionViewModel(ReferenceOptionViewModel Option, bool IsAdded)
{
    public string Label => Option.Label;
    public string? Description => Option.Description;
    public bool HasDescription => Option.HasDescription;
}
