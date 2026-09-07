using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;
using Microsoft.Win32;

namespace PIHarness.App;

public partial class MainWindow
{
    private CancellationTokenSource? _completionCts;
    private bool _acceptingCompletion;
    private bool _imeComposing;

    private void InitializeComposer()
    {
        TextCompositionManager.AddPreviewTextInputStartHandler(PromptBox, (_, _) => _imeComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(PromptBox, (_, _) => _imeComposing = false);
        _viewModel.PropertyChanged += OnComposerContextChanged;
        _viewModel.SlashCommands.CollectionChanged += (_, _) => RefreshCompletion();
    }

    private void OnComposerContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CurrentCwd) or nameof(MainViewModel.State)) RefreshCompletion();
        if (args.PropertyName == nameof(MainViewModel.CanEditComposer) && _viewModel.CanEditComposer)
            Dispatcher.BeginInvoke(() => PromptBox.Focus());
    }

    private void OnPromptSelectionChanged(object sender, RoutedEventArgs args) => RefreshCompletion();

    private async void RefreshCompletion()
    {
        if (CompletionPanel is null || _acceptingCompletion) return;
        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;
        var query = ComposerCompletion.GetQuery(PromptBox.Text, PromptBox.CaretIndex);
        if (query is null || PromptBox.SelectionLength > 0 || _viewModel.State == ChatSessionState.Starting)
        {
            CompletionPanel.Visibility = Visibility.Collapsed;
            return;
        }
        CompletionPanel.Visibility = Visibility.Visible;
        CompletionList.ItemsSource = null;
        CompletionHint.Text = "正在查找…";
        try
        {
            IReadOnlyList<ComposerSuggestion> suggestions;
            if (query.Trigger == '@')
            {
                var cwd = _viewModel.CurrentCwd;
                await Task.Delay(120, token);
                suggestions = await Task.Run(() => ComposerCompletion.FindFiles(cwd, query.Filter, token), token);
            }
            else
            {
                suggestions = _viewModel.SlashCommands.Where(item =>
                    item.Label.Contains(query.Filter, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(query.Filter, StringComparison.OrdinalIgnoreCase)).Take(40).ToArray();
            }
            if (token.IsCancellationRequested) return;
            CompletionList.ItemsSource = suggestions;
            CompletionList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
            CompletionHint.Text = suggestions.Count > 0
                ? (query.Trigger == '@' ? "项目文件（最多 40 项）" : "Pi 命令与 skills") + " · ↑↓ 选择 · Tab / Enter 插入 · Esc 关闭"
                : query.Trigger == '@' ? "未找到文件；请缩小关键词或确认已选择项目（跳过构建目录，限时搜索）" : _viewModel.CommandLoadStatus;
        }
        catch (OperationCanceledException) { }
    }

    private void OnPromptPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (_imeComposing || args.Key == Key.ImeProcessed)
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) args.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || CompletionPanel.Visibility != Visibility.Visible) return;
        if (args.Key == Key.Escape)
        {
            _completionCts?.Cancel();
            CompletionPanel.Visibility = Visibility.Collapsed;
            args.Handled = true;
        }
        else if (args.Key is Key.Up or Key.Down && CompletionList.Items.Count > 0)
        {
            var count = CompletionList.Items.Count;
            CompletionList.SelectedIndex = (CompletionList.SelectedIndex + (args.Key == Key.Down ? 1 : count - 1)) % count;
            CompletionList.ScrollIntoView(CompletionList.SelectedItem);
            args.Handled = true;
        }
        else if (args.Key is Key.Tab or Key.Enter && CompletionList.SelectedItem is ComposerSuggestion)
        {
            AcceptCompletion();
            args.Handled = true;
        }
    }

    private void OnCompletionClick(object sender, MouseButtonEventArgs args)
    {
        var item = FindVisualAncestor<ListBoxItem>(args.OriginalSource as DependencyObject);
        if (item?.DataContext is not ComposerSuggestion suggestion) return;
        CompletionList.SelectedItem = suggestion;
        AcceptCompletion();
        args.Handled = true;
    }

    private void AcceptCompletion()
    {
        var query = ComposerCompletion.GetQuery(PromptBox.Text, PromptBox.CaretIndex);
        if (query is null || CompletionList.SelectedItem is not ComposerSuggestion suggestion) return;
        _completionCts?.Cancel();
        _acceptingCompletion = true;
        try
        {
            PromptBox.Select(query.Start, query.Length);
            PromptBox.SelectedText = suggestion.InsertText;
            PromptBox.CaretIndex = query.Start + suggestion.InsertText.Length;
            PromptBox.SelectionLength = 0;
            CompletionPanel.Visibility = Visibility.Collapsed;
            PromptBox.Focus();
        }
        finally { _acceptingCompletion = false; }
    }

    private void OnPromptPasting(object sender, DataObjectPastingEventArgs args)
    {
        if (!_viewModel.CanEditComposer) { args.CancelCommand(); return; }
        try
        {
            if (args.DataObject.GetDataPresent(DataFormats.Bitmap))
            {
                args.CancelCommand();
                if (args.DataObject.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bitmap)
                    _viewModel.AddAttachment(ImageAttachmentViewModel.FromBitmap(bitmap, $"粘贴图片 {_viewModel.Attachments.Count + 1}.png"));
                else _viewModel.ReportRecoverableError("剪贴板图片无法读取，请保存后使用“＋ 图片”添加。");
            }
            else if (args.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                args.CancelCommand();
                if (args.DataObject.GetData(DataFormats.FileDrop) is string[] paths) AddImageFiles(paths);
            }
        }
        catch (Exception exception) when (IsImageError(exception))
        {
            args.CancelCommand();
            _viewModel.ReportRecoverableError($"粘贴图片失败：{exception.Message}");
        }
    }

    private void OnAttachImageClick(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Title = "添加图片", Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true) AddImageFiles(dialog.FileNames);
        PromptBox.Focus();
    }

    private void AddImageFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_viewModel.Attachments.Count >= ImageAttachmentViewModel.MaxImages)
            {
                _viewModel.ReportRecoverableError("每条消息最多添加 4 张图片。");
                break;
            }
            try { _viewModel.AddAttachment(ImageAttachmentViewModel.FromFile(path)); }
            catch (Exception exception) when (IsImageError(exception))
            { _viewModel.ReportRecoverableError($"无法添加 {Path.GetFileName(path)}：{exception.Message}"); }
        }
    }

    private static bool IsImageError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or NotSupportedException or ArgumentException or InvalidOperationException or COMException;

    private void OnRemoveImageClick(object sender, RoutedEventArgs args)
    {
        if (_viewModel.CanEditComposer && sender is Button { Tag: ImageAttachmentViewModel image }) _viewModel.Attachments.Remove(image);
        PromptBox.Focus();
    }

    private void OnPreviewImageClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ImageAttachmentViewModel image }) return;
        var preview = new Window
        {
            Owner = this, Title = image.Detail, Width = 900, Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("WindowBrush"),
            Content = new Image { Source = image.OpenPreview(), Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(12) },
        };
        preview.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) preview.Close(); };
        preview.ShowDialog();
    }
}
