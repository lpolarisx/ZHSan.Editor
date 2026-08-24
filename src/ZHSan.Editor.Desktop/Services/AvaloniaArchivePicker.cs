using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ZHSan.Editor.Desktop.Services;

public sealed class AvaloniaArchivePicker(Window owner) : IArchivePicker
{
    public async Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开游戏数据档案",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZHSan 游戏数据") { Patterns = ["*.dat"] },
                FilePickerFileTypes.All
            ]
        });

        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
