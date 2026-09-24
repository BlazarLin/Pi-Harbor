using System.Collections.ObjectModel;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SessionUsageIndex _usageIndex = new();
    private readonly CancellationTokenSource _overviewCancellation = new();
    private Task _overviewTask = Task.CompletedTask;
    private bool _overviewPending;
    private bool _isOverviewVisible = true;
    private string _overviewStatus = "正在读取本机会话用量…";
    public bool IsOverviewVisible { get => _isOverviewVisible; set => SetProperty(ref _isOverviewVisible, value); }
    public string OverviewStatus { get => _overviewStatus; private set => SetProperty(ref _overviewStatus, value); }
    public string TodayTokens { get; private set; } = "0";
    public string MonthTokens { get; private set; } = "0";
    public string ActiveDays { get; private set; } = "0";
    public ObservableCollection<UsageDayViewModel> UsageDays { get; } = [];
    public ObservableCollection<SessionSummary> RecentSessions { get; } = [];

    public Task RefreshOverviewAsync()
    {
        if (_shuttingDown) return Task.CompletedTask;
        _overviewPending = true;
        if (_overviewTask.IsCompleted) _overviewTask = UpdateOverviewAsync();
        return _overviewTask;
    }

    private async Task UpdateOverviewAsync()
    {
        // Yield so the field is assigned even when the index is empty / already cached.
        await Task.Yield();
        try
        {
            while (_overviewPending && !_shuttingDown)
            {
                _overviewPending = false;
                var sessions = AllSessions.ToArray();
                var today = DateOnly.FromDateTime(DateTime.Now);
                var result = await Task.Run(() => _usageIndex.ReadAsync(sessions.Select(session => session.SessionPath), today, _overviewCancellation.Token));
                if (_shuttingDown) return;
                await RunOnUiAsync(() =>
                {
                    UsageDays.Clear();
                    var peak = Math.Max(1, result.Days.Max(day => day.Tokens));
                    foreach (var day in result.Days) UsageDays.Add(new UsageDayViewModel(day, peak));
                    TodayTokens = result.Days.Last().Tokens.ToString("N0");
                    MonthTokens = result.Days.Sum(day => day.Tokens).ToString("N0");
                    ActiveDays = result.Days.Count(day => day.Replies > 0).ToString();
                    OnPropertyChanged(nameof(TodayTokens)); OnPropertyChanged(nameof(MonthTokens)); OnPropertyChanged(nameof(ActiveDays));
                    OverviewStatus = $"最近 28 天 · 本机全部 {sessions.Length} 个会话（含终端与归档） · {result.UnreadableFiles} 个文件 / {result.InvalidRecords} 条记录未计入";
                    RecentSessions.Clear();
                    foreach (var session in sessions.OrderByDescending(session => session.LastActivityAt).Take(8)) RecentSessions.Add(session);
                });
            }
        }
        catch (OperationCanceledException) when (_overviewCancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { await RunOnUiAsync(() => OverviewStatus = "读取用量失败：" + error.Message); }
    }
}

public sealed record UsageDayViewModel(DailyUsage Usage, long Peak)
{
    public string DateText => Usage.Date.ToString("MM-dd");
    public string TokensText => Usage.Tokens.ToString("N0");
    public string Detail => $"{Usage.Date:yyyy-MM-dd} · {Usage.Tokens:N0} Tokens · {Usage.Replies} 条助手回复";
    public string Background => Usage.Tokens == 0 ? "#26252B" : Usage.Tokens < Peak / 4d ? "#393354" : Usage.Tokens < Peak / 2d ? "#514574" : Usage.Tokens < Peak * .75 ? "#69589C" : "#7D73EA";
}
