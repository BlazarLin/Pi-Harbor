using System.Text.Json;
using System.Xml.Linq;
using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;
using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class ReleaseReadinessTests
{
    [TestCase("TEST-32A", "活动时间以分钟为最小粒度且正确跨越小时、天和未来时间")]
    public static void RelativeActivityTimes()
    {
        var now = DateTimeOffset.Parse("2026-09-07T08:00:00Z");
        AssertEx.Equal("不到 1 分钟", RelativeActivityTime.Format(now.AddSeconds(-59), now), "不显示秒级跳动");
        AssertEx.Equal("1 分钟前", RelativeActivityTime.Format(now.AddMinutes(-1), now), "分钟边界");
        AssertEx.Equal("59 分钟前", RelativeActivityTime.Format(now.AddMinutes(-59), now), "小时前边界");
        AssertEx.Equal("1 小时前", RelativeActivityTime.Format(now.AddHours(-1), now), "小时边界");
        AssertEx.Equal("2 天前", RelativeActivityTime.Format(now.AddDays(-2), now), "跨天");
        AssertEx.Equal("不到 1 分钟", RelativeActivityTime.Format(now.AddMinutes(2), now), "时钟偏差不出现负数");
    }

    [TestCase("TEST-32B", "启动时根目录不存在，终端后来创建会话仍自动出现")]
    public static async Task WatchesLateCreatedRoot()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "late-root");
        await using var vm = MainViewModelTests.CreateViewModel(root, out _);
        await vm.InitializeAsync();
        await Task.Delay(100);
        WriteSession(root, "project/new.jsonl", "终端新会话", DateTimeOffset.UtcNow);
        await WaitUntil(() => vm.Projects.SelectMany(project => project.Sessions).Any(item => item.Title == "终端新会话"));
    }

    [TestCase("TEST-32C", "持续写入期间不饿死刷新，已有会话更新仍排序并保留折叠")]
    public static async Task WatchesContinuousWrites()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.UtcNow;
        var path = WriteSession(directory.Path, "project/first.jsonl", "第一会话", now.AddHours(-1));
        await using var vm = MainViewModelTests.CreateViewModel(directory.Path, out _);
        await vm.InitializeAsync();
        vm.Projects[0].IsExpanded = false;
        var second = WriteSession(directory.Path, "project/second.jsonl", "第二会话", now);
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < 30; i++)
            {
                await File.AppendAllTextAsync(second, "\n");
                await Task.Delay(80);
            }
        });
        await WaitUntil(() => vm.Projects.SelectMany(project => project.Sessions).Count() == 2, 2000);
        AssertEx.False(vm.Projects[0].IsExpanded, "自动刷新保留项目折叠");
        AssertEx.True(vm.Projects[0].Sessions[0].IsLatest, "最新会话标识");
        await writer;
        await File.AppendAllTextAsync(path, "\n" + JsonSerializer.Serialize(new { type = "message", id = "new-message", timestamp = now.AddMinutes(1), message = new { role = "assistant", content = "终端继续交流" } }));
        await WaitUntil(() => vm.Projects[0].Sessions[0].Title == "第一会话");
        var currentItem = vm.Projects[0].Sessions[0];
        vm.RefreshRelativeActivityTimes(now.AddMinutes(6));
        AssertEx.Equal("5 分钟前", currentItem.RelativeActivityText, "没有新文件事件也能刷新分钟差");
    }

    [TestCase("TEST-32D", "回到最新使用主题紫色圆角和独立悬停、按下状态")]
    public static void AccentReturnButton()
    {
        var doc = XDocument.Load("src/PIHarness.App/MainWindow.xaml");
        var button = doc.Descendants().Single(node => node.Attributes().Any(attr => attr.Name.LocalName == "Name" && attr.Value == "ReturnToLatestButton"));
        AssertEx.Equal("{StaticResource AccentButtonStyle}", button.Attribute("Style")?.Value, "按钮不能回退系统方角模板");
        var theme = XDocument.Load("src/PIHarness.App/Themes/Colors.xaml");
        var style = theme.Descendants().Single(node => node.Attributes().Any(attr => attr.Name.LocalName == "Key" && attr.Value == "AccentButtonStyle"));
        AssertEx.True(style.Descendants().Any(node => node.Attribute("CornerRadius")?.Value == "16"), "圆角必须进入真实模板");
        AssertEx.True(style.Descendants().Any(node => node.Attribute("Value")?.Value == "{StaticResource AccentBrush}"), "复用主题紫色");
    }

    private static string WriteSession(string root, string relativePath, string title, DateTimeOffset timestamp)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "session", version = 3, id = Guid.NewGuid().ToString(), cwd = root, timestamp }) + "\n" +
            JsonSerializer.Serialize(new { type = "message", id = "user", timestamp, message = new { role = "user", content = title } }));
        return path;
    }

    internal static async Task VerifyInstalledPiDiscoveryAsync(string modulePath)
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "external-pi-sessions");
        await using var vm = MainViewModelTests.CreateViewModel(root, out _);
        await vm.InitializeAsync();
        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["PI_QA_MODULE"] = new Uri(Path.GetFullPath(modulePath)).AbsoluteUri;
        start.Environment["PI_QA_ROOT"] = root;
        start.Environment["PI_QA_CWD"] = directory.Path;
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
            import { existsSync } from 'node:fs';
            const { SessionManager } = await import(process.env.PI_QA_MODULE);
            const manager = SessionManager.create(process.env.PI_QA_CWD, process.env.PI_QA_ROOT);
            if (existsSync(manager.getSessionFile())) throw new Error('Unexpected persisted empty session');
            manager.appendMessage({ role: 'user', content: [{ type: 'text', text: '外部 Pi 会话发现验收' }], timestamp: Date.now() });
            manager.appendMessage({ role: 'assistant', content: [{ type: 'text', text: '离线演示，不调用模型' }], api: 'test', provider: 'test', model: 'test', usage: {}, stopReason: 'stop', timestamp: Date.now() });
            if (!existsSync(manager.getSessionFile())) throw new Error('Pi did not persist the conversation');
            console.log('Pi SessionManager persisted a session from an external Node process.');
            """);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        AssertEx.Equal(0, process.ExitCode, await errors);
        await WaitUntil(() => vm.Projects.SelectMany(project => project.Sessions).Any(item => item.Title == "外部 Pi 会话发现验收"));
        Console.WriteLine(await output);
        Console.WriteLine("[通过] 本机 Pi SessionManager 创建的会话自动进入侧栏；空会话未落盘；未调用模型。");
    }

    private static async Task WaitUntil(Func<bool> condition, int timeout = 8000)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > timeout) throw new InvalidOperationException("会话目录未在预期时间内刷新");
            await Task.Delay(50);
        }
    }
}
