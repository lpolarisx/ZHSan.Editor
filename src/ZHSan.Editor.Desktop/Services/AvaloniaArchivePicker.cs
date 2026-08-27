using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ZHSan.Editor.Desktop.Services;

public sealed class AvaloniaArchivePicker(Window owner) : IArchivePicker
{
    private static readonly FilePickerFileType ArchiveFileType = new("ZHSan 游戏数据")
    {
        Patterns = ["*.dat"]
    };

    private static readonly FilePickerFileType JsonFileType = new("JSON 配置")
    {
        Patterns = ["*.json"]
    };

    public async Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开游戏数据档案",
            AllowMultiple = false,
            FileTypeFilter = [ArchiveFileType, FilePickerFileTypes.All]
        });

        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveArchiveAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存游戏数据档案",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "dat",
            FileTypeChoices = [ArchiveFileType, FilePickerFileTypes.All],
            ShowOverwritePrompt = true
        });

        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken = default)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "从游戏数据档案批量导入",
            AllowMultiple = false,
            FileTypeFilter = [ArchiveFileType, FilePickerFileTypes.All]
        });

        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickConfigJsonAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"导入配置 {suggestedFileName}",
            AllowMultiple = false,
            SuggestedStartLocation = null,
            FileTypeFilter = [JsonFileType, FilePickerFileTypes.All]
        });

        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveConfigJsonAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出当前配置 JSON",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType, FilePickerFileTypes.All],
            ShowOverwritePrompt = true
        });

        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择全项目 JSON 导出目录",
            AllowMultiple = false
        });

        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickPublishArchiveAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "发布游戏配置包（请选择工作档案之外的位置）",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "dat",
            FileTypeChoices = [ArchiveFileType, FilePickerFileTypes.All],
            ShowOverwritePrompt = true
        });

        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }
}
