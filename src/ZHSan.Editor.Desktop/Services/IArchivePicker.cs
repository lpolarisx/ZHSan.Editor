namespace ZHSan.Editor.Desktop.Services;

public interface IArchivePicker
{
    Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default);

    Task<string?> PickSaveArchiveAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);

    Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken = default) =>
        PickArchiveAsync(cancellationToken);

    Task<string?> PickConfigJsonAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
