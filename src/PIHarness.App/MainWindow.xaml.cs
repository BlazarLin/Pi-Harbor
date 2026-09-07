// Created: 2026-09-06
// Purpose: Handle Windows-only shell interactions and preserve chat scroll intent.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PIHarness.App.ViewModels;
using PIHarness.App.Presentation;
using PIHarness.Core.Sessions;
using PIHarness.Core.Rpc;

namespace PIHarness.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly HashSet<ChatItemViewModel> _subscribedMessages = [];
    private readonly ChatScrollCoordinator _scrollCoordinator = new(1);
    private DispatcherOperation? _pendingScrollOperation;
    private bool _shutdownComplete;
    private bool _shutdownStarted;
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    public MainWindow()
    {
        _viewModel = ReadCommandLineOption("--qa-composer-capture-dir") is null
            ? new MainViewModel()
            : new MainViewModel(rpcClientFactory: () => new PiRpcClient(options =>
                PiProcessLocator.CreateStartInfo(PiProcessLocator.Find().PiCommandPath!, options with { NoSession = true, Offline = true })));
        InitializeComponent();
        DataContext = _viewModel;
        InitializeComposer();
        _activityTimer.Tick += (_, _) => _viewModel.RefreshRelativeActivityTimes(DateTimeOffset.Now);
        _activityTimer.Start();
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await _viewModel.InitializeAsync();
        var publicCaptureDirectory = ReadCommandLineOption("--capture-public-docs");
        if (!string.IsNullOrWhiteSpace(publicCaptureDirectory))
        {
            var passed = await CapturePublicDocumentationAsync(publicCaptureDirectory);
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Environment.ExitCode = passed ? 0 : 1;
            Close();
            return;
        }
        var composerQaDirectory = ReadCommandLineOption("--qa-composer-capture-dir");
        if (!string.IsNullOrWhiteSpace(composerQaDirectory))
        {
            var passed = await RunComposerQaAsync(composerQaDirectory);
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Environment.ExitCode = passed ? 0 : 1;
            Close();
            return;
        }
        var styleQaDirectory = ReadCommandLineOption("--qa-style-capture-dir");
        if (!string.IsNullOrWhiteSpace(styleQaDirectory))
        {
            await CaptureNoSessionStyleAsync(styleQaDirectory);
        }

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
        var scrollQaDirectory = ReadCommandLineOption("--qa-scroll-capture-dir");
        if (!string.IsNullOrWhiteSpace(styleQaDirectory))
        {
            var bPassed = await RunStyleQaAsync(styleQaDirectory);
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Environment.ExitCode = bPassed ? 0 : 1;
            Close();
            return;
        }

        if (!string.IsNullOrWhiteSpace(scrollQaDirectory))
        {
            var bPassed = await RunScrollQaAsync(scrollQaDirectory);
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Environment.ExitCode = bPassed ? 0 : 1;
            Close();
            return;
        }

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(350);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateLayout();
            await Task.Delay(150);
            CaptureWindow(capturePath);
            await _viewModel.ShutdownAsync();
            _shutdownComplete = true;
            Close();
        }
    }

    private async void OnNewSessionClick(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 pi 项目文件夹",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.CreateSessionAsync(dialog.FolderName);
            PromptBox.Focus();
        }
    }

    private async void OnSessionSelected(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (args.NewValue is SessionItemViewModel sessionItem && _viewModel.CanSwitchSession)
        {
            PromptBox.Focus();
            await _viewModel.OpenSessionAsync(sessionItem.Session);
            PromptBox.Focus();
        }
    }

    private void OnTreeItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        var item = FindVisualAncestor<TreeViewItem>(args.OriginalSource as DependencyObject);
        if (item?.Header is ProjectGroupViewModel)
        {
            ToggleProjectItem(item);
            args.Handled = true;
        }
    }

    private static void ToggleProjectItem(TreeViewItem item)
    {
        item.IsSelected = false;
        item.IsExpanded = !item.IsExpanded;
    }

    private async void OnModelSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count == 1 && args.AddedItems[0] is ModelOptionViewModel model)
        {
            await _viewModel.SelectModelAsync(model);
        }
    }

    private void OnOpenProjectFolderClick(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: string cwd })
        {
            return;
        }

        if (!Directory.Exists(cwd))
        {
            _viewModel.ReportRecoverableError($"项目路径不存在：{cwd}");
            return;
        }

        try
        {
            Process.Start(FolderLauncher.CreateStartInfo(cwd));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _viewModel.ReportRecoverableError($"打开项目目录失败：{exception.Message}");
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_shutdownComplete)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _activityTimer.Stop();
        _completionCts?.Cancel();
        IsEnabled = false;
        await _viewModel.ShutdownAsync();
        _shutdownComplete = true;
        Close();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _subscribedMessages)
            {
                item.PropertyChanged -= OnMessagePropertyChanged;
            }

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

    private void OnMessageScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (args.ExtentHeightChange == 0)
        {
            var distanceFromBottom = Math.Max(0, args.ExtentHeight - args.VerticalOffset - args.ViewportHeight);
            _scrollCoordinator.OnViewportPositionChanged(distanceFromBottom);
            UpdateReturnToLatestVisibility();
        }
        else if (_scrollCoordinator.ShouldFollowExtentChange)
        {
            ScrollToBottomIfNeeded();
        }
    }

    private void OnMessagePreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        _scrollCoordinator.OnUserWheel(args.Delta);
        if (args.Delta > 0)
        {
            CancelPendingAutoScroll();
        }
        UpdateReturnToLatestVisibility();
    }

    private void OnReturnToLatestClick(object sender, RoutedEventArgs args)
    {
        _scrollCoordinator.ReturnToLatest();
        UpdateReturnToLatestVisibility();
        ScrollToBottomIfNeeded();
    }

    private void UpdateReturnToLatestVisibility()
    {
        ReturnToLatestButton.Visibility = _scrollCoordinator.IsFollowingLatest
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CancelPendingAutoScroll()
    {
        if (_pendingScrollOperation is { Status: DispatcherOperationStatus.Pending })
        {
            _pendingScrollOperation.Abort();
        }

        _pendingScrollOperation = null;
    }

    private void ScrollToBottomIfNeeded()
    {
        if (!_scrollCoordinator.IsFollowingLatest || _viewModel.Messages.Count == 0)
        {
            return;
        }

        if (_pendingScrollOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        _pendingScrollOperation = Dispatcher.BeginInvoke(() =>
            RunScrollToBottomPass(0), DispatcherPriority.Background);
    }

    private void RunScrollToBottomPass(int nPass)
    {
        _pendingScrollOperation = null;
        if (!_scrollCoordinator.IsFollowingLatest || _viewModel.Messages.Count == 0)
        {
            return;
        }

        MessageList.ScrollIntoView(_viewModel.Messages[^1]);
        MessageList.UpdateLayout();
        var scrollViewer = FindVisualChild<ScrollViewer>(MessageList);
        scrollViewer?.ScrollToEnd();

        if (nPass < 2)
        {
            _pendingScrollOperation = Dispatcher.BeginInvoke(
                () => RunScrollToBottomPass(nPass + 1),
                DispatcherPriority.ContextIdle);
        }
    }

    private void EnableDarkTitleBar()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(source.Handle, 20, ref enabled, sizeof(int));
    }

    private async Task<bool> RunScrollQaAsync(string outputDirectory)
    {
        var fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();

        var scrollViewer = FindVisualChild<ScrollViewer>(MessageList);
        if (scrollViewer is null || _viewModel.Messages.Count == 0)
        {
            await File.WriteAllTextAsync(
                Path.Combine(fullDirectory, "scroll-qa.txt"),
                "[失败] TEST-UI-01：未找到消息滚动区域或会话没有消息。",
                new UTF8Encoding(false));
            return false;
        }

        MessageList.ScrollIntoView(_viewModel.Messages[^1]);
        await SettleLatestPositionAsync(scrollViewer);
        var bottom = await CaptureScrollStepAsync(scrollViewer, fullDirectory, "01-bottom", scrollViewer.ScrollableHeight);

        _scrollCoordinator.OnUserWheel(120);
        CancelPendingAutoScroll();
        UpdateReturnToLatestVisibility();
        var smallUp = await CaptureScrollStepAsync(scrollViewer, fullDirectory, "02-small-up", Math.Max(0, bottom.VerticalOffset - 24));
        var upOneTarget = Math.Max(0, smallUp.VerticalOffset - 56);
        var upOne = await CaptureScrollStepAsync(scrollViewer, fullDirectory, "02-up-one-page", upOneTarget);
        var upFourTarget = Math.Max(0, upOne.VerticalOffset - 240);
        var upFour = await CaptureScrollStepAsync(scrollViewer, fullDirectory, "03-up-four-pages", upFourTarget);
        var downTwoTarget = Math.Min(scrollViewer.ScrollableHeight, upFour.VerticalOffset + 160);
        var downTwo = await CaptureScrollStepAsync(scrollViewer, fullDirectory, "04-down-two-pages", downTwoTarget);

        var bReadingIntentPassed = !_scrollCoordinator.IsFollowingLatest &&
                                   ReturnToLatestButton.Visibility == Visibility.Visible;
        OnReturnToLatestClick(ReturnToLatestButton, new RoutedEventArgs());
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var returned = await CaptureScrollStepAsync(scrollViewer, fullDirectory, "05-return-to-latest", null);

        var steps = new[] { bottom, smallUp, upOne, upFour, downTwo, returned };
        var smallScrollDelta = smallUp.BottomGap - bottom.BottomGap;
        var bDirectionPassed = bottom.BottomGap < smallUp.BottomGap &&
                               smallUp.BottomGap < upOne.BottomGap &&
                               upOne.BottomGap < upFour.BottomGap &&
                               downTwo.BottomGap < upFour.BottomGap;
        var bStablePassed = steps.All(step => step.StableFrames);
        var bResponsivePassed = steps.All(step => step.StepElapsedMilliseconds < 3000);
        var bThumbMeasurementsValid = steps.All(step => double.IsFinite(step.ThumbLength) && step.ThumbLength > 0);
        var thumbLengthDelta = bThumbMeasurementsValid
            ? steps.Max(step => step.ThumbLength) - steps.Min(step => step.ThumbLength)
            : double.PositiveInfinity;
        var bThumbLengthPassed = bThumbMeasurementsValid && thumbLengthDelta <= 1;
        var bReturnPassed = _scrollCoordinator.IsFollowingLatest &&
                            ReturnToLatestButton.Visibility == Visibility.Collapsed &&
                            scrollViewer.ScrollableHeight - returned.VerticalOffset <= 1;
        var bBottomGapPassed = returned.BottomGap <= 2;
        var bSmallScrollPassed = smallScrollDelta is >= 22 and <= 26;
        var bPassed = bDirectionPassed && bStablePassed && bResponsivePassed && bReadingIntentPassed && bReturnPassed &&
                      bThumbLengthPassed && bBottomGapPassed && bSmallScrollPassed;

        var report = new StringBuilder();
        report.AppendLine($"[{(bDirectionPassed ? "通过" : "失败")}] TEST-UI-01：上下滚动方向与内容底边距离一致");
        report.AppendLine($"[{(bStablePassed ? "通过" : "失败")}] TEST-UI-02：每个阅读位置静置双帧完全一致");
        report.AppendLine($"[{(bResponsivePassed ? "通过" : "失败")}] TEST-UI-03：每次滚动、布局与双帧捕获均小于 3000 ms");
        report.AppendLine($"[{(bReadingIntentPassed ? "通过" : "失败")}] TEST-UI-04：离开底部后保持历史阅读并显示回到最新入口");
        report.AppendLine($"[{(bReturnPassed ? "通过" : "失败")}] TEST-UI-05：点击回到最新后准确到底并恢复自动跟随");
        report.AppendLine($"[{(bThumbLengthPassed ? "通过" : "失败")}] TEST-UI-06：六个阅读位置的滚动条滑块长度差值不超过 1 DIP");
        report.AppendLine($"[{(bBottomGapPassed ? "通过" : "失败")}] TEST-UI-07：回到最新后末条内容底边与视口底边差值不超过 2 DIP");
        report.AppendLine($"[{(bSmallScrollPassed ? "通过" : "失败")}] TEST-UI-08：从底部上移 24 DIP 时保持像素级连续滚动");
        report.AppendLine($"BottomGap={returned.BottomGap:F1} DIP；SmallScrollDelta={smallScrollDelta:F1} DIP");
        report.AppendLine($"会话显示项：{_viewModel.Messages.Count}；视口高度：{scrollViewer.ViewportHeight:F1}；可滚动高度：{scrollViewer.ScrollableHeight:F1}");
        foreach (var step in steps)
        {
            report.AppendLine(
                $"{step.Name}: offset={step.VerticalOffset:F1}, elapsed={step.StepElapsedMilliseconds} ms, " +
                $"stable={step.StableFrames}, thumb={step.ThumbLength:F1} DIP, bottomGap={step.BottomGap:F1} DIP, sha256={step.FirstFrameHash}");
        }

        await File.WriteAllTextAsync(
            Path.Combine(fullDirectory, "scroll-qa.txt"),
            report.ToString(),
            new UTF8Encoding(false));
        return bPassed;
    }

    private async Task<ScrollQaStep> CaptureScrollStepAsync(
        ScrollViewer scrollViewer,
        string outputDirectory,
        string name,
        double? targetOffset)
    {
        var timer = Stopwatch.StartNew();
        if (targetOffset.HasValue)
        {
            scrollViewer.ScrollToVerticalOffset(targetOffset.Value);
        }
        await SettleScrollPositionAsync();

        var firstPath = Path.Combine(outputDirectory, $"{name}-a.png");
        var secondPath = Path.Combine(outputDirectory, $"{name}-b.png");
        var firstHash = CaptureElement(MessageList, firstPath);
        await Task.Delay(120);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        var secondHash = CaptureElement(MessageList, secondPath);
        timer.Stop();
        var thumbLength = GetVerticalThumbLength(scrollViewer);
        var bottomGap = GetLastItemBottomGap(scrollViewer);

        return new ScrollQaStep(
            name,
            scrollViewer.VerticalOffset,
            timer.ElapsedMilliseconds,
            string.Equals(firstHash, secondHash, StringComparison.Ordinal),
            thumbLength,
            bottomGap,
            firstHash);
    }

    private double GetLastItemBottomGap(ScrollViewer scrollViewer)
    {
        var nLastIndex = _viewModel.Messages.Count - 1;
        if (nLastIndex < 0 || MessageList.ItemContainerGenerator.ContainerFromIndex(nLastIndex) is not FrameworkElement lastItem)
        {
            return double.PositiveInfinity;
        }

        var bottom = lastItem.TranslatePoint(new Point(0, lastItem.ActualHeight), scrollViewer).Y;
        return Math.Abs(scrollViewer.ViewportHeight - bottom);
    }

    private async Task SettleScrollPositionAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        UpdateLayout();
        await Task.Delay(80);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        UpdateLayout();
    }

    private async Task SettleLatestPositionAsync(ScrollViewer scrollViewer)
    {
        var previousScrollableHeight = double.NaN;
        for (var nPass = 0; nPass < 6; nPass++)
        {
            MessageList.ScrollIntoView(_viewModel.Messages[^1]);
            MessageList.UpdateLayout();
            scrollViewer.ScrollToEnd();
            await SettleScrollPositionAsync();

            var bExtentStable = double.IsFinite(previousScrollableHeight) &&
                                Math.Abs(previousScrollableHeight - scrollViewer.ScrollableHeight) <= 0.5;
            if (bExtentStable && GetLastItemBottomGap(scrollViewer) <= 2)
            {
                return;
            }
            previousScrollableHeight = scrollViewer.ScrollableHeight;
        }
    }

    private static double GetVerticalThumbLength(ScrollViewer scrollViewer)
    {
        var scrollBar = scrollViewer.Template.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
        var track = scrollBar?.Template.FindName("PART_Track", scrollBar) as Track;
        if (track?.Thumb is not Thumb thumb || track.ActualHeight <= 0 || thumb.ActualHeight <= 0)
        {
            return double.NaN;
        }

        var thumbOrigin = thumb.TranslatePoint(new Point(0, 0), track);
        var visibleTop = Math.Max(0, thumbOrigin.Y);
        var visibleBottom = Math.Min(track.ActualHeight, thumbOrigin.Y + thumb.ActualHeight);
        return Math.Max(0, visibleBottom - visibleTop);
    }

    private string CaptureWindow(string outputPath)
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var nWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        var nHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(nWidth, nHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        encoder.Save(stream);
        stream.Flush();
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var nIndex = 0; nIndex < VisualTreeHelper.GetChildrenCount(parent); nIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, nIndex);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private sealed record ScrollQaStep(
        string Name,
        double VerticalOffset,
        long StepElapsedMilliseconds,
        bool StableFrames,
        double ThumbLength,
        double BottomGap,
        string FirstFrameHash);
}
