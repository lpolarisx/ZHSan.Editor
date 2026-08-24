namespace ZHSan.Editor.Desktop.Services;

public interface IArchivePicker
{
    Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default);
}
