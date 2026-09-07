using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PIHarness.App.ViewModels;

namespace PIHarness.App;

public partial class MainWindow
{
    private async Task<bool> RunComposerQaAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var results = new List<string>();
        void Check(string name, bool passed) => results.Add($"[{(passed ? "通过" : "失败")}] {name}");
        try
        {
            await _viewModel.CreateSessionAsync(Environment.CurrentDirectory);
            Check("真实 Pi 会话就绪（不发送模型请求）：" + _viewModel.StatusText, _viewModel.State == ChatSessionState.Ready);
            Activate();
            PromptBox.Focus();
            PromptBox.Text = "检查输入光标：中文与 English\n第二行可继续输入";
            PromptBox.CaretIndex = PromptBox.Text.Length;
            await SettleStyleLayoutAsync();
            CaptureWindow(Path.Combine(outputDirectory, "01-caret.png"));
            Check("输入框焦点、亮色光标与多行输入", PromptBox.IsKeyboardFocused &&
                PromptBox.CaretBrush is SolidColorBrush brush && brush.Color.R > 200 && PromptBox.LineCount == 2);
            var caretFrames = new HashSet<string>();
            for (var index = 0; index < 10; index++)
            {
                await Task.Delay(100);
                caretFrames.Add(CaptureElement(PromptBox, Path.Combine(outputDirectory, $"caret-{index}.png")));
            }
            Check("聚焦后输入框静置帧有光标闪烁变化", caretFrames.Count > 1);

            PromptBox.Text = "请查看 @MainWindow";
            PromptBox.CaretIndex = PromptBox.Text.Length;
            await Task.Delay(1800);
            await SettleStyleLayoutAsync();
            CaptureWindow(Path.Combine(outputDirectory, "02-files.png"));
            Check("@ 从当前项目查找文件", CompletionList.Items.Count > 0);
            var downKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            PromptBox.RaiseEvent(downKey);
            Check("方向键选择补全项", downKey.Handled && CompletionList.SelectedIndex == 1);
            var tabKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            PromptBox.RaiseEvent(tabKey);
            Check("文件引用替换触发词且保留前文", PromptBox.Text.StartsWith("请查看 \"") && !PromptBox.Text.Contains('@'));

            PromptBox.Text = "/";
            PromptBox.CaretIndex = 1;
            await SettleStyleLayoutAsync();
            CaptureWindow(Path.Combine(outputDirectory, "03-commands.png"));
            Check("/ 菜单使用真实 Pi 命令", _viewModel.State == ChatSessionState.Ready && CompletionPanel.Visibility == Visibility.Visible && CompletionList.Items.Count == Math.Min(40, _viewModel.SlashCommands.Count));
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            OnPromptPreviewKeyDown(PromptBox, key);
            Check("Esc 关闭补全菜单", key.Handled && CompletionPanel.Visibility == Visibility.Collapsed);

            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 40, 62)), null, new Rect(0, 0, 480, 240));
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(125, 115, 234)), null, new Rect(35, 40, 410, 160), 20, 20);
                context.DrawEllipse(Brushes.White, null, new Point(155, 120), 40, 40);
                context.DrawRectangle(Brushes.LightSkyBlue, null, new Rect(255, 80, 90, 80));
            }
            var bitmap = new RenderTargetBitmap(480, 240, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var data = new DataObject(DataFormats.Bitmap, bitmap);
            PromptBox.Text = "请分析这张图片中的形状与颜色。";
            PromptBox.CaretIndex = PromptBox.Text.Length;
            PromptBox.RaiseEvent(new DataObjectPastingEventArgs(data, false, DataFormats.Bitmap));
            Check("粘贴事件添加图片并允许发送", _viewModel.Attachments.Count == 1 && _viewModel.CanSend);
            var attachment = _viewModel.Attachments[0];
            Check("图片编码与预览可解码", attachment.ToPromptImage().MimeType == "image/png" && attachment.OpenPreview().PixelWidth > 0);
            var outgoing = new ChatItemViewModel(Core.Models.ChatItemKind.User, "图片附件预览示例（未发送）") { Images = [attachment] };
            _viewModel.Messages.Add(outgoing);
            await SettleStyleLayoutAsync();
            CaptureWindow(Path.Combine(outputDirectory, "04-images.png"));
            var previewOpened = false;
            var previewCapture = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.Owner == this);
                if (dialog is null) return;
                previewOpened = true;
                try
                {
                    dialog.UpdateLayout();
                    CaptureElement(dialog, Path.Combine(outputDirectory, "05-preview.png"));
                }
                finally { dialog.Close(); }
            });
            OnPreviewImageClick(new Button { Tag = attachment }, new RoutedEventArgs());
            await previewCapture;
            Check("缩略图点击打开实际大图预览窗口", previewOpened);
            for (var index = 0; index < 4; index++) _viewModel.AddAttachment(attachment);
            Check("附件最多四张", _viewModel.Attachments.Count == 4);
            OnRemoveImageClick(new Button { Tag = attachment }, new RoutedEventArgs());
            Check("移除附件", _viewModel.Attachments.Count == 3);
            _viewModel.Attachments.Clear();
            _viewModel.AddAttachment(attachment);
            PromptBox.Text = "";
            Check("仅图片也允许发送", _viewModel.CanSend);
            var textData = new DataObject(DataFormats.UnicodeText, "普通文字");
            var textPaste = new DataObjectPastingEventArgs(textData, false, DataFormats.UnicodeText);
            OnPromptPasting(PromptBox, textPaste);
            Check("普通文字粘贴保留默认行为", !textPaste.CommandCancelled);
            Width = MinWidth;
            Height = MinHeight;
            PromptBox.Text = "在较小窗口中输入消息";
            await SettleStyleLayoutAsync();
            CaptureWindow(Path.Combine(outputDirectory, "06-small-window.png"));
            Check("最小窗口下输入框可见", PromptBox.ActualWidth > 200 && ComposerBorder.ActualHeight < ActualHeight / 2);
        }
        catch (Exception exception) { results.Add($"[失败] {exception}"); }
        await File.WriteAllLinesAsync(Path.Combine(outputDirectory, "composer-qa.txt"), results, new UTF8Encoding(false));
        return results.All(result => result.StartsWith("[通过]", StringComparison.Ordinal));
    }
}
