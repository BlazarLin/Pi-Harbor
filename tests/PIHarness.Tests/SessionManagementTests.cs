using System.Text.Json;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;
using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class SessionManagementTests
{
    public static async Task VerifyInstalledPiReloadAsync()
    {
        using var temp = new TemporaryDirectory();
        var pi = PiProcessLocator.Find();
        AssertEx.True(pi.Found, "需要安装 Pi");
        await using var vm = new MainViewModel(Path.Combine(temp.Path, "sessions"), () => new PiRpcClient(options =>
        {
            var start = PiProcessLocator.CreateStartInfo(pi.PiCommandPath!, options with { NoSession = true, Offline = true });
            start.Environment["PI_CODING_AGENT_DIR"] = Path.Combine(temp.Path, "agent");
            return start;
        }), Path.Combine(temp.Path, "names"));
        await vm.CreateSessionAsync(temp.Path);
        AssertEx.Equal(ChatSessionState.Ready, vm.State, vm.StatusText);
        var prompts = Path.Combine(temp.Path, ".pi", "prompts");
        var skills = Path.Combine(temp.Path, ".pi", "skills", "reload-probe");
        Directory.CreateDirectory(prompts);
        Directory.CreateDirectory(skills);
        File.WriteAllText(Path.Combine(prompts, "reload-probe.md"), "---\ndescription: Reload prompt probe\n---\nThis is an offline test.");
        File.WriteAllText(Path.Combine(skills, "SKILL.md"), "---\nname: reload-probe\ndescription: Reload skill probe\n---\nThis is an offline test.");
        AssertEx.False(vm.SlashCommands.Any(command => command.Label.Contains("reload-probe", StringComparison.Ordinal)), "启动后新增资源尚未加载");
        vm.InputText = "保留离线草稿";
        await vm.Active.ReloadAsync();
        AssertEx.Equal(ChatSessionState.Ready, vm.State, vm.StatusText);
        AssertEx.True(vm.SlashCommands.Any(command => command.Label == "/reload-probe"), "真实 Pi 重载提示模板");
        AssertEx.True(vm.SlashCommands.Any(command => command.Label == "/skill:reload-probe"), "真实 Pi 重载 skill");
        AssertEx.Equal("保留离线草稿", vm.InputText, "重载保留草稿");
        Console.WriteLine("[通过] 本机 Pi 离线重载新提示模板和 skill，草稿保留；无模型调用、无用户配置变更。");
    }

    [TestCase("TEST-40A", "归档可恢复、统计包含归档、搜索仍命中原始文件")]
    public static async Task ArchiveAndSearchAsync()
    {
        using var temp = new TemporaryDirectory();
        var old = WriteSession(temp, "old", DateTimeOffset.UtcNow.AddDays(-8));
        WriteSession(temp, "recent", DateTimeOffset.UtcNow.AddMinutes(-5));
        var bytes = File.ReadAllBytes(old.SessionPath);
        await using (var vm = MainViewModelTests.CreateViewModel(temp.Path, out _))
        {
            await vm.InitializeAsync();
            AssertEx.Equal(2, vm.TotalSessionCount, "全部会话数");
            AssertEx.Equal(1, vm.ActiveSessionCount, "滚动 7 天活跃数");
            AssertEx.Equal(1, vm.GetArchiveCandidates(DateTimeOffset.Now).Length, "仅旧会话符合整理条件");
            await vm.SetArchivedAsync([old], true);
            AssertEx.Equal(1, vm.Projects.Sum(p => p.Sessions.Count), "默认隐藏归档");
            AssertEx.Equal(2, vm.TotalSessionCount, "归档不减少总数");
            vm.SearchText = "old";
            await vm.SearchNowAsync();
            AssertEx.Equal(1, vm.SearchResults.Count, "搜索包含归档");
            vm.SessionFilter = 1;
            AssertEx.Equal(old.SessionPath, vm.Projects.Single().Sessions.Single().SessionPath, "归档筛选");
        }
        await using var restored = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await restored.InitializeAsync();
        AssertEx.Equal(1, restored.ArchivedSessionCount, "重启后保留归档");
        await restored.SetArchivedAsync([old], false);
        AssertEx.Equal(2, restored.Projects.Sum(p => p.Sessions.Count), "恢复回默认列表");
        AssertEx.True(bytes.SequenceEqual(File.ReadAllBytes(old.SessionPath)), "管理操作不修改 Pi 文件");
    }

    [TestCase("TEST-40B", "未打开的会话也可标为未读并跨重启保存")]
    public static async Task ManualUnreadAsync()
    {
        using var temp = new TemporaryDirectory();
        var session = WriteSession(temp, "unread", DateTimeOffset.UtcNow.AddDays(-10));
        await using (var vm = MainViewModelTests.CreateViewModel(temp.Path, out _))
        {
            await vm.InitializeAsync();
            await vm.MarkSessionUnreadAsync(session, true);
            AssertEx.Equal(1, vm.UnreadCount, "未打开也计数");
            AssertEx.Equal(0, vm.GetArchiveCandidates(DateTimeOffset.Now).Length, "整理保留未读");
            AssertEx.True(vm.Projects.Single().Sessions.Single().HasUnread, "侧栏标记");
        }
        await using var restored = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await restored.InitializeAsync();
        AssertEx.Equal(1, restored.UnreadCount, "重启保留未读");
        await restored.OpenSessionAsync(session);
        AssertEx.Equal(0, restored.UnreadCount, "查看清除未读");
        await restored.MarkSessionUnreadAsync(session, true);
        await restored.OpenSessionAsync(session);
        AssertEx.Equal(0, restored.UnreadCount, "再次打开当前会话清除人工未读");
    }

    [TestCase("TEST-40C", "窗口后台时当前会话完成也标未读，回来查看后清除")]
    public static async Task BackgroundWindowAsync()
    {
        using var temp = new TemporaryDirectory();
        var session = WriteSession(temp, "background", DateTimeOffset.UtcNow);
        await using var vm = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await vm.InitializeAsync();
        await vm.OpenSessionAsync(session);
        var attention = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConversationAttention += (_, _) => attention.TrySetResult();
        vm.IsWindowActive = false;
        vm.InputText = "开始";
        await vm.SendAsync();
        await attention.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEx.Equal(1, vm.UnreadCount, "后台当前会话需要提醒");
        vm.IsWindowActive = true;
        vm.MarkActiveRead();
        AssertEx.Equal(0, vm.UnreadCount, "回到窗口已查看");
    }

    [TestCase("TEST-40D", "重载保留草稿和历史、重建 RPC，默认目录持久化")]
    public static async Task ReloadAndDefaultDirectoryAsync()
    {
        using var temp = new TemporaryDirectory();
        var session = WriteSession(temp, "reload", DateTimeOffset.UtcNow);
        var log = Path.Combine(temp.Path, "commands.log");
        await using (var vm = MainViewModelTests.CreateViewModel(temp.Path, out var clients, log))
        {
            await vm.InitializeAsync();
            await vm.SetDefaultWorkingDirectoryAsync(temp.Path);
            await vm.CreateDefaultSessionAsync();
            AssertEx.Equal(temp.Path, vm.CurrentCwd, "默认目录直接创建");
            await vm.OpenSessionAsync(session);
            vm.InputText = "保留草稿";
            var count = vm.Messages.Count;
            var previous = clients.Last();
            await vm.Active.ReloadAsync();
            AssertEx.False(previous.IsRunning, "重载回收旧进程");
            AssertEx.Equal("保留草稿", vm.InputText, "草稿保留");
            AssertEx.Equal(count, vm.Messages.Count, "历史保留");
            AssertEx.Equal(session.SessionPath, vm.SelectedSessionPath, "原会话路径保留");
            AssertEx.Equal(2, vm.Models.Count, "重读模型列表");
            vm.InputText = "/reload";
            await vm.SendAsync();
            AssertEx.False(File.ReadAllLines(log).Contains("prompt"), "重载不作为模型问题发送");
            AssertEx.Equal(ChatSessionState.Ready, vm.State, "重载后可继续");
        }
        await using var restored = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await restored.InitializeAsync();
        AssertEx.Equal(temp.Path, restored.DefaultWorkingDirectory, "默认目录跨重启保留");
    }

    [TestCase("TEST-40E", "设置损坏可恢复并再次保存")]
    public static async Task CorruptSettingsAsync()
    {
        using var temp = new TemporaryDirectory();
        var store = new SessionManagementStore(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "session-management.json"), "{bad");
        AssertEx.Equal(0, (await store.LoadAsync()).Unread.Length, "损坏文件降级");
        await store.SaveAsync(new SessionManagementState([], ["C:\\test.jsonl"], temp.Path));
        AssertEx.Equal(1, (await store.LoadAsync()).Unread.Length, "可再次保存");
    }

    [TestCase("TEST-40F", "处理中不归档不重载、重点关注不被批量整理")]
    public static async Task ProtectBusyAndStarredAsync()
    {
        using var temp = new TemporaryDirectory();
        var session = WriteSession(temp, "busy", DateTimeOffset.UtcNow.AddDays(-9));
        await using var vm = MainViewModelTests.CreateViewModel(temp.Path, out var clients, nStateDelayMs: 800);
        await vm.InitializeAsync();
        await vm.ToggleStarAsync(session);
        AssertEx.Equal(0, vm.GetArchiveCandidates(DateTimeOffset.Now).Length, "重点关注不整理");
        var open = vm.OpenSessionAsync(session);
        await Task.Delay(100);
        AssertEx.Equal(1, vm.BusyCount, "启动也计入处理中");
        await vm.SetArchivedAsync([session], true);
        await vm.Active.ReloadAsync();
        AssertEx.Equal(0, vm.ArchivedSessionCount, "不能归档处理中会话");
        AssertEx.Equal(1, clients.Count, "不能重载正在启动的会话");
        await open;
        AssertEx.Equal(0, vm.BusyCount, "完成启动后汇总归零");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => vm.MarkSessionUnreadAsync(session, index % 2 == 0)));
        var state = await new SessionManagementStore(Path.Combine(temp.Path, ".harbor-test")).LoadAsync();
        AssertEx.Equal(0, state.Unread.Length, "连续写入的最终已读状态保留");
    }

    private static SessionSummary WriteSession(TemporaryDirectory temp, string name, DateTimeOffset timestamp)
    {
        var path = temp.WriteSession(name + ".jsonl", string.Join('\n',
            JsonSerializer.Serialize(new { type = "session", version = 3, id = name, cwd = temp.Path, timestamp }),
            JsonSerializer.Serialize(new { type = "message", id = "u", timestamp, message = new { role = "user", content = name } })));
        return new SessionSummary(path, temp.Path, name, timestamp, timestamp);
    }
}
