using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Views;

public partial class OpenScenarioWindow : Window
{
    public OpenScenarioWindow() => InitializeComponent();

    private async void SelectScenario(object? sender, RoutedEventArgs e) => await SelectFileAsync(false);
    private async void SelectCommon(object? sender, RoutedEventArgs e) => await SelectFileAsync(true);

    private async Task SelectFileAsync(bool common)
    {
        if (DataContext is not OpenScenarioViewModel model) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = common ? "选择公共配置 CommonData" : "选择剧本 / 存档",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ZHSan 游戏数据") { Patterns = ["*.dat"] }, FilePickerFileTypes.All]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                if (common) model.CommonPath = path;
                else model.ScenarioPath = path;
            }
        }
        catch (Exception exception)
        {
            model.ShowPickerError($"无法选择文件：{exception.GetBaseException().Message}");
        }
    }

    private void Load(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OpenScenarioViewModel model && model.GetSelection() is { } selection)
        {
            Close(selection);
        }
    }

    private void Cancel(object? sender, RoutedEventArgs e) => Close((ScenarioArchiveSelection?)null);
}
