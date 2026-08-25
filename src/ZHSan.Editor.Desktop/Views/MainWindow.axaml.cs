using Avalonia.Controls;
using Avalonia.Data;
using System.ComponentModel;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainWindowViewModel? _viewModel;

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

        for (var index = 0; index < document.Properties.Count; index++)
        {
            var property = document.Properties[index];
            RecordsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = property.DisplayName,
                Binding = new Binding($"Cells[{index}].DisplayValue"),
                SortMemberPath = $"Cells[{index}].SortValue",
                IsReadOnly = true,
                MinWidth = property.PropertyType == typeof(string) ? 150 : 80
            });
        }
    }

    private void RecordsGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is DataGrid dataGrid && _viewModel?.SelectedDocument is { } document)
        {
            document.SetSelectedRecords(dataGrid.SelectedItems);
        }
    }
}
