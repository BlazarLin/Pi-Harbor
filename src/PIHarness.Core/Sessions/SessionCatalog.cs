// Created: 2026-09-06
// Purpose: Discover, group, sort, and watch local pi JSONL sessions.

using System.Collections.Concurrent;
using PIHarness.Core.Models;

namespace PIHarness.Core.Sessions;

public sealed class SessionCatalog : IDisposable
{
    private const int DebounceMilliseconds = 300;
    private readonly string _root;
    private readonly SessionNameStore? _names;
    private readonly object _timerLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Timer? _pollTimer;
    private bool _changeScheduled;
    private int _polling;
    private Dictionary<string, FileStamp> _lastFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedSession> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public SessionCatalog(string root, SessionNameStore? names = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = System.IO.Path.GetFullPath(root);
        _names = names;
    }

    public event EventHandler? Changed;

    public static Task<CatalogSnapshot> ScanAsync(string root, CancellationToken cancellationToken) => ScanAsync(root, null, cancellationToken);

    public async Task<CatalogSnapshot> ScanCurrentAsync(CancellationToken cancellationToken)
    {
        var snapshot = await ScanAsync(_root, _cache, cancellationToken).ConfigureAwait(false);
        if (_names is null) return snapshot;
        var projects = new List<ProjectGroup>();
        var warnings = snapshot.Warnings.ToList();
        foreach (var project in snapshot.Projects)
        {
            var sessions = new List<SessionSummary>();
            foreach (var session in project.Sessions)
            {
                var renamed = session;
                try
                {
                    if (await _names.ReadAsync(session.SessionPath, cancellationToken).ConfigureAwait(false) is { } name)
                        renamed = session with { Title = name };
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
                {
                    if (warnings.Count < 200) warnings.Add($"自定义名称读取失败：{error.Message}");
                }
                sessions.Add(renamed);
            }
            projects.Add(project with { Sessions = sessions });
        }
        return snapshot with { Projects = projects, Warnings = warnings };
    }

    private static async Task<CatalogSnapshot> ScanAsync(string root, ConcurrentDictionary<string, CachedSession>? cache, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root))
        {
            return new CatalogSnapshot([], 0, 0, [$"会话目录不存在：{root}"]);
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CatalogSnapshot([], 0, 0, [$"扫描会话目录失败：{exception.Message}"]);
        }

        var sessions = new ConcurrentBag<SessionSummary>();
        var warnings = new ConcurrentBag<string>();
        var nSkipped = 0;
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        };

        await Parallel.ForEachAsync(files, options, async (file, token) =>
        {
            var stamp = GetStamp(file);
            var result = cache is not null && cache.TryGetValue(file, out var cached) && cached.Stamp == stamp
                ? cached.Result
                : await SessionParser.ParseAsync(file, token).ConfigureAwait(false);
            if (cache is not null && stamp is not null && stamp == GetStamp(file) && result.IsValid)
                cache[file] = new CachedSession(stamp, result);
            foreach (var warning in result.Warnings)
            {
                warnings.Add(warning);
            }

            if (result.IsValid && result.Session is not null)
            {
                sessions.Add(result.Session);
            }
            else
            {
                Interlocked.Increment(ref nSkipped);
            }
        }).ConfigureAwait(false);

        if (cache is not null)
        {
            var existing = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in cache.Keys) if (!existing.Contains(path)) cache.TryRemove(path, out _);
        }

        var projects = sessions
            .GroupBy(session => session.Cwd, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedSessions = group
                    .OrderByDescending(session => session.LastActivityAt)
                    .ThenBy(session => session.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                var cwd = orderedSessions[0].Cwd;
                return new ProjectGroup(cwd, GetDisplayName(cwd), orderedSessions, orderedSessions[0].LastActivityAt);
            })
            .OrderByDescending(project => project.LastActivityAt)
            .ThenBy(project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new CatalogSnapshot(projects, sessions.Count, nSkipped, warnings.Take(200).ToArray());
    }

    public void StartWatching()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_timerLock)
        {
            if (_pollTimer is not null) return;
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
            TryStartWatcher();
            // Polling also covers a root created after startup, dropped events and watcher errors.
            _pollTimer = new Timer(OnPollElapsed, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    private void TryStartWatcher()
    {
        if (_disposed || _watcher is not null || !Directory.Exists(_root)) return;
        try
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_timerLock)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _pollTimer?.Dispose();
            _pollTimer = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private static string GetDisplayName(string cwd)
    {
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(cwd);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? cwd : name;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        if (args.FullPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
            args.FullPath.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(args.FullPath) || args.ChangeType is WatcherChangeTypes.Deleted or WatcherChangeTypes.Renamed)
            ScheduleChanged();
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        lock (_timerLock)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        ScheduleChanged();
    }

    private void ScheduleChanged()
    {
        lock (_timerLock)
        {
            if (!_disposed && !_changeScheduled)
            {
                _changeScheduled = true;
                _debounceTimer?.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        lock (_timerLock)
        {
            _changeScheduled = false;
            if (_disposed) return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPollElapsed(object? state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            lock (_timerLock)
            {
                if (_disposed) return;
                if (!Directory.Exists(_root)) { _watcher?.Dispose(); _watcher = null; }
                TryStartWatcher();
            }
            var files = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories))
                    if (GetStamp(file) is { } stamp) files[file] = stamp;
            }
            if (files.Count != _lastFiles.Count || files.Any(pair => !_lastFiles.TryGetValue(pair.Key, out var old) || old != pair.Value))
            {
                _lastFiles = files;
                ScheduleChanged();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ScheduleChanged(); }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private static FileStamp? GetStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var meta = new FileInfo(Path.ChangeExtension(path, ".meta.json"));
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, meta.Exists ? meta.LastWriteTimeUtc.Ticks : 0) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private sealed record FileStamp(long Length, long LastWriteTicks, long MetaWriteTicks);
    private sealed record CachedSession(FileStamp Stamp, SessionParseResult Result);
}
