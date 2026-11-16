// Created: 2026-09-08
// Purpose: Verify steering (queued messages during streaming), conversation export, the
//          conversation tab strip and idle recycling of background conversations.

using System.Diagnostics;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class ConversationSteeringTests
{
    [TestCase("TEST-34A", "处理中插话：排队消息在当前轮次结束后自动发送")]
    public static async Task QueuedMessageIsSentAfterTurnSettlesAsync()
    {
        using var directory = new TemporaryDirectory();
        var commandLog = Path.Combine(directory.Path, "commands.log");
        await using var viewModel = MainViewModelTests.CreateViewModel(directory.Path, out _, commandLog, nPromptDelayMs: 700);
        await viewModel.CreateSessionAsync(directory.Path);
        viewModel.InputText = "第一条消息";
        await viewModel.SendAsync();
        AssertEx.Equal(ChatSessionState.Streaming, viewModel.State, "第一轮应处于流式处理中");

        viewModel.InputText = "第二条插队消息";
        await viewModel.SendAsync();
        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Queued && item.Text == "第二条插队消息"),
            "流式期间发送的消息应进入排队区");
        AssertEx.Equal("排队 1", viewModel.Active.PendingCountText, "排队计数应显示 1 条");
        AssertEx.Equal(string.Empty, viewModel.InputText, "排队后输入框应清空");

        await WaitUntilAsync(() => File.Exists(commandLog + ".prompts") &&
                ReadPromptLog(commandLog).Any(text => text.Contains("第二条插队消息", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(8));
        await WaitUntilAsync(() => viewModel.State == ChatSessionState.Ready, TimeSpan.FromSeconds(8));
        var prompts = ReadPromptLog(commandLog);
        AssertEx.True(prompts.Any(line => line.Contains("第二条插队消息", StringComparison.Ordinal)),
            "排队消息必须在轮次结束后自动续发");
        AssertEx.False(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Queued), "已发送的排队项应移除");
    }

    [TestCase("TEST-34B", "停止生成会撤回排队消息且不发送")]
    public static async Task StopRevokesQueuedMessagesAsync()
    {
        using var directory = new TemporaryDirectory();
        var commandLog = Path.Combine(directory.Path, "commands.log");
        await using var viewModel = MainViewModelTests.CreateViewModel(directory.Path, out _, commandLog, nPromptDelayMs: 700);
        await viewModel.CreateSessionAsync(directory.Path);
        viewModel.InputText = "第一条消息";
        await viewModel.SendAsync();
        viewModel.InputText = "不应发出的插队消息";
        await viewModel.SendAsync();
        AssertEx.Equal("排队 1", viewModel.Active.PendingCountText, "插队消息应先进入排队");

        await viewModel.StopAsync();
        await WaitUntilAsync(() => viewModel.Active.PendingCountText.Length == 0, TimeSpan.FromSeconds(3));
        AssertEx.False(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Queued), "停止后排队气泡应移除");

        // Let the delayed fake turn finish; the revoked message must never be dispatched.
        await WaitUntilAsync(() => viewModel.State == ChatSessionState.Ready, TimeSpan.FromSeconds(6));
        await Task.Delay(150);
        var prompts = ReadPromptLog(commandLog);
        AssertEx.False(prompts.Any(line => line.Contains("不应发出的插队消息", StringComparison.Ordinal)),
            "撤回的排队消息不得发送给 pi");
    }

    [TestCase("TEST-34C", "会话导出为 Markdown 包含用户与助手全文")]
    public static async Task ExportMarkdownContainsConversationAsync()
    {
        using var directory = new TemporaryDirectory();
        var sessionPath = MainViewModelTests.WriteConversationSession(directory, "export.jsonl");
        await using var viewModel = MainViewModelTests.CreateViewModel(directory.Path, out _);
        await viewModel.OpenSessionAsync(MainViewModelTests.CreateSummary(directory.Path, "导出会话", sessionPath));

        var markdown = viewModel.ExportActiveConversationMarkdown();
        AssertEx.True(markdown.StartsWith("# 导出会话", StringComparison.Ordinal), "导出标题应使用会话名");
        AssertEx.True(markdown.Contains("历史问题", StringComparison.Ordinal), "导出必须包含用户消息");
        AssertEx.True(markdown.Contains("历史回答", StringComparison.Ordinal), "导出必须包含助手消息");
    }

    [TestCase("TEST-34D", "空闲回收只关闭非活跃、非未读的后台会话")]
    public static async Task IdleRecycleSkipsActiveAndUnreadAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = MainViewModelTests.CreateViewModel(directory.Path, out var clients);
        await viewModel.UpdateShellPreferencesAsync(true, true, true, true, 5);
        var firstPath = MainViewModelTests.WriteConversationSession(directory, "recycle-a.jsonl");
        var secondPath = MainViewModelTests.WriteConversationSession(directory, "recycle-b.jsonl");
        var thirdPath = MainViewModelTests.WriteConversationSession(directory, "recycle-c.jsonl");

        await viewModel.OpenSessionAsync(MainViewModelTests.CreateSummary(directory.Path, "会话A", firstPath));
        await viewModel.OpenSessionAsync(MainViewModelTests.CreateSummary(directory.Path, "会话B", secondPath));
        await viewModel.OpenSessionAsync(MainViewModelTests.CreateSummary(directory.Path, "会话C", thirdPath));
        AssertEx.Equal(3, viewModel.OpenConversationCount, "三个会话应全部出现在标签列表");

        var now = DateTimeOffset.Now;
        var conversationA = viewModel.OpenConversations.First(item => item.Title == "会话A");
        var conversationB = viewModel.OpenConversations.First(item => item.Title == "会话B");
        conversationA.SetLastActivityForTest(now.AddMinutes(-6));
        conversationB.SetLastActivityForTest(now.AddMinutes(-6));
        conversationA.HasUnread = true; // 未读会话不得被回收

        await viewModel.RecycleIdleConversationsAsync(now);

        AssertEx.Equal(2, viewModel.OpenConversationCount, "只有空闲的会话 B 应被回收");
        AssertEx.True(viewModel.OpenConversations.Any(item => item.Title == "会话A"), "未读会话必须保留");
        AssertEx.True(viewModel.OpenConversations.Any(item => item.Title == "会话C"), "活跃会话必须保留");
        AssertEx.False(clients[1].IsRunning, "被回收会话的 pi 进程应退出");
        AssertEx.True(clients[0].IsRunning, "未读会话的 pi 进程应保持运行");
        AssertEx.True(clients[2].IsRunning, "活跃会话的 pi 进程应保持运行");
    }

    [TestCase("TEST-34E", "关闭标签页移除会话并停用对应 pi 进程")]
    public static async Task ClosingConversationTabRetiresClientAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = MainViewModelTests.CreateViewModel(directory.Path, out var clients);
        var firstPath = MainViewModelTests.WriteConversationSession(directory, "tab-a.jsonl");
        var secondPath = MainViewModelTests.WriteConversationSession(directory, "tab-b.jsonl");
        await viewModel.OpenSessionAsync(MainViewModelTests.CreateSummary(directory.Path, "会话一", firstPath));
        await viewModel.OpenSessionAsync(MainViewModelTests.CreateSummary(directory.Path, "会话二", secondPath));
        AssertEx.Equal(2, viewModel.OpenConversationCount, "两个标签页");

        var conversationOne = viewModel.OpenConversations.First(item => item.Title == "会话一");
        await viewModel.CloseConversationAsync(conversationOne);
        AssertEx.Equal(1, viewModel.OpenConversationCount, "关闭后只剩一个标签页");
        AssertEx.False(clients[0].IsRunning, "关闭标签页应停用对应的 pi 进程");
        AssertEx.True(clients[1].IsRunning, "保持中的会话不受影响");

        var conversationTwo = viewModel.OpenConversations.Single();
        await viewModel.CloseConversationAsync(conversationTwo);
        AssertEx.Equal(0, viewModel.OpenConversationCount, "全部关闭后标签列表为空");
        AssertEx.Equal("选择一个会话", viewModel.CurrentTitle, "全部关闭后回到初始主页");
    }

    [TestCase("TEST-34F", "外壳偏好与窗口布局持久化往返一致")]
    public static async Task ShellPreferencesRoundTripAsync()
    {
        using var directory = new TemporaryDirectory();
        var store = new SessionManagementStore(directory.Path);
        var preferences = new ShellPreferences(
            CloseToTray: false,
            ToastNotifications: true,
            FlashWindow: false,
            GlobalHotkeyEnabled: false,
            IdleRecycleMinutes: 15,
            Window: new WindowLayoutData(11, 22, 1280, 820, Maximized: true, SidebarWidth: 340));
        await store.SaveAsync(new SessionManagementState([], [], null, preferences));

        var loaded = await store.LoadAsync();
        AssertEx.NotNull(loaded.Preferences, "偏好必须持久化");
        AssertEx.False(loaded.Preferences!.CloseToTray == true, "CloseToTray 应写为 false");
        AssertEx.True(loaded.Preferences.ToastNotifications == true, "ToastNotifications 应写为 true");
        AssertEx.False(loaded.Preferences.FlashWindow == true, "FlashWindow 应写为 false");
        AssertEx.False(loaded.Preferences.GlobalHotkeyEnabled == true, "GlobalHotkeyEnabled 应写为 false");
        AssertEx.Equal(15, loaded.Preferences.IdleRecycleMinutes, "IdleRecycleMinutes 应写为 15");
        AssertEx.NotNull(loaded.Preferences.Window, "窗口布局必须持久化");
        AssertEx.Equal(340, loaded.Preferences.Window!.SidebarWidth, "侧栏宽度应持久化");
        AssertEx.True(loaded.Preferences.Window.Maximized, "最大化状态应持久化");
    }

    // The fake rpc logs prompts as JSON-escaped strings; decode them before comparing.
    private static IReadOnlyList<string> ReadPromptLog(string commandLog)
    {
        try
        {
            return File.ReadAllLines(commandLog + ".prompts")
                .Select(line => System.Text.Json.JsonSerializer.Deserialize<string>(line) ?? line)
                .ToList();
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed >= timeout)
            {
                throw new TimeoutException("等待条件超时");
            }

            await Task.Delay(25);
        }
    }
}
