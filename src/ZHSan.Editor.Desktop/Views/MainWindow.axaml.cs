using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using System.ComponentModel;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private MainWindowViewModel? _viewModel;
    private ConfigDocumentViewModel? _columnsDocument;
    private bool _allowWindowClose;
    private bool _isClosePending;

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
                MinWidth = property.PropertyType == typeof(string) ? 150 : 80
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
    }

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
