using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ReferencePickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<ReferenceOptionViewModel> _options;
    private readonly Func<int?> _getCurrentId;
    private readonly Action<ReferenceOptionViewModel> _select;
    private readonly Action<ReferenceOptionViewModel>? _navigate;
    private string _searchText = string.Empty;

    public ReferencePickerViewModel(
        IReadOnlyList<ReferenceOptionViewModel> options,
        Func<int?> getCurrentId,
        Action<ReferenceOptionViewModel> select,
        Action<ReferenceOptionViewModel>? navigate = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _getCurrentId = getCurrentId ?? throw new ArgumentNullException(nameof(getCurrentId));
        _select = select ?? throw new ArgumentNullException(nameof(select));
        _navigate = navigate;
        ClearSearchCommand = new RelayCommand(
            () => SearchText = string.Empty,
            () => SearchText.Length > 0);
        NavigateCommand = new RelayCommand(Navigate, CanNavigate);
        RefreshOptions();
    }

    public ObservableCollection<ReferenceOptionViewModel> FilteredOptions { get; } = [];
    public ICommand ClearSearchCommand { get; }
    public ICommand NavigateCommand { get; }

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
                     option.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            FilteredOptions.Add(option);
        }

        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(HasMissingSelection));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(ResultSummary));
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
