using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Abstractions;

public interface IArchiveChangeMonitor : IDisposable
{
    event EventHandler<ArchiveExternalChangeEventArgs>? ExternalChangeDetected;

    void Watch(EditorProject project);

    void Stop();

    bool HasChanged(EditorProject project);
}

public sealed class ArchiveExternalChangeEventArgs(string archivePath) : EventArgs
{
    public string ArchivePath { get; } = archivePath;
}
