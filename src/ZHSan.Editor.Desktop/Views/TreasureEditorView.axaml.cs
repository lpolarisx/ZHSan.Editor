using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ZHSan.Editor.Desktop.Editors;

namespace ZHSan.Editor.Desktop.Views;

public partial class TreasureEditorView : UserControl
{
    public TreasureEditorView()
    {
        InitializeComponent();
    }

    private async void OnPictureClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TreasureEditorViewModel editor ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var start = editor.PictureDirectory is { Length: > 0 } current && Directory.Exists(current)
            ? await storage.TryGetFolderFromPathAsync(current)
            : null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择宝物图片文件夹（包含 {图片序号}.jpg）",
            AllowMultiple = false,
            SuggestedStartLocation = start
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            editor.PictureDirectory = path;
        }
    }
}
