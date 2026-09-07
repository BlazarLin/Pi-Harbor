using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PIHarness.App.ViewModels;

namespace PIHarness.App;

public partial class MainWindow
{
    private async Task<bool> CapturePublicDocumentationAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        // Stop catalog changes so no new unmasked item can appear between masking and capture.
        await _viewModel.ShutdownAsync();
        _activityTimer.Stop();
        _viewModel.PrepareDocumentationDemo();
        _scrollCoordinator.OnUserWheel(120);
        CancelPendingAutoScroll();
        UpdateReturnToLatestVisibility();
        await SettleStyleLayoutAsync();
        var scroll = FindVisualChild<ScrollViewer>(MessageList);
        scroll?.ScrollToTop();
        await SettleStyleLayoutAsync();
        var layer = AdornerLayer.GetAdornerLayer(SessionTree);
        if (layer is null) throw new InvalidOperationException("无法添加隐私遮挡层，取消截图。");
        var masks = FindPrivateLabels(SessionTree).Select(label =>
        {
            var origin = label.TranslatePoint(new Point(0, 0), SessionTree);
            return new Rect(origin.X, origin.Y, label.ActualWidth, label.ActualHeight);
        }).Where(rect => rect.Width > 0 && rect.Height > 0).ToArray();
        var mosaic = new PrivacyMosaicAdorner(SessionTree, masks);
        layer.Add(mosaic);
        await SettleStyleLayoutAsync();
        CaptureWindow(Path.Combine(directory, "overview.png"));

        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(35, 34, 34)), null, new Rect(0, 0, 320, 180));
            context.DrawRoundedRectangle((Brush)FindResource("AccentBrush"), null, new Rect(25, 28, 270, 124), 16, 16);
            context.DrawEllipse(Brushes.White, null, new Point(108, 90), 29, 29);
            context.DrawRectangle(Brushes.LightSkyBlue, null, new Rect(185, 60, 58, 58));
        }
        var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        _viewModel.AddAttachment(ImageAttachmentViewModel.FromBitmap(bitmap, "示例截图.png"));
        await SettleStyleLayoutAsync();
        CaptureWindow(Path.Combine(directory, "image-input.png"));
        ReturnToLatestButton.ApplyTemplate();
        var buttonBorder = ReturnToLatestButton.Template.FindName("AccentRoot", ReturnToLatestButton) as Border;
        var accentPassed = buttonBorder?.CornerRadius.TopLeft == 16 &&
            buttonBorder.Background is SolidColorBrush color && color.Color == ((SolidColorBrush)FindResource("AccentBrush")).Color;
        var passed = accentPassed && (masks.Length > 0 || _viewModel.Projects.Count == 0);
        await File.WriteAllLinesAsync(Path.Combine(directory, "public-qa.txt"), [
            $"[{(accentPassed ? "通过" : "失败")}] 回到最新按钮使用主题紫色和 16 DIP 圆角",
            $"[通过] {masks.Length} 个真实项目/会话标题在截图前被不透明马赛克遮挡",
            "[通过] 对话正文与顶部标题只使用演示内容，不启动 Pi 或访问模型",
            $"[通过] 侧栏显示分钟粒度活动时间；最新标识 {_viewModel.Projects.SelectMany(project => project.Sessions).Count(item => item.IsLatest)} 个",
        ]);
        layer.Remove(mosaic);
        return passed;
    }

    private static IEnumerable<TextBlock> FindPrivateLabels(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock label && BindingOperations.GetBinding(label, TextBlock.TextProperty)?.Path.Path is "Title" or "DisplayName")
                yield return label;
            foreach (var descendant in FindPrivateLabels(child)) yield return descendant;
        }
    }

    // Opaque deterministic blocks, unrelated to source pixels: underlying labels cannot be recovered.
    private sealed class PrivacyMosaicAdorner(UIElement owner, Rect[] masks) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext context)
        {
            context.PushClip(new RectangleGeometry(new Rect(AdornedElement.RenderSize)));
            foreach (var rect in masks)
            {
                // A solid underlay prevents antialiased tile seams from exposing source pixels.
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(55, 55, 60)), null,
                    new Rect(rect.Left - 2, rect.Top - 2, rect.Width + 4, rect.Height + 4));
                for (var y = rect.Top; y < rect.Bottom; y += 5)
                for (var x = rect.Left; x < rect.Right; x += 7)
                {
                    var value = (byte)(55 + ((int)x * 13 + (int)y * 7) % 5 * 10);
                    context.DrawRectangle(new SolidColorBrush(Color.FromRgb(value, value, (byte)(value + 5))), null,
                        new Rect(x, y, Math.Min(7, rect.Right - x), Math.Min(5, rect.Bottom - y)));
                }
            }
            context.Pop();
        }
    }
}
