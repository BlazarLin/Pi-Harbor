using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using PIHarness.Core.Models;

namespace PIHarness.App;

public partial class MainWindow
{
    private readonly TaskCompletionSource _windowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task VerifyNotificationAsync(string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        const string tag = "harbor-qa";
        try
        {
            _viewModel.Active.PrepareDocumentationDemo();
            ShowCompletionToast(_viewModel.Active, "settled", tag);
            await Task.Delay(1000);
            var setting = ToastNotificationManagerCompat.CreateToastNotifier().Setting;
            var delivered = ToastNotificationManagerCompat.History.GetHistory().Any(toast => toast.Tag == tag);
            await File.WriteAllTextAsync(reportPath, $"Windows notification setting: {setting}\nNotification present in Windows history: {delivered}\n");
            ToastNotificationManagerCompat.History.Remove(tag);
            Environment.ExitCode = delivered ? 0 : 1;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(reportPath, $"Notification verification failed: {error.Message}");
            Environment.ExitCode = 1;
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await _windowReady.Task;
            if (_shutdownStarted) return;
            var arguments = ToastArguments.Parse(args.Argument);
            if (!arguments.TryGetValue("session", out var path)) return;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            await _viewModel.OpenNotificationAsync(path);
        });
    }

    private async void OnChooseSessionFolderClick(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog { Title = "选择本次对话的工作目录", Multiselect = false };
        if (dialog.ShowDialog(this) == true) await _viewModel.CreateSessionAsync(dialog.FolderName);
    }

    private async void OnSetDefaultFolderClick(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog { Title = "设置新对话的默认工作目录", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try { await _viewModel.SetDefaultWorkingDirectoryAsync(dialog.FolderName); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _viewModel.ReportRecoverableError($"保存默认目录失败：{error.Message}"); }
    }

    private async void OnArchiveSessionClick(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: SessionSummary session }) return;
        if (_viewModel.IsSessionBusy(session))
        { _viewModel.ReportRecoverableError("此会话仍在处理中，请结束后再归档。"); return; }
        await _viewModel.SetArchivedAsync([session], !_viewModel.IsArchived(session));
    }

    private async void OnArchiveOlderClick(object sender, RoutedEventArgs args)
    {
        var candidates = _viewModel.GetArchiveCandidates(DateTimeOffset.Now);
        if (candidates.Length == 0)
        { _viewModel.ReportRecoverableError("没有可归档的旧会话：保留当前、未读、重点关注和处理中会话。"); return; }
        if (MessageBox.Show(this, $"归档 {candidates.Length} 个超过 7 天未活动的会话？\n\n保留当前、未读、重点关注和处理中会话。\n归档不删除文件，可在“已归档”中恢复，全局搜索仍可找到。",
                "整理旧会话", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        // Recheck eligibility after the modal dialog: a background task may have changed state.
        var eligible = _viewModel.GetArchiveCandidates(DateTimeOffset.Now).Select(session => session.SessionPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _viewModel.SetArchivedAsync(candidates.Where(session => eligible.Contains(session.SessionPath)), true);
    }

    private async void OnMarkUnreadClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: SessionSummary session }) await _viewModel.MarkSessionUnreadAsync(session, true);
    }

    private async void OnMarkReadClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: SessionSummary session }) await _viewModel.MarkSessionUnreadAsync(session, false);
    }
}
