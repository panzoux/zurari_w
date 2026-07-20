using System.Linq;
using Zurari.Core;

namespace Zurari.Runtime;

/// <summary>
/// Watches a live set of directory paths for filesystem changes made outside zurari (e.g. Explorer
/// pasting a file into a directory a column is currently showing) and posts a debounced
/// <see cref="Msg.ExternalDirectoryChanged"/> per path. One <see cref="System.IO.FileSystemWatcher"/>
/// is kept per distinct watched path; <see cref="SetWatchedPaths"/> reconciles that set against
/// whatever <see cref="Zurari.App.MainWindow"/> currently shows, every render.
/// </summary>
/// <remarks>
/// <see cref="System.IO.FileSystemWatcher"/> is flaky in practice - a watched directory can vanish
/// out from under it, network drives can drop events or error out entirely - so every event/error
/// handler here swallows its own failures rather than letting one bad watcher take the process down;
/// at worst that one path silently stops being watched (see <see cref="WatchedDirectory.OnError"/>).
/// </remarks>
public sealed class DirectoryWatcher : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly Action<Msg> _post;
    private readonly object _gate = new();
    private readonly Dictionary<string, WatchedDirectory> _watched = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// <paramref name="post"/> may be invoked from an arbitrary <see cref="System.IO.FileSystemWatcher"/>
    /// callback thread or from a debounce timer thread; the caller is responsible for marshalling
    /// each posted <see cref="Msg"/> onto whatever thread owns application state (the UI thread in
    /// the real app) - same contract as <see cref="JobEngine"/> and <see cref="WorkerRuntime"/>.
    /// </summary>
    public DirectoryWatcher(Action<Msg> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
    }

    /// <summary>
    /// Reconciles the live set of watched directories to exactly <paramref name="paths"/> (empty
    /// entries ignored, duplicates collapsed): watchers for paths no longer present are disposed,
    /// and a new <see cref="System.IO.FileSystemWatcher"/> is started for each newly-added path. A
    /// path that fails to start watching (e.g. it has since been deleted) is silently skipped -
    /// there is simply nothing useful to watch there. Cheap to call on every render; paths already
    /// being watched are left untouched.
    /// </summary>
    public void SetWatchedPaths(IReadOnlyCollection<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!string.IsNullOrEmpty(path))
            {
                desired.Add(path);
            }
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var stale in _watched.Keys.Where(k => !desired.Contains(k)).ToList())
            {
                _watched[stale].Dispose();
                _watched.Remove(stale);
            }

            foreach (var path in desired)
            {
                if (!_watched.ContainsKey(path))
                {
                    TryStartWatching(path);
                }
            }
        }
    }

    /// <summary>Must be called with <see cref="_gate"/> held.</summary>
    private void TryStartWatching(string path)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            var entry = new WatchedDirectory(watcher, path, NotifyChanged);
            watcher.EnableRaisingEvents = true;
            _watched[path] = entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The path vanished, is inaccessible, or is not a valid directory to watch (e.g. a
            // drive that was just ejected) - nothing to watch, nothing to report.
        }
    }

    private void NotifyChanged(string path)
    {
        try
        {
            _post(new Msg.ExternalDirectoryChanged(path));
        }
        catch
        {
            // A bad post delegate must never crash the debounce timer thread.
        }
    }

    /// <summary>Stops and disposes every watched directory's <see cref="System.IO.FileSystemWatcher"/>.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _watched.Values)
            {
                entry.Dispose();
            }

            _watched.Clear();
        }
    }

    /// <summary>
    /// One <see cref="System.IO.FileSystemWatcher"/> plus its trailing per-path debounce timer:
    /// every Created/Deleted/Renamed/Changed event restarts a ~500ms timer rather than firing
    /// immediately, so a burst of events (e.g. Explorer copying many files in) collapses into one
    /// <see cref="Msg.ExternalDirectoryChanged"/> after the burst goes quiet.
    /// </summary>
    private sealed class WatchedDirectory : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly string _path;
        private readonly Action<string> _onDebouncedChange;
        private readonly object _timerGate = new();
        private Timer? _debounceTimer;
        private bool _disposed;

        public WatchedDirectory(FileSystemWatcher watcher, string path, Action<string> onDebouncedChange)
        {
            _watcher = watcher;
            _path = path;
            _onDebouncedChange = onDebouncedChange;
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Changed += OnEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
        }

        private void OnEvent(object sender, FileSystemEventArgs e) => Debounce();

        private void OnRenamed(object sender, RenamedEventArgs e) => Debounce();

        /// <summary>
        /// <see cref="System.IO.FileSystemWatcher"/> raises this when its internal buffer overflows
        /// or the watched directory itself becomes inaccessible (deleted, network drive dropped,
        /// ...). There is nothing to recover to automatically here - the reconcile in
        /// <see cref="SetWatchedPaths"/> is what eventually re-adds it if it comes back - so this
        /// just stops the flaky watcher from spinning.
        /// </summary>
        private void OnError(object sender, ErrorEventArgs e)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
            }
            catch
            {
                // Never throw from an event handler.
            }
        }

        private void Debounce()
        {
            try
            {
                lock (_timerGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _debounceTimer ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
                    _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
                }
            }
            catch
            {
                // Never throw from an event handler.
            }
        }

        private void Fire()
        {
            try
            {
                _onDebouncedChange(_path);
            }
            catch
            {
                // Never throw from a timer callback.
            }
        }

        public void Dispose()
        {
            lock (_timerGate)
            {
                _disposed = true;
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }

            try
            {
                _watcher.EnableRaisingEvents = false;
            }
            catch
            {
                // Already gone; nothing to do.
            }

            _watcher.Created -= OnEvent;
            _watcher.Deleted -= OnEvent;
            _watcher.Changed -= OnEvent;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
        }
    }
}
