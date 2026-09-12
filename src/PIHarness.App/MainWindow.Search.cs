using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.App;

public partial class MainWindow
{
    private async void OnWorkspacePreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            GlobalSearchBox.Focus(); GlobalSearchBox.SelectAll(); args.Handled = true;
        }
        else if (args.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            var session = SessionTree.IsKeyboardFocusWithin ? (SessionTree.SelectedItem as SessionItemViewModel)?.Session
                : GlobalSearchResults.IsKeyboardFocusWithin ? (GlobalSearchResults.SelectedItem as SessionSearchItemViewModel)?.Session : null;
            if (session is not null) { args.Handled = true; await RenameSessionAsync(session); }
        }
    }
    private async void OnGlobalSearchKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { _viewModel.SearchText = ""; args.Handled = true; }
        else if (args.Key == Key.Enter) { args.Handled = true; await _viewModel.SearchNowAsync(); }
        else if (args.Key == Key.Down && GlobalSearchResults.Items.Count > 0)
        {
            args.Handled = true;
            GlobalSearchResults.Focus();
            (GlobalSearchResults.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        }
    }
    private void OnClearSearchClick(object sender, RoutedEventArgs args) { _viewModel.SearchText = ""; GlobalSearchBox.Focus(); }
    private SessionSummary? _pendingSearchJump;
    private string _pendingSearchQuery = "";
    private async void OnSearchResultSelected(object sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is SessionSearchItemViewModel result && _viewModel.CanSwitchSession)
        {
            _pendingSearchJump = result.Session;
            _pendingSearchQuery = _viewModel.SearchText.Trim();
            await _viewModel.OpenSessionAsync(result.Session);
            LocateSearchResult();
        }
    }

    /// <summary>After opening a search hit, scroll to the first matching message and flash-highlight it.</summary>
    private void LocateSearchResult()
    {
        var query = _pendingSearchQuery;
        _pendingSearchJump = null;
        _pendingSearchQuery = "";
        if (query.Length == 0 || _viewModel.Messages.Count == 0) return;
        var match = _viewModel.Messages.FirstOrDefault(item =>
            (item.Kind is ChatItemKind.User or ChatItemKind.Assistant or ChatItemKind.System or ChatItemKind.Error) &&
            item.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        // Keep reading intent so auto-follow does not pull the view back to the bottom.
        _scrollCoordinator.OnUserWheel(120);
        CancelPendingAutoScroll();
        UpdateReturnToLatestVisibility();
        MessageList.ScrollIntoView(match);
        match.IsHighlighted = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => MessageList.UpdateLayout());
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            match.IsHighlighted = false;
            timer.Stop();
        };
        timer.Start();
    }
    private async void OnRenameSessionClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: SessionSummary session }) await RenameSessionAsync(session);
    }
    private async void OnToggleStarClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: SessionSummary session })
        {
            try { await _viewModel.ToggleStarAsync(session); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _viewModel.ReportRecoverableError($"保存重点关注标记失败：{error.Message}");
            }
        }
    }
    private async Task RenameSessionAsync(SessionSummary session)
    {
        var dialog = new RenameSessionDialog(this, session.Title);
        if (dialog.ShowDialog() != true) return;
        try { await _viewModel.RenameSessionAsync(session, dialog.NewName); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _viewModel.ReportRecoverableError($"重命名失败：{error.Message}");
        }
    }
}
