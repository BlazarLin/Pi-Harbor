// Created: 2026-09-08
// Purpose: Conversation-surface interactions: tab strip clicks, in-conversation find (Ctrl+F),
//          message copy/quote actions and Markdown export.

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.App;

public partial class MainWindow
{
    private readonly List<ChatItemViewModel> _findMatches = [];
    private readonly List<ChatItemViewModel> _highlightedMessages = [];
    private int _findIndex = -1;

    // ---- Conversation tab strip ----

    private void OnConversationTabClick(object sender, MouseButtonEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ConversationViewModel conversation })
        {
            _viewModel.ActivateConversation(conversation);
            PromptBox.Focus();
        }
    }

    private async void OnCloseConversationClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ConversationViewModel conversation })
        {
            return;
        }

        try
        {
            await _viewModel.CloseConversationAsync(conversation);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _viewModel.ReportRecoverableError($"关闭会话失败：{error.Message}");
        }
    }

    // ---- Message actions ----

    private void OnCopyMessageClick(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: ChatItemViewModel item })
        {
            return;
        }

        try
        {
            Clipboard.SetText(item.Text);
            _viewModel.ReportRecoverableError("已复制该条消息全文。");
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _viewModel.ReportRecoverableError($"复制失败：{error.Message}");
        }
    }

    private void OnQuoteMessageClick(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: ChatItemViewModel item })
        {
            return;
        }

        var quoted = string.Join("\n", item.Text.Split('\n').Select(line => "> " + line)) + "\n\n";
        _viewModel.InputText = string.IsNullOrWhiteSpace(_viewModel.InputText)
            ? quoted
            : quoted + _viewModel.InputText;
        PromptBox.Focus();
        PromptBox.CaretIndex = PromptBox.Text.Length;
    }

    private void OnRevokeQueuedClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: ChatItemViewModel item })
        {
            _viewModel.RevokeQueuedMessage(item);
        }
    }

    // ---- Markdown export ----

    private void OnExportConversationClick(object sender, RoutedEventArgs args)
    {
        var markdown = _viewModel.ExportActiveConversationMarkdown();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            _viewModel.ReportRecoverableError("当前会话还没有可导出的内容。");
            return;
        }

        var fileName = string.Concat(_viewModel.CurrentTitle
            .Where(character => !Path.GetInvalidFileNameChars().Contains(character)));
        if (fileName.Length > 60) fileName = fileName[..60];
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "会话";
        var dialog = new SaveFileDialog
        {
            Title = "导出会话为 Markdown",
            FileName = fileName + ".md",
            Filter = "Markdown|*.md|全部文件|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, markdown, new UTF8Encoding(false));
            _viewModel.ReportRecoverableError($"已导出到 {dialog.FileName}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportRecoverableError($"导出失败：{error.Message}");
        }
    }

    // ---- Settings ----

    private async void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        var dialog = new SettingsWindow(
            _viewModel.CloseToTrayEnabled,
            _viewModel.ToastNotificationsEnabled,
            _viewModel.FlashWindowEnabled,
            _viewModel.GlobalHotkeyEnabled,
            _viewModel.IdleRecycleMinutes)
        { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _viewModel.UpdateShellPreferencesAsync(
                    dialog.CloseToTray, dialog.ToastNotifications, dialog.FlashWindow,
                    dialog.GlobalHotkey, dialog.IdleRecycleMinutes);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _viewModel.ReportRecoverableError($"保存设置失败：{error.Message}");
            }
        }
    }

    // ---- In-conversation find (Ctrl+F) ----

    private void OnFindBarOpenClick(object sender, RoutedEventArgs args) => OpenFindBar();

    private void OpenFindBar()
    {
        FindPanel.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFindBar()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        ClearFindHighlights();
        _findMatches.Clear();
        _findIndex = -1;
        FindCountText.Text = "";
    }

    private void ClearFindHighlights()
    {
        foreach (var message in _highlightedMessages)
        {
            message.IsHighlighted = false;
        }

        _highlightedMessages.Clear();
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs args) => RunFind();

    private void RunFind()
    {
        var query = FindBox.Text.Trim();
        ClearFindHighlights();
        _findMatches.Clear();
        _findIndex = -1;
        if (query.Length > 0)
        {
            foreach (var message in _viewModel.DisplayMessages)
            {
                if (message.Kind is ChatItemKind.User or ChatItemKind.Assistant or ChatItemKind.System
                        or ChatItemKind.Error or ChatItemKind.Queued or ChatItemKind.Tool &&
                    message.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    _findMatches.Add(message);
                }
            }
        }

        if (_findMatches.Count > 0)
        {
            _findIndex = 0;
            ShowCurrentFind();
        }
        else
        {
            FindCountText.Text = query.Length == 0 ? "" : "无结果";
        }
    }

    private void OnFindNextClick(object sender, RoutedEventArgs args) => MoveFind(1);

    private void OnFindPreviousClick(object sender, RoutedEventArgs args) => MoveFind(-1);

    private void OnFindBarCloseClick(object sender, RoutedEventArgs args) => CloseFindBar();

    private void OnFindBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            MoveFind(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            CloseFindBar();
            args.Handled = true;
        }
    }

    private void MoveFind(int delta)
    {
        if (_findMatches.Count == 0)
        {
            return;
        }

        _findIndex = (_findIndex + delta + _findMatches.Count) % _findMatches.Count;
        ShowCurrentFind();
    }

    private void ShowCurrentFind()
    {
        if (_findIndex < 0 || _findIndex >= _findMatches.Count)
        {
            return;
        }

        ClearFindHighlights();
        var match = _findMatches[_findIndex];
        match.IsHighlighted = true;
        _highlightedMessages.Add(match);
        FindCountText.Text = $"{_findIndex + 1}/{_findMatches.Count}";
        // Keep reading intent so auto-follow does not pull the view back to the bottom.
        _scrollCoordinator.OnUserWheel(120);
        CancelPendingAutoScroll();
        UpdateReturnToLatestVisibility();
        MessageList.ScrollIntoView(match);
    }
}
