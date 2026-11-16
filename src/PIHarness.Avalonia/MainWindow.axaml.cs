// Created: 2026-09-12
// Purpose: Avalonia main window behavior: session tree/search interactions, conversation tabs,
//          in-conversation find, message actions, export, image pickers and capture support.

using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PIHarness.App.ViewModels;
using PIHarness.App.Presentation;
using PIHarness.Avalonia.Presentation;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.Avalonia;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly HashSet<ChatItemViewModel> _subscribedMessages = [];
    private readonly ChatScrollCoordinator _scrollCoordinator = new(1);
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _spinnerTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly string[] _spinnerFrames = ["⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷"];
    private int _spinnerFrame;
    private BulkObservableCollection<ChatItemViewModel>? _attachedMessages;
    private bool _shutdownComplete;
    private readonly List<ChatItemViewModel> _findMatches = [];
    private readonly List<ChatItemViewModel> _highlightedMessages = [];
    private int _findIndex;

    private static string? ReadCommandLineOption(string option)
    {
        var args = Environment.GetCommandLineArgs();
        for (var nIndex = 1; nIndex + 1 < args.Length; nIndex++)
        {
            if (string.Equals(args[nIndex], option, StringComparison.Ordinal))
            {
                return args[nIndex + 1];
            }
        }

        return null;
    }

    public MainWindow()
    {
        var sessionRoot = ReadCommandLineOption("--session-root");
        _viewModel = sessionRoot is null
            ? new MainViewModel()
            : new MainViewModel(sessionRoot);
        InitializeComponent();
        DataContext = _viewModel;
        MessageList.PointerWheelChanged += OnMessagePointerWheelChanged;
        Presentation.MessageTemplateSelector.WindowResources = this.Resources;
        AttachActiveMessages();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ConversationAttention += OnConversationAttention;
        Activated += (_, _) => { _viewModel.IsWindowActive = true; _viewModel.MarkActiveRead(); };
        Deactivated += (_, _) => _viewModel.IsWindowActive = false;
        _activityTimer.Tick += (_, _) => _viewModel.RefreshRelativeActivityTimes(DateTimeOffset.Now);
        _activityTimer.Start();
        _spinnerTimer.Tick += (_, _) =>
        {
            if (_viewModel.IsStreaming)
            {
                ProcessingSpinner.Text = _spinnerFrames[_spinnerFrame++ % _spinnerFrames.Length];
            }
        };
        _spinnerTimer.Start();
    }

    private void AttachActiveMessages()
    {
        if (_attachedMessages is not null)
        {
            _attachedMessages.CollectionChanged -= OnMessagesChanged;
        }

        _attachedMessages = _viewModel.Messages;
        _attachedMessages.CollectionChanged += OnMessagesChanged;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
        {
            OpenFindBar();
            args.Handled = true;
        }
    }

    private void OnMessagePointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        _scrollCoordinator.OnUserWheel((int)(args.Delta.Y * 120));
        if (args.Delta.Y > 0)
        {
            CancelPendingAutoScroll();
        }

        UpdateReturnToLatestVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.Active))
        {
            AttachActiveMessages();
            _scrollCoordinator.Reset();
            CancelPendingAutoScroll();
            UpdateReturnToLatestVisibility();
            CloseFindBar();
        }
    }

    private void OnConversationAttention(ConversationViewModel conversation, string kind)
    {
        // Shell notifications (toast/taskbar badge) remain in the WPF frontend for now.
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await _viewModel.InitializeAsync();
        var captureSessionPath = ReadCommandLineOption("--capture-session");
        if (!string.IsNullOrWhiteSpace(captureSessionPath))
        {
            var session = _viewModel.Projects
                .SelectMany(project => project.Sessions)
                .FirstOrDefault(item => string.Equals(
                    Path.GetFullPath(item.SessionPath),
                    Path.GetFullPath(captureSessionPath),
                    StringComparison.OrdinalIgnoreCase));
            if (session is not null)
            {
                await _viewModel.OpenSessionAsync(session.Session);
            }
            else if (File.Exists(captureSessionPath))
            {
                var parsed = await SessionParser.ParseAsync(captureSessionPath, CancellationToken.None);
                if (parsed.Session is not null)
                {
                    await _viewModel.OpenSessionAsync(parsed.Session);
                }
            }
        }

        var capturePath = ReadCommandLineOption("--capture-ui");
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(500);
                UpdateLayout();
                await CaptureWindowAsync(capturePath);
            });
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Environment.ExitCode = 0;
            Close();
        }
    }

    private async Task CaptureWindowAsync(string outputPath)
    {
        var width = (int)Math.Ceiling(Width);
        var height = (int)Math.Ceiling(Height);
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = new RenderTargetBitmap(new global::Avalonia.PixelSize(width, height));
        bitmap.Render(this);
        await using var stream = File.Create(outputPath);
        #pragma warning disable CS0618
        bitmap.Save(stream);
        #pragma warning restore CS0618
    }

    // ---- Message stream following ----

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _subscribedMessages.Clear();
            _scrollCoordinator.Reset();
            UpdateReturnToLatestVisibility();
        }

        if (args.NewItems is not null)
        {
            foreach (ChatItemViewModel item in args.NewItems)
            {
                item.PropertyChanged += OnMessagePropertyChanged;
                _subscribedMessages.Add(item);
                if (item.Kind == ChatItemKind.User)
                {
                    _scrollCoordinator.ReturnToLatest();
                }
            }
        }

        if (args.OldItems is not null)
        {
            foreach (ChatItemViewModel item in args.OldItems)
            {
                item.PropertyChanged -= OnMessagePropertyChanged;
                _subscribedMessages.Remove(item);
            }
        }

        ScrollToBottomIfNeeded();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ChatItemViewModel.Text))
        {
            ScrollToBottomIfNeeded();
        }
    }

    private void CancelPendingAutoScroll() => _pendingScroll = false;

    private bool _pendingScroll;

    private void ScrollToBottomIfNeeded()
    {
        if (!_scrollCoordinator.IsFollowingLatest || _viewModel.DisplayMessages.Count == 0)
        {
            return;
        }

        if (_pendingScroll)
        {
            return;
        }

        _pendingScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            MessageList.ScrollIntoView(_viewModel.DisplayMessages[^1]);
            var scrollViewer = FindScrollViewer(MessageList);
            scrollViewer?.ScrollToEnd();
            _pendingScroll = false;
        }, DispatcherPriority.Background);
    }

    private ScrollViewer? FindScrollViewer(Control control)
    {
        if (control is ScrollViewer viewer)
        {
            return viewer;
        }

        foreach (var child in global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(control))
        {
            if (child is Control childControl && FindScrollViewer(childControl) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private void UpdateReturnToLatestVisibility() =>
        ReturnToLatestButton.IsVisible = !_scrollCoordinator.IsFollowingLatest;

    private void OnReturnToLatestClick(object? sender, RoutedEventArgs args)
    {
        _scrollCoordinator.ReturnToLatest();
        UpdateReturnToLatestVisibility();
        ScrollToBottomIfNeeded();
    }

    // ---- Sidebar interactions ----

    private async void OnNewSessionClick(object? sender, RoutedEventArgs args)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 pi 项目文件夹",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
        {
            await _viewModel.CreateSessionAsync(folders[0].Path.LocalPath);
            PromptBox.Focus();
        }
    }

    private async void OnNewDefaultSessionClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            await _viewModel.CreateDefaultSessionAsync();
            PromptBox.Focus();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportRecoverableError($"创建默认工作目录失败：{error.Message}");
        }
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs args)
    {
        _viewModel.SearchText = "";
        GlobalSearchBox.Focus();
    }

    private void OnGlobalSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            _viewModel.SearchText = "";
            args.Handled = true;
        }
        else if (args.Key == Key.Enter)
        {
            args.Handled = true;
            _ = _viewModel.SearchNowAsync();
        }
    }

    private async void OnSessionSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is SessionItemViewModel sessionItem && _viewModel.CanSwitchSession)
        {
            await _viewModel.OpenSessionAsync(sessionItem.Session);
            PromptBox.Focus();
        }
    }

    private async void OnSearchResultSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is SessionSearchItemViewModel result && _viewModel.CanSwitchSession)
        {
            await _viewModel.OpenSessionAsync(result.Session);
        }
    }

    private async void OnRenameSessionClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: SessionSummary session })
        {
            return;
        }

        var dialog = new RenameDialogWindow(session.Title);
        if (await dialog.ShowDialog<bool?>(this) == true)
        {
            try
            {
                await _viewModel.RenameSessionAsync(session, dialog.NewName);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _viewModel.ReportRecoverableError($"重命名失败：{error.Message}");
            }
        }
    }

    private async void OnToggleStarClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: SessionSummary session })
        {
            try
            {
                await _viewModel.ToggleStarAsync(session);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _viewModel.ReportRecoverableError($"保存重点关注标记失败：{error.Message}");
            }
        }
    }

    private async void OnArchiveSessionClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: SessionSummary session })
        {
            return;
        }

        await _viewModel.SetArchivedAsync([session], !_viewModel.IsArchived(session));
    }

    // ---- Conversation tabs ----

    private void OnConversationTabClick(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: ConversationViewModel conversation })
        {
            _viewModel.ActivateConversation(conversation);
            PromptBox.Focus();
        }
    }

    private async void OnCloseConversationClick(object? sender, RoutedEventArgs args)
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

    private async void OnCopyMessageClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: ChatItemViewModel item })
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(item.Text));
                await clipboard.SetDataAsync(transfer);
            }

            _viewModel.ReportRecoverableError("已复制该条消息全文。");
        }
    }

    private void OnQuoteMessageClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: ChatItemViewModel item })
        {
            var quoted = string.Join("\n", item.Text.Split('\n').Select(line => "> " + line)) + "\n\n";
            _viewModel.InputText = string.IsNullOrWhiteSpace(_viewModel.InputText)
                ? quoted
                : quoted + _viewModel.InputText;
            PromptBox.Focus();
            PromptBox.CaretIndex = PromptBox.Text?.Length ?? 0;
        }
    }

    private void OnRevokeQueuedClick(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: ChatItemViewModel item })
        {
            _viewModel.RevokeQueuedMessage(item);
        }
    }

    private async void OnPreviewImageClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ImageAttachmentViewModel image })
        {
            return;
        }

        try
        {
            var preview = AvaloniaImageConversion.OpenPreview(image);
            var window = new Window
            {
                Title = image.Detail,
                Width = 900,
                Height = 650,
                Background = global::Avalonia.Media.Brushes.Black,
                Content = new Image { Source = preview, Stretch = Stretch.Uniform, Margin = new Thickness(12) },
            };
            window.KeyDown += (_, key) => { if (key.Key == Key.Escape) window.Close(); };
            await window.ShowDialog(this);
        }
        catch (Exception error) when (error is ArgumentException or IOException)
        {
            _viewModel.ReportRecoverableError($"打开图片预览失败：{error.Message}");
        }
    }

    private void OnRemoveImageClick(object? sender, RoutedEventArgs args)
    {
        if (_viewModel.CanEditComposer && sender is Button { Tag: ImageAttachmentViewModel image })
        {
            _viewModel.Attachments.Remove(image);
        }

        PromptBox.Focus();
    }

    private async void OnAttachImageClick(object? sender, RoutedEventArgs args)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "添加图片",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("图片")
            {
                Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tif", "*.tiff"],
            }],
        });

        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                using var bitmap = new Bitmap(memory);
                using var scaled = ScaleBitmap(bitmap, 1600);
                using var encoded = new MemoryStream();
                #pragma warning disable CS0618
                scaled.Save(encoded);
                #pragma warning restore CS0618
                _viewModel.AddAttachment(ImageAttachmentViewModel.FromPngBytes(encoded.ToArray(), file.Name));
            }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException)
            {
                _viewModel.ReportRecoverableError($"无法添加 {file.Name}：{error.Message}");
            }
        }

        PromptBox.Focus();
    }

    private static Bitmap ScaleBitmap(Bitmap bitmap, int maxWidth)
    {
        if (bitmap.PixelSize.Width <= maxWidth)
        {
            return bitmap;
        }

        var height = (int)((double)bitmap.PixelSize.Height / bitmap.PixelSize.Width * maxWidth);
        var scaled = bitmap.CreateScaledBitmap(new global::Avalonia.PixelSize(maxWidth, Math.Max(1, height)));
        return scaled;
    }

    // ---- Composer ----

    private async void OnPromptBoxKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            args.Handled = true;
            await _viewModel.SendAsync();
        }
        else if (args.Key == Key.V && args.KeyModifiers == KeyModifiers.Control)
        {
            args.Handled = await TryPasteImageFromClipboardAsync();
        }
    }

    private async Task<bool> TryPasteImageFromClipboardAsync()
    {
        if (!_viewModel.CanEditComposer)
        {
            return false;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return false;
            }

            using var data = await clipboard.TryGetDataAsync();
            if (data is null)
            {
                return false;
            }

            // Text wins so regular copy/paste keeps its default behavior.
            if (data.Contains(DataFormat.Text) && await data.TryGetValueAsync(DataFormat.Text) is { } text && text.Length > 0)
            {
                return false;
            }

            if (data.Contains(DataFormat.File))
            {
                var count = 0;
                foreach (var entry in data.Items)
                {
                    if (!entry.Formats.Contains(DataFormat.File))
                    {
                        continue;
                    }

                    if (await entry.TryGetValueAsync(DataFormat.File) is not IStorageItem item ||
                        item.TryGetLocalPath() is not { } path ||
                        Path.GetExtension(path) is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff"))
                    {
                        continue;
                    }

                    count++;
                    await using var stream = File.OpenRead(path);
                    using var bitmap = new Bitmap(stream);
                    using var scaled = ScaleBitmap(bitmap, 1600);
                    using var encoded = new MemoryStream();
                    #pragma warning disable CS0618
                    scaled.Save(encoded);
                    #pragma warning restore CS0618
                    _viewModel.AddAttachment(ImageAttachmentViewModel.FromPngBytes(encoded.ToArray(), Path.GetFileName(path)));
                }

                return count > 0;
            }

            // Screenshots arrive as a platform bitmap or raw PNG bytes.
            if (data.Contains(DataFormat.Bitmap) && await data.TryGetValueAsync(DataFormat.Bitmap) is { } clipBitmap)
            {
                using var encoded = new MemoryStream();
                #pragma warning disable CS0618
                clipBitmap.Save(encoded);
                #pragma warning restore CS0618
                _viewModel.AddAttachment(ImageAttachmentViewModel.FromPngBytes(encoded.ToArray(), $"粘贴图片 {_viewModel.Attachments.Count + 1}.png"));
                return true;
            }

            var pngFormat = DataFormat.CreateBytesPlatformFormat("PNG");
            if (data.Contains(pngFormat) && await data.TryGetValueAsync(pngFormat) is { } pngBytes)
            {
                _viewModel.AddAttachment(ImageAttachmentViewModel.FromPngBytes(pngBytes, $"粘贴图片 {_viewModel.Attachments.Count + 1}.png"));
                return true;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            _viewModel.ReportRecoverableError($"粘贴图片失败：{error.Message}");
            return true;
        }

        return false;
    }

    private async void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count == 1 && args.AddedItems[0] is ModelOptionViewModel model)
        {
            await _viewModel.SelectModelAsync(model);
        }
    }

    // ---- Export ----

    private async void OnExportConversationClick(object? sender, RoutedEventArgs args)
    {
        var markdown = _viewModel.ExportActiveConversationMarkdown();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            _viewModel.ReportRecoverableError("当前会话还没有可导出的内容。");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出会话为 Markdown",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(markdown);
            _viewModel.ReportRecoverableError($"已导出到 {file.Name}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportRecoverableError($"导出失败：{error.Message}");
        }
    }

    // ---- In-conversation find ----

    private void OnFindBarOpenClick(object? sender, RoutedEventArgs args) => OpenFindBar();

    private void OpenFindBar()
    {
        FindPanel.IsVisible = true;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFindBar()
    {
        FindPanel.IsVisible = false;
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

    private void OnFindTextChanged(object? sender, TextChangedEventArgs args) => RunFind();

    private void RunFind()
    {
        var query = FindBox.Text?.Trim() ?? string.Empty;
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

    private void OnFindNextClick(object? sender, RoutedEventArgs args) => MoveFind(1);

    private void OnFindPreviousClick(object? sender, RoutedEventArgs args) => MoveFind(-1);

    private void OnFindBarCloseClick(object? sender, RoutedEventArgs args) => CloseFindBar();

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            MoveFind(args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            CloseFindBar();
            args.Handled = true;
        }
        else if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
        {
            OpenFindBar();
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
        _scrollCoordinator.OnUserWheel(120);
        MessageList.ScrollIntoView(match);
    }

    protected override void OnClosed(EventArgs args)
    {
        if (!_shutdownComplete && !_viewModel.IsDisposed)
        {
            _ = ShutdownQuietlyAsync();
        }

        base.OnClosed(args);
    }

    private async Task ShutdownQuietlyAsync()
    {
        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
