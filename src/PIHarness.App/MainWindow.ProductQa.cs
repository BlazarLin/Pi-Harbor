using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.App;

public partial class MainWindow
{
    private async Task CaptureProductQaAsync(string directory, Action<bool, string> check)
    {
        await _viewModel.RefreshOverviewAsync();
        _viewModel.IsOverviewVisible = true;
        await SettleStyleLayoutAsync();
        check(OverviewPanel.IsVisible && _viewModel.UsageDays.Count == 28 && _viewModel.UsageDays.Any(day => day.Usage.Tokens > 0), "Overview 展示全部本机会话的 28 天用量");
        check(_viewModel.RecentSessions.Count == 3, "首页最近会话包含外部写入的独立测试会话");
        check(RecentSessionTree.IsVisible && RecentSessionTree.Items.Count == 3 &&
              RecentSessionTree.TranslatePoint(new Point(), this).Y < SessionTree.TranslatePoint(new Point(), this).Y,
              "最近对话快捷区位于文件夹分类上方");
        var recentLabel = FindSearchQaLabels(RecentSessionTree).First(label => label.Text == _viewModel.SidebarRecentSessions[0].Title);
        check(FindContextMenuAncestor(recentLabel)?.ToolTip is string tip && tip.Contains("距上次对话：") && tip.Contains("分钟前"),
              "最近对话悬停包含距上次交流的时间差");
        CaptureWindow(Path.Combine(directory, "overview.png"));
        check(FindQaButtons(SessionTree).Any(button => button.Tag is string cwd && Directory.Exists(cwd)), "文件夹行提供带正确目录的加号按钮");
        check(!FindQaButtons(this).Any(button => Equals(button.Content, "选择目录新建…") || Equals(button.Content, "默认目录…")), "新建附属选项不常驻主界面");
        OnNewSessionClick(NewSessionButton, new RoutedEventArgs());
        await SettleStyleLayoutAsync();
        check(NewSessionButton.ContextMenu.IsOpen && NewSessionButton.ContextMenu.Items.OfType<MenuItem>().Count() == 3, "新建按钮打开三项二级菜单");
        CaptureWindow(Path.Combine(directory, "new-session.png"));
        NewSessionButton.ContextMenu.IsOpen = false;

        var cwdDemo = Path.Combine(Path.GetFullPath(directory), "示例项目");
        File.WriteAllText(Path.Combine(cwdDemo, "camera.cpp"), "// synthetic");
        File.WriteAllText(Path.Combine(cwdDemo, "camera.h"), "// synthetic");
        for (var i = 0; i < 30; i++) File.WriteAllText(Path.Combine(cwdDemo, $"sample-{i:D2}.txt"), "synthetic");
        _viewModel.Active.PrepareDocumentationDemo(cwdDemo);
        _viewModel.IsOverviewVisible = false;
        PromptBox.Text = "@";
        PromptBox.CaretIndex = 1;
        await Task.Delay(800);
        await SettleStyleLayoutAsync();
        check(CompletionList.Items.Count >= 30 && CompletionList.ActualHeight > 160, "文件候选更多且列表高度增大");
        CaptureWindow(Path.Combine(directory, "file-candidates.png"));
        var selected = (ComposerSuggestion)CompletionList.Items[0];
        CompletionList.SelectedIndex = 0;
        AcceptCompletion();
        check(PromptBox.Text.Contains("〔文件1〕") && _viewModel.Active.ExpandFileReferences(PromptBox.Text).Trim() == selected.InsertText.Trim(), "紧凑占位符保留原始路径");

        _viewModel.Messages.Clear();
        _viewModel.Messages.Add(new ChatItemViewModel(ChatItemKind.User, "检查相机模块，并给出预览图。"));
        _viewModel.Messages.Add(new ChatItemViewModel(ChatItemKind.Thinking, "检查输入和图像尺寸。"));
        _viewModel.Messages.Add(new ChatItemViewModel(ChatItemKind.Tool, "检查完成") { Title = "读取 camera.cpp" });
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(35, 34, 40)), null, new Rect(0, 0, 480, 180));
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(125, 115, 234)), null, new Rect(30, 30, 420, 120), 14, 14);
            context.DrawEllipse(Brushes.White, null, new Point(240, 90), 40, 40);
        }
        var bitmap = new RenderTargetBitmap(480, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var imagePath = Path.Combine(cwdDemo, "preview.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(imagePath)) encoder.Save(stream);
        _viewModel.Messages.Add(new ChatItemViewModel(ChatItemKind.Assistant, "检查完成，下面是输出预览：\n\n![输出预览](preview.png)"));
        _viewModel.Messages.Add(new ChatItemViewModel(ChatItemKind.Metrics, "⏱ 2.0 秒 · Tokens 1200"));
        await SettleStyleLayoutAsync();
        await Task.Delay(500);
        MessageList.UpdateLayout();
        var group = _viewModel.DisplayMessages.Single(item => item.Kind == ChatItemKind.ActivityGroup);
        check(!group.IsExpanded && group.Children.Count == 2, "整轮思考与工具步骤在完成后收起");
        check(FindQaButtons(MessageList).Any(button => button.Content is Image { Source: not null }), "Markdown 项目相对图片显示真实缩略图");
        CaptureWindow(Path.Combine(directory, "output-preview.png"));
        group.IsExpanded = true;
        group.CompleteActivity();
        check(group.IsExpanded, "手动展开的整轮过程在结束时保留");
        await SettleStyleLayoutAsync();
        CaptureWindow(Path.Combine(directory, "turn-expanded.png"));
        for (var i = 0; i < 9; i++)
        {
            var timestamp = DateTimeOffset.UtcNow.AddHours(-i - 2);
            File.WriteAllLines(Path.Combine(directory, "sessions", $"recent-extra-{i}.jsonl"), [
                System.Text.Json.JsonSerializer.Serialize(new { type = "session", version = 3, cwd = cwdDemo, timestamp }),
                System.Text.Json.JsonSerializer.Serialize(new { type = "message", id = "u", timestamp, message = new { role = "user", content = $"演示任务 {i + 1}" } })
            ]);
        }
        await _viewModel.RefreshCatalogAsync();
        await SettleStyleLayoutAsync();
        check(RecentSessionTree.Items.Count == 10 && _viewModel.Projects.Sum(project => project.Sessions.Count) == 12,
              "最近区只显示十条，文件夹保留全部十二条");
        CaptureWindow(Path.Combine(directory, "recent-ten.png"));
        var originalHeight = Height;
        Height = MinHeight;
        await SettleStyleLayoutAsync();
        check(RecentSessionTree.ActualHeight > 40 && SessionTree.ActualHeight > 40,
              "最小窗口高度仍可浏览最近区和文件夹区");
        CaptureWindow(Path.Combine(directory, "recent-compact.png"));
        Height = originalHeight;
    }
}
