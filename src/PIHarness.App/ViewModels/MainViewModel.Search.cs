using System.Collections.ObjectModel;
using System.IO;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SessionNameStore _nameStore;
    private CancellationTokenSource? _searchCts;
    private Task _searchTask = Task.CompletedTask;
    private int _searchGeneration;
    private bool _searchRefreshPending;
    private string _searchText = "";
    private string _searchStatus = "";
    private bool _isSearching;
    public ObservableCollection<SessionSearchItemViewModel> SearchResults { get; } = [];
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            OnPropertyChanged(nameof(IsSearchActive));
            ScheduleSearch();
        }
    }
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);
    public string SearchStatus { get => _searchStatus; private set => SetProperty(ref _searchStatus, value); }
    public bool IsSearching { get => _isSearching; private set => SetProperty(ref _isSearching, value); }
    public Task SearchNowAsync() { ScheduleSearch(delay: 0); return _searchTask; }

    private void ScheduleSearch(bool catalogChanged = false, int delay = 350)
    {
        if (_shuttingDown) return;
        if (catalogChanged && IsSearching) { _searchRefreshPending = true; return; }
        _searchCts?.Cancel();
        _searchCts = null;
        var generation = ++_searchGeneration;
        _searchRefreshPending = false;
        if (!catalogChanged) SearchResults.Clear();
        if (!IsSearchActive) { IsSearching = false; SearchStatus = ""; return; }
        IsSearching = true;
        SearchStatus = "正在搜索全部会话…";
        var cts = _searchCts = new CancellationTokenSource();
        var sessions = Projects.SelectMany(project => project.Sessions).Select(item => item.Session).ToArray();
        var query = SearchText.Trim();
        _searchTask = RunSearchAsync(sessions, query, generation, delay, cts);
    }

    private async Task RunSearchAsync(SessionSummary[] sessions, string query, int generation, int delay, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token).ConfigureAwait(false);
            var result = await Task.Run(() => SessionSearch.FindAsync(sessions, query, cts.Token), cts.Token).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                if (_shuttingDown || generation != _searchGeneration) return;
                SearchResults.Clear();
                foreach (var match in result.Matches) SearchResults.Add(new SessionSearchItemViewModel(match));
                SearchStatus = result.Truncated ? $"显示前 {SessionSearch.MaxResults} 个会话，请缩小关键词范围" : result.Matches.Count == 0 ? "没有找到匹配会话" : $"找到 {result.Matches.Count} 个会话";
                if (result.UnreadableFiles > 0 || result.InvalidRecords > 0)
                    SearchStatus += $" · {result.UnreadableFiles} 个文件 / {result.InvalidRecords} 条记录无法读取";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await RunOnUiAsync(() => { if (!_shuttingDown && generation == _searchGeneration) SearchStatus = $"搜索失败：{error.Message}"; }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                if (generation != _searchGeneration) return;
                _searchCts = null;
                IsSearching = false;
                if (_searchRefreshPending && !_shuttingDown) ScheduleSearch(catalogChanged: true);
            }).ConfigureAwait(false);
            cts.Dispose();
        }
    }

    public async Task RenameSessionAsync(SessionSummary session, string? name)
    {
        ThrowIfDisposed();
        if (!File.Exists(session.SessionPath)) throw new FileNotFoundException("会话文件已不存在，请刷新列表。");
        await _nameStore.SaveAsync(session.SessionPath, name).ConfigureAwait(false);
        var key = Path.GetFullPath(session.SessionPath);
        if (name is not null)
        {
            _customRenames.Add(key);
            if (_conversations.TryGetValue(key, out var renamed)) renamed.ApplySessionRename(name);
        }
        else
        {
            _customRenames.Remove(key);
        }

        await RefreshCatalogAsync().ConfigureAwait(false);
        if (name is null && _conversations.TryGetValue(key, out var restored))
        {
            // Restore the parsed title from the refreshed catalog.
            var parsedTitle = Projects.SelectMany(project => project.Sessions)
                .FirstOrDefault(item => AreSameSessionPath(item.SessionPath, key))?.Title;
            if (parsedTitle is not null) restored.ApplySessionRename(parsedTitle);
        }
    }
}

public sealed class SessionSearchItemViewModel(SessionSearchMatch match)
{
    public SessionSummary Session => match.Session;
    public string Title => Session.Title;
    public string Preview => match.Preview;
    public string Details => $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(Session.Cwd))} · {match.Source}";
    public string ToolTip => $"{Session.Title}\n{Session.Cwd}\n{match.Source}：{match.Preview}";
}
