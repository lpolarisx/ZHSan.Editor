using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class FileSystemArchiveChangeMonitor : IArchiveChangeMonitor
{
    private readonly object _syncRoot = new();
    private readonly Timer _debounceTimer;
    private FileSystemWatcher? _watcher;
    private EditorProject? _project;
    private string? _lastReportedRevision;
    private bool _disposed;

    public FileSystemArchiveChangeMonitor()
    {
        _debounceTimer = new Timer(CheckWatchedArchive, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<ArchiveExternalChangeEventArgs>? ExternalChangeDetected;

    public void Watch(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var archivePath = Path.GetFullPath(project.ArchivePath);
        lock (_syncRoot)
        {
            DisposeWatcher();
            _project = project;
            _lastReportedRevision = null;
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(archivePath)!, Path.GetFileName(archivePath))
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            DisposeWatcher();
            _project = null;
            _lastReportedRevision = null;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public bool HasChanged(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        try
        {
            return !string.Equals(
                project.ArchiveRevision,
                ArchiveFileRevision.Read(project.ArchivePath),
                StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _debounceTimer.Dispose();
        _disposed = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs eventArgs) =>
        _debounceTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);

    private void CheckWatchedArchive(object? state)
    {
        EditorProject? project;
        lock (_syncRoot)
        {
            project = _project;
        }

        if (project is null || !HasChanged(project))
        {
            lock (_syncRoot)
            {
                _lastReportedRevision = null;
            }

            return;
        }

        string? currentRevision;
        try
        {
            currentRevision = ArchiveFileRevision.Read(project.ArchivePath) ?? "<missing>";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            currentRevision = "<unreadable>";
        }

        lock (_syncRoot)
        {
            if (!ReferenceEquals(project, _project) || currentRevision == _lastReportedRevision)
            {
                return;
            }

            _lastReportedRevision = currentRevision;
        }

        ExternalChangeDetected?.Invoke(
            this,
            new ArchiveExternalChangeEventArgs(Path.GetFullPath(project.ArchivePath)));
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Renamed -= OnFileChanged;
        _watcher.Dispose();
        _watcher = null;
    }
}
