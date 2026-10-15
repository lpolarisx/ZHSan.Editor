using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ZHSan.Editor.Desktop.Editors;

namespace ZHSan.Editor.Desktop.Views;

public partial class PersonBasicEditorView : UserControl
{
    public PersonBasicEditorView()
    {
        InitializeComponent();
    }

    private async void OnPortraitClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonBasicEditorViewModel editor ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var start = editor.PortraitDirectory is { Length: > 0 } current && Directory.Exists(current)
            ? await storage.TryGetFolderFromPathAsync(current)
            : null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择人物头像文件夹（包含 {头像序号}.jpg）",
            AllowMultiple = false,
            SuggestedStartLocation = start
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            editor.PortraitDirectory = path;
        }
    }
}
