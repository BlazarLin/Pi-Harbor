// Created: 2026-09-06
// Purpose: Handle Windows-only shell interactions and preserve chat scroll intent.

using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PIHarness.App.ViewModels;
using PIHarness.Core.Sessions;

namespace PIHarness.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly HashSet<ChatItemViewModel> _subscribedMessages = [];
    private DispatcherOperation? _pendingScrollOperation;
    private bool _stickToBottom = true;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
        SourceInitialized += (_, _) => EnableDarkTitleBar();
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
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(100);
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
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_shutdownComplete)
        {
            return;
        }

        args.Cancel = true;
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
            _stickToBottom = true;
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
            _stickToBottom = args.ExtentHeight - args.VerticalOffset - args.ViewportHeight < 80;
        }
        else if (_stickToBottom)
        {
            ScrollToBottomIfNeeded();
        }
    }

    private void ScrollToBottomIfNeeded()
    {
        if (!_stickToBottom || _viewModel.Messages.Count == 0)
        {
            return;
        }

        if (_pendingScrollOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        _pendingScrollOperation = Dispatcher.BeginInvoke(() =>
        {
            _pendingScrollOperation = null;
            if (_stickToBottom && _viewModel.Messages.Count > 0)
            {
                MessageList.ScrollIntoView(_viewModel.Messages[^1]);
            }
        }, DispatcherPriority.Background);
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

    private void CaptureWindow(string outputPath)
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
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(stream);
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
}
