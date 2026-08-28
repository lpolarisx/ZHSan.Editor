using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using System.ComponentModel;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private const double NavigationPaneDefaultWidth = 250;
    private const double NavigationPaneMinWidth = 180;
    private const double NavigationPaneMaxWidth = 520;
    private const double DetailsPaneDefaultWidth = 380;
    private const double DetailsPaneMinWidth = 280;
    private const double DetailsPaneMaxWidth = 720;
    private const double SplitterWidth = 5;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private MainWindowViewModel? _viewModel;
    private ConfigDocumentViewModel? _columnsDocument;
    private bool _allowWindowClose;
    private bool _isClosePending;
    private ColumnDefinition NavigationPaneColumn => MainWorkspaceGrid.ColumnDefinitions[0];
    private ColumnDefinition NavigationSplitterColumn => MainWorkspaceGrid.ColumnDefinitions[1];
    private ColumnDefinition DetailsSplitterColumn => MainWorkspaceGrid.ColumnDefinitions[3];
    private ColumnDefinition DetailsPaneColumn => MainWorkspaceGrid.ColumnDefinitions[4];

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        ApplySavedWindowLayout();
        BuildRecordColumns();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainWindowViewModel.SelectedDocument))
        {
            BuildRecordColumns();
        }
        else if (eventArgs.PropertyName is nameof(MainWindowViewModel.IsNavigationPaneVisible) or
                 nameof(MainWindowViewModel.IsDetailsPaneVisible))
        {
            SaveCurrentWorkspacePaneWidths();
            ApplyWorkspacePaneLayout();
        }
    }

    private void BuildRecordColumns()
    {
        SaveCurrentColumnWidths();
        if (RecordsGrid is null)
        {
            return;
        }

        RecordsGrid.Columns.Clear();
        var document = _viewModel?.SelectedDocument;
        if (document is null)
        {
            return;
        }

        _columnsDocument = document;
        for (var index = 0; index < document.Properties.Count; index++)
        {
            var property = document.Properties[index];
            var column = new DataGridTextColumn
            {
                Header = property.DisplayName,
                Binding = new Binding($"Cells[{index}].DisplayValue"),
                SortMemberPath = $"Cells[{index}].SortValue",
                IsReadOnly = true,
                MinWidth = property.Reference is not null || property.PropertyType == typeof(string) ? 150 : 80
            };
            if (index < document.SavedColumnWidths.Count)
            {
                column.Width = new DataGridLength(document.SavedColumnWidths[index]);
            }

            RecordsGrid.Columns.Add(column);
        }
    }

    private void SaveCurrentColumnWidths()
    {
        if (_columnsDocument is null || RecordsGrid is null || RecordsGrid.Columns.Count == 0)
        {
            return;
        }

        _columnsDocument.SaveColumnWidths(RecordsGrid.Columns.Select(column => column.ActualWidth));
        _columnsDocument = null;
    }

    private void ApplySavedWindowLayout()
    {
        if (_viewModel is null)
        {
            return;
        }

        var state = _viewModel.UiState;
        if (double.IsFinite(state.WindowWidth) && state.WindowWidth >= MinWidth)
        {
            Width = state.WindowWidth;
        }

        if (double.IsFinite(state.WindowHeight) && state.WindowHeight >= MinHeight)
        {
            Height = state.WindowHeight;
        }

        if (state.WindowX.HasValue && state.WindowY.HasValue)
        {
            Position = new PixelPoint(state.WindowX.Value, state.WindowY.Value);
        }

        ApplyWorkspacePaneLayout();
    }

    private void ApplyWorkspacePaneLayout()
    {
        if (_viewModel is null)
        {
            return;
        }

        var state = _viewModel.UiState;
        if (_viewModel.IsNavigationPaneVisible)
        {
            NavigationPaneColumn.MinWidth = NavigationPaneMinWidth;
            NavigationPaneColumn.Width = new GridLength(ClampWidth(
                state.NavigationPaneWidth,
                NavigationPaneDefaultWidth,
                NavigationPaneMinWidth,
                NavigationPaneMaxWidth));
            NavigationSplitterColumn.Width = new GridLength(SplitterWidth);
        }
        else
        {
            NavigationPaneColumn.MinWidth = 0;
            NavigationPaneColumn.Width = new GridLength(0);
            NavigationSplitterColumn.Width = new GridLength(0);
        }

        if (_viewModel.IsDetailsPaneVisible)
        {
            DetailsPaneColumn.MinWidth = DetailsPaneMinWidth;
            DetailsPaneColumn.Width = new GridLength(ClampWidth(
                state.DetailsPaneWidth,
                DetailsPaneDefaultWidth,
                DetailsPaneMinWidth,
                DetailsPaneMaxWidth));
            DetailsSplitterColumn.Width = new GridLength(SplitterWidth);
        }
        else
        {
            DetailsPaneColumn.MinWidth = 0;
            DetailsPaneColumn.Width = new GridLength(0);
            DetailsSplitterColumn.Width = new GridLength(0);
        }
    }

    private void SaveCurrentWorkspacePaneWidths()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.UpdateWorkspacePaneWidths(
            NavigationPaneColumn.ActualWidth,
            DetailsPaneColumn.ActualWidth);
    }

    private static double ClampWidth(double width, double defaultWidth, double minWidth, double maxWidth) =>
        double.IsFinite(width) ? Math.Clamp(width, minWidth, maxWidth) : defaultWidth;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowWindowClose || _viewModel is null)
        {
            SaveCurrentColumnWidths();
            SaveWindowState();
            return;
        }

        eventArgs.Cancel = true;
        if (_isClosePending)
        {
            return;
        }

        _isClosePending = true;
        try
        {
            if (await _viewModel.TryCloseProjectAsync())
            {
                _allowWindowClose = true;
                Close();
            }
        }
        finally
        {
            _isClosePending = false;
        }
    }

    private void SaveWindowState()
    {
        if (_viewModel is null)
        {
            return;
        }

        SaveCurrentWorkspacePaneWidths();
        _viewModel.UpdateWindowLayout(Width, Height, Position.X, Position.Y);
        _viewModel.SaveUiState();
    }

    private void RecordsGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is DataGrid dataGrid && _viewModel?.SelectedDocument is { } document)
        {
            document.SetSelectedRecords(dataGrid.SelectedItems);
        }
    }

    private void ValidationIssues_OnDoubleTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (sender is ListBox { SelectedItem: ValidationIssueViewModel issue } &&
            issue.NavigateCommand.CanExecute(null))
        {
            issue.NavigateCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.F ||
            !eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            DetailsTabs.SelectedIndex = 2;
            GlobalSearchBox.Focus();
            GlobalSearchBox.SelectAll();
        }
        else
        {
            CurrentSearchBox.Focus();
            CurrentSearchBox.SelectAll();
        }

        eventArgs.Handled = true;
    }
}
