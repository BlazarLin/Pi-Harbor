// Created: 2026-09-06
// Purpose: Discover, group, sort, and watch local pi JSONL sessions.

using System.Collections.Concurrent;
using PIHarness.Core.Models;

namespace PIHarness.Core.Sessions;

public sealed class SessionCatalog : IDisposable
{
    private const int DebounceMilliseconds = 300;
    private readonly string _root;
    private readonly object _timerLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private bool _disposed;

    public SessionCatalog(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = System.IO.Path.GetFullPath(root);
    }

    public event EventHandler? Changed;

    public static async Task<CatalogSnapshot> ScanAsync(string root, CancellationToken cancellationToken)
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
            var result = await SessionParser.ParseAsync(file, token).ConfigureAwait(false);
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
        if (_watcher is not null || !Directory.Exists(_root))
        {
            return;
        }

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(_root, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.Error += OnWatcherError;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        lock (_timerLock)
        {
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
        ScheduleChanged();
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        ScheduleChanged();
    }

    private void ScheduleChanged()
    {
        lock (_timerLock)
        {
            if (!_disposed)
            {
                _debounceTimer?.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
