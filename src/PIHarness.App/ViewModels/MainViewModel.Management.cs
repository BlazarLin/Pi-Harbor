using System.IO;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SessionManagementStore _managementStore;
    private Task _managementLoaded = Task.CompletedTask;
    private Task _managementSaved = Task.CompletedTask;
    private readonly HashSet<string> _archived = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unread = new(StringComparer.OrdinalIgnoreCase);
    private CatalogSnapshot _snapshot = new([], 0, 0, []);
    private int _sessionFilter;
    private string _defaultWorkingDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pi Harbor", "Workspace");

    public bool IsWindowActive { get; set; } = true;
    public string DefaultWorkingDirectory => _defaultWorkingDirectory;
    public int TotalSessionCount => _snapshot.SessionCount;
    public int ActiveSessionCount => AllSessions.Count(session => session.LastActivityAt >= DateTimeOffset.Now.AddDays(-7));
    public int ArchivedSessionCount => AllSessions.Count(session => _archived.Contains(session.SessionPath));
    public string SessionStatistics => $"7 天内活跃 {ActiveSessionCount} · 总计 {TotalSessionCount} · 已归档 {ArchivedSessionCount}";
    private IEnumerable<SessionSummary> AllSessions => _snapshot.Projects.SelectMany(project => project.Sessions);
    public int SessionFilter
    {
        get => _sessionFilter;
        set { if (SetProperty(ref _sessionFilter, value)) RebuildProjects(); }
    }

    private async Task LoadManagementAsync()
    {
        try
        {
            var state = await _managementStore.LoadAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _archived.UnionWith(state.Archived.Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)));
                _unread.UnionWith(state.Unread.Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)));
                if (!string.IsNullOrWhiteSpace(state.DefaultWorkingDirectory) && Path.IsPathFullyQualified(state.DefaultWorkingDirectory))
                    _defaultWorkingDirectory = state.DefaultWorkingDirectory;
                OnPropertyChanged(nameof(DefaultWorkingDirectory));
                RebuildProjects();
            }).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await RunOnUiAsync(() => ReportRecoverableError($"读取会话管理设置失败：{error.Message}")).ConfigureAwait(false);
        }
    }

    // Capture on the UI thread, then serialize writes so an earlier save cannot overwrite a later one.
    private Task SaveManagementAsync()
    {
        var state = new SessionManagementState(_archived.ToArray(), _unread.ToArray(), _defaultWorkingDirectory);
        return _managementSaved = SaveAfterAsync(_managementSaved, state);
    }

    private async Task SaveAfterAsync(Task previous, SessionManagementState state)
    {
        await previous.ConfigureAwait(false);
        try { await _managementStore.SaveAsync(state).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await RunOnUiAsync(() => ReportRecoverableError($"保存会话管理设置失败：{error.Message}")).ConfigureAwait(false);
        }
    }

    public async Task SetDefaultWorkingDirectoryAsync(string directory)
    {
        await _managementLoaded;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("默认工作目录不存在。");
        _defaultWorkingDirectory = Path.GetFullPath(directory);
        OnPropertyChanged(nameof(DefaultWorkingDirectory));
        await SaveManagementAsync();
    }

    public async Task CreateDefaultSessionAsync()
    {
        await _managementLoaded;
        Directory.CreateDirectory(DefaultWorkingDirectory);
        await CreateSessionAsync(DefaultWorkingDirectory);
    }

    public bool IsArchived(SessionSummary session) => _archived.Contains(session.SessionPath);
    public bool IsSessionUnread(SessionSummary session) => _unread.Contains(session.SessionPath);
    public bool IsSessionBusy(SessionSummary session) => _conversations.TryGetValue(Path.GetFullPath(session.SessionPath), out var conversation) && conversation.IsBusy;

    public async Task SetArchivedAsync(IEnumerable<SessionSummary> sessions, bool archived)
    {
        await _managementLoaded;
        foreach (var session in sessions.ToArray())
        {
            if (archived && IsSessionBusy(session)) continue;
            if (archived) _archived.Add(session.SessionPath); else _archived.Remove(session.SessionPath);
        }
        RebuildProjects();
        await SaveManagementAsync();
    }

    public SessionSummary[] GetArchiveCandidates(DateTimeOffset now) => AllSessions
        .Where(session => session.LastActivityAt < now.AddDays(-7) && !IsArchived(session) &&
            !_starredSessions.Contains(session.SessionPath) && !IsSessionUnread(session) && !IsSessionBusy(session) &&
            !AreSameSessionPath(session.SessionPath, Active.Key)).ToArray();

    public async Task MarkSessionUnreadAsync(SessionSummary session, bool unread)
    {
        await _managementLoaded;
        SetUnread(session.SessionPath, unread);
        await _managementSaved;
    }

    private void SetUnread(string path, bool unread)
    {
        var changed = unread ? _unread.Add(path) : _unread.Remove(path);
        if (_conversations.TryGetValue(path, out var conversation)) conversation.HasUnread = unread;
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(BackgroundActivityText));
        UpdateTreeState();
        if (changed) _ = SaveManagementAsync();
    }

    public void MarkActiveRead()
    {
        if (IsWindowActive && Active.SessionPath is { } path) SetUnread(path, false);
    }

    public async Task OpenNotificationAsync(string sessionPath)
    {
        await _managementLoaded;
        var session = AllSessions.FirstOrDefault(session => AreSameSessionPath(session.SessionPath, sessionPath));
        if (session is null)
        {
            await RefreshCatalogAsync();
            session = AllSessions.FirstOrDefault(session => AreSameSessionPath(session.SessionPath, sessionPath));
        }
        if (session is null) { ReportRecoverableError("通知对应的会话已不存在。"); return; }
        if (IsArchived(session)) SessionFilter = 2;
        await OpenSessionAsync(session);
    }

    private void RebuildProjects()
    {
        var expansion = Projects.ToDictionary(project => project.Cwd, project => project.IsExpanded, StringComparer.OrdinalIgnoreCase);
        var latestPath = AllSessions.OrderByDescending(session => session.LastActivityAt).FirstOrDefault()?.SessionPath;
        Projects.Clear();
        foreach (var project in _snapshot.Projects)
        {
            var sessions = project.Sessions.Where(session => SessionFilter == 2 || IsArchived(session) == (SessionFilter == 1)).ToArray();
            if (sessions.Length == 0) continue;
            var item = new ProjectGroupViewModel(project with { Sessions = sessions }) { IsExpanded = expansion.GetValueOrDefault(project.Cwd, true) };
            foreach (var session in item.Sessions) session.IsLatest = AreSameSessionPath(session.SessionPath, latestPath);
            Projects.Add(item);
        }
        OnPropertyChanged(nameof(TotalSessionCount));
        OnPropertyChanged(nameof(ActiveSessionCount));
        OnPropertyChanged(nameof(ArchivedSessionCount));
        OnPropertyChanged(nameof(SessionStatistics));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(BackgroundActivityText));
        UpdateTreeState();
        ScheduleSearch(catalogChanged: true);
    }
}
