using System.IO;
using System.Text.Json;
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
    private static MainViewModel CreateSearchQaViewModel(string directory)
    {
        var root = Path.Combine(Path.GetFullPath(directory), "sessions");
        if (Directory.Exists(root)) throw new InvalidOperationException("搜索验收需要新的目录。");
        Directory.CreateDirectory(root);
        var cwd = Path.Combine(Path.GetFullPath(directory), "示例项目");
        Directory.CreateDirectory(cwd);
        var examples = new[] {
            ("first", "设计搜索与重命名体验", "检查窗口缓存更新时是否丢失搜索结果。"),
            ("second", "窗口缓存异常排查", "保留日志并验证恢复过程。"),
            ("third", "确定 Pi Harbor 图标", "采用紫色底部色带与 harbor 字样。"),
        };
        for (var i = 0; i < examples.Length; i++)
        {
            var (id, title, text) = examples[i];
            var timestamp = DateTimeOffset.UtcNow.AddMinutes(-i * 30 - 5);
            File.WriteAllLines(Path.Combine(root, id + ".jsonl"), [
                JsonSerializer.Serialize(new { type = "session", version = 3, id, cwd, timestamp }),
                JsonSerializer.Serialize(new { type = "message", id = "u", timestamp, message = new { role = "user", content = title } }),
                JsonSerializer.Serialize(new { type = "message", id = "a", parentId = "u", timestamp, message = new { role = "assistant", content = text } }),
            ]);
        }
        return new MainViewModel(root, rpcClientFactory: () => throw new InvalidOperationException("搜索验收不得启动 Pi"), namesDirectory: Path.Combine(directory, "names"));
    }

    private async Task<bool> CaptureSearchQaAsync(string directory)
    {
        var checks = new List<string>();
        void Check(bool pass, string text) { checks.Add($"[{(pass ? "通过" : "失败")}] {text}"); }
        try
        {
            _viewModel.SearchText = "窗口缓存";
            await _viewModel.SearchNowAsync();
            await SettleStyleLayoutAsync();
            Check(GlobalSearchResults.IsVisible && !SessionTree.IsVisible && _viewModel.SearchResults.Count == 2, "全局搜索同时命中正文与名称，显示 2 个结果");
            CaptureWindow(Path.Combine(directory, "search.png"));
            _viewModel.SearchText = "";
            await SettleStyleLayoutAsync();
            Check(SessionTree.IsVisible && !GlobalSearchResults.IsVisible, "清空搜索恢复项目树");
            var session = _viewModel.Projects[0].Sessions.First(item => Path.GetFileName(item.SessionPath) == "first.jsonl").Session;
            var label = FindSearchQaLabels(SessionTree).First(item => item.Text == session.Title);
            var host = FindContextMenuAncestor(label);
            var menu = host?.ContextMenu;
            Check(menu is not null, "会话行存在重命名菜单");
            menu!.PlacementTarget = host;
            menu.IsOpen = true;
            await SettleStyleLayoutAsync();
            var menuItem = (MenuItem)menu.Items[0];
            Check(menuItem.Tag is SessionSummary actual && actual.SessionPath == session.SessionPath, "右键菜单绑定到目标会话，未打开 Pi");
            menu.IsOpen = false;
            var bytes = await File.ReadAllBytesAsync(session.SessionPath);
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                var dialog = Application.Current.Windows.OfType<RenameSessionDialog>().Single();
                await SettleStyleLayoutAsync();
                dialog.NameBox.Text = "搜索模块复盘";
                await SettleStyleLayoutAsync();
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(directory, "rename-dialog.png"))) encoder.Save(stream);
                FindQaButtons(dialog).Single(button => Equals(button.Content, "保存")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            await RenameSessionAsync(session);
            await SettleStyleLayoutAsync();
            Check(_viewModel.Projects[0].Sessions.Any(item => item.Title == "搜索模块复盘"), "真实对话框保存名称后更新侧栏");
            var after = await File.ReadAllBytesAsync(session.SessionPath);
            Check(bytes.SequenceEqual(after), "重命名不改写原始 Pi 会话内容");
            CaptureWindow(Path.Combine(directory, "renamed.png"));
            _viewModel.SearchText = "搜索模块复盘";
            await _viewModel.SearchNowAsync();
            Check(_viewModel.SearchResults.Count == 1, "自定义名称进入全局搜索");
            await _viewModel.RenameSessionAsync(session, null);
            _viewModel.SearchText = "";
            Check(_viewModel.Projects[0].Sessions.Any(item => item.Title == session.Title), "恢复默认名称");
        }
        catch (Exception error) { checks.Add($"[失败] {error}"); }
        await File.WriteAllLinesAsync(Path.Combine(directory, "search-qa.txt"), checks);
        return checks.All(line => line.StartsWith("[通过]", StringComparison.Ordinal));
    }

    private static FrameworkElement? FindContextMenuAncestor(DependencyObject start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement { ContextMenu: not null } owner) return owner;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static IEnumerable<TextBlock> FindSearchQaLabels(DependencyObject owner)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
        {
            var child = VisualTreeHelper.GetChild(owner, i);
            if (child is TextBlock label) yield return label;
            foreach (var descendant in FindSearchQaLabels(child)) yield return descendant;
        }
    }
    private static IEnumerable<Button> FindQaButtons(DependencyObject owner)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
        {
            var child = VisualTreeHelper.GetChild(owner, i);
            if (child is Button button) yield return button;
            foreach (var descendant in FindQaButtons(child)) yield return descendant;
        }
    }
}
