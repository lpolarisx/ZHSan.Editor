using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class FileSystemArchiveChangeMonitor : IArchiveChangeMonitor
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<EditorProject, WatchedArchive> _watched =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public event EventHandler<ArchiveExternalChangeEventArgs>? ExternalChangeDetected;

    public void Watch(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_syncRoot)
        {
            RemoveWatcher(project);
            _watched.Add(project, new WatchedArchive(project, CheckWatchedArchive));
        }
    }

    public void Stop(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_syncRoot)
        {
            RemoveWatcher(project);
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            foreach (var watched in _watched.Values)
            {
                watched.Dispose();
            }

            _watched.Clear();
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
        _disposed = true;
    }

    private void CheckWatchedArchive(EditorProject project)
    {
        WatchedArchive? watched;
        lock (_syncRoot)
        {
            if (!_watched.TryGetValue(project, out watched))
            {
                return;
            }
        }

        if (!HasChanged(project))
        {
            watched.LastReportedRevision = null;
            return;
        }

        string currentRevision;
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
            if (!_watched.TryGetValue(project, out var current) ||
                !ReferenceEquals(current, watched) ||
                currentRevision == watched.LastReportedRevision)
            {
                return;
            }

            watched.LastReportedRevision = currentRevision;
        }

        ExternalChangeDetected?.Invoke(
            this,
            new ArchiveExternalChangeEventArgs(Path.GetFullPath(project.ArchivePath)));
    }

    private void RemoveWatcher(EditorProject project)
    {
        if (_watched.Remove(project, out var watched))
        {
            watched.Dispose();
        }
    }

    private sealed class WatchedArchive : IDisposable
    {
        private readonly Timer _debounceTimer;
        private readonly FileSystemWatcher _watcher;

        public WatchedArchive(EditorProject project, Action<EditorProject> check)
        {
            Project = project;
            var archivePath = Path.GetFullPath(project.ArchivePath);
            _debounceTimer = new Timer(_ => check(Project), null, Timeout.Infinite, Timeout.Infinite);
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

        public EditorProject Project { get; }
        public string? LastReportedRevision { get; set; }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Deleted -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }

        private void OnFileChanged(object sender, FileSystemEventArgs eventArgs) =>
            _debounceTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
    }
}
