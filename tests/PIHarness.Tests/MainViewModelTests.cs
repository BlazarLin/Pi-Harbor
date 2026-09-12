// Created: 2026-09-06
// Purpose: Verify chat orchestration, streaming event mapping, watcher refresh, and client replacement.

using System.Diagnostics;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;
using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class MainViewModelTests
{
    [TestCase("TEST-07", "打开会话后加载当前有效分支的历史消息")]
    public static async Task LoadsExistingMessagesAsync()
    {
        using var directory = new TemporaryDirectory();
        var sessionPath = WriteConversationSession(directory, "history.jsonl");
        await using var viewModel = CreateViewModel(directory.Path, out _);

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "历史会话", sessionPath));

        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.User && item.Text == "历史问题"), "应显示历史用户消息");
        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Assistant && item.Text == "历史回答"), "应显示历史助手消息");
    }

    [TestCase("TEST-16B", "本地历史先显示且打开会话不请求巨型 get_messages")]
    public static async Task ShowsLocalHistoryBeforePiIsReadyAsync()
    {
        using var directory = new TemporaryDirectory();
        var sessionPath = WriteConversationSession(directory, "local-first.jsonl");
        var commandLog = Path.Combine(directory.Path, "rpc-commands.log");
        await using var viewModel = CreateViewModel(directory.Path, out _, commandLog, 800);

        var openTask = viewModel.OpenSessionAsync(CreateSummary(directory.Path, "本地优先", sessionPath));
        await Task.Delay(250);

        AssertEx.True(viewModel.Messages.Any(item => item.Text == "历史回答"), "pi 尚未就绪时应先显示本地历史");
        AssertEx.Equal(ChatSessionState.Starting, viewModel.State, "pi 后台初始化期间应保持 Starting");

        await openTask;
        var commands = File.Exists(commandLog) ? await File.ReadAllLinesAsync(commandLog) : [];
        AssertEx.False(commands.Contains("get_messages", StringComparer.Ordinal), "已有会话不得复制完整 get_messages 响应");
    }

    [TestCase("TEST-08", "助手增量文本归并到同一条消息")]
    public static async Task CoalescesAssistantDeltasAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = CreateViewModel(directory.Path, out _);
        await viewModel.CreateSessionAsync(directory.Path);
        viewModel.InputText = "开始流式测试";

        await viewModel.SendAsync();
        await WaitUntilAsync(() => viewModel.State == ChatSessionState.Ready, TimeSpan.FromSeconds(3));

        var streamed = viewModel.Messages.Where(item => item.Kind == ChatItemKind.Assistant && item.Text == "流式回复").ToArray();
        AssertEx.Equal(1, streamed.Length, "流式文本应归并为一条最终助手消息");
        var metrics = viewModel.Messages.Single(item => item.Kind == ChatItemKind.Metrics);
        AssertEx.True(metrics.Text.Contains("Tokens 350", StringComparison.Ordinal), "实时轮次应在 settled 后显示累计 token");
    }

    [TestCase("TEST-22B", "会话就绪后列出模型并可显式切换")]
    public static async Task ListsAndSwitchesModelsAsync()
    {
        using var directory = new TemporaryDirectory();
        var commandLog = Path.Combine(directory.Path, "model-commands.log");
        await using var viewModel = CreateViewModel(directory.Path, out _, commandLog);

        await viewModel.CreateSessionAsync(directory.Path);

        AssertEx.Equal(2, viewModel.Models.Count, "应加载 fake RPC 返回的全部可用模型");
        AssertEx.Equal("fake/fake-model", viewModel.SelectedModel?.Key, "应选中会话当前模型");
        AssertEx.True(viewModel.CanChangeModel, "就绪状态应允许切换模型");

        var alternate = viewModel.Models.Single(model => model.Id == "alternate-model");
        await viewModel.SelectModelAsync(alternate);

        AssertEx.Equal("fake/alternate-model", viewModel.SelectedModel?.Key, "切换成功后应更新选中模型");
        AssertEx.Equal("fake/alternate-model", viewModel.ModelText, "状态栏应同步当前模型");
        var commands = await File.ReadAllLinesAsync(commandLog);
        AssertEx.True(commands.Contains("set_model", StringComparer.Ordinal), "必须向 pi 发送 set_model 命令");
    }

    [TestCase("TEST-09", "思考、工具和工具结果使用正确显示类型")]
    public static async Task MapsRpcEventsToChatItemsAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = CreateViewModel(directory.Path, out _);
        await viewModel.CreateSessionAsync(directory.Path);
        viewModel.InputText = "触发事件映射";

        await viewModel.SendAsync();
        await WaitUntilAsync(() => viewModel.State == ChatSessionState.Ready, TimeSpan.FromSeconds(3));

        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Thinking && item.Text.Contains("思考过程", StringComparison.Ordinal)), "应映射思考内容");
        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Tool && item.Text.Contains("工具结果", StringComparison.Ordinal)), "应映射工具结果");
    }

    [TestCase("TEST-09B", "模型失败且正文为空时显示具体错误")]
    public static async Task ShowsAssistantErrorMessageAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = CreateViewModel(directory.Path, out _);
        await viewModel.CreateSessionAsync(directory.Path);
        viewModel.InputText = "触发模型错误";

        await viewModel.SendAsync();
        await WaitUntilAsync(() => viewModel.State == ChatSessionState.Ready, TimeSpan.FromSeconds(3));

        AssertEx.True(
            viewModel.Messages.Any(item => item.Kind == ChatItemKind.Error && item.Text.Contains("429", StringComparison.Ordinal)),
            "聊天区应显示 pi 返回的模型错误");
    }

    [TestCase("TEST-11", "目录变化刷新侧边栏且保留当前服务稳定")]
    public static async Task RefreshesCatalogAfterFileChangeAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = CreateViewModel(directory.Path, out _);
        await viewModel.InitializeAsync();
        WriteSession(directory, "first.jsonl", "G:\\Code\\Watch", "第一个");
        await WaitUntilAsync(() => viewModel.Projects.Sum(project => project.Sessions.Count) == 1, TimeSpan.FromSeconds(4));

        WriteSession(directory, "second.jsonl", "G:\\Code\\Watch", "第二个");
        await WaitUntilAsync(() => viewModel.Projects.Sum(project => project.Sessions.Count) == 2, TimeSpan.FromSeconds(4));

        AssertEx.Equal(2, viewModel.Projects[0].Sessions.Count, "自动刷新后应显示两个会话");
    }

    [TestCase("TEST-24A", "打开、刷新和切换会话时侧栏只保持一个当前项")]
    public static async Task KeepsCurrentSessionAcrossCatalogRefreshAsync()
    {
        using var directory = new TemporaryDirectory();
        var firstPath = WriteConversationSession(directory, "first-current.jsonl");
        var secondPath = directory.WriteSession(
            "second-current.jsonl",
            """
            {"type":"session","version":3,"id":"session-second","timestamp":"2026-09-06T00:00:00Z","cwd":"G:\\Code\\PI-Harness"}
            {"type":"message","id":"second-user","parentId":null,"timestamp":"2026-09-06T00:00:03Z","message":{"role":"user","content":"第二个会话"}}
            """);
        await using var viewModel = CreateViewModel(directory.Path, out _);
        await viewModel.InitializeAsync();

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "第一个会话", firstPath));
        AssertCurrentSession(viewModel, firstPath, "打开会话后");

        await viewModel.RefreshCatalogAsync();
        AssertCurrentSession(viewModel, firstPath, "目录刷新后");

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "第二个会话", secondPath));
        AssertCurrentSession(viewModel, secondPath, "切换会话后");

        await viewModel.CreateSessionAsync(directory.Path);
        AssertEx.Equal(0, viewModel.Projects.SelectMany(project => project.Sessions).Count(session => session.IsCurrent), "新对话不应继续高亮历史会话");
    }

    [TestCase("TEST-13", "多会话并行时每个会话保持独立 RPC 客户端运行")]
    public static async Task KeepsIndependentClientsForParallelSessionsAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = CreateViewModel(directory.Path, out var clients);
        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "会话一"));
        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "会话二"));

        AssertEx.Equal(2, clients.Count, "两个会话应各自创建一个客户端");
        AssertEx.True(clients[0].IsRunning, "并行会话的旧客户端必须保持运行");
        AssertEx.True(clients[1].IsRunning, "新客户端应保持运行");
        AssertEx.False(ReferenceEquals(clients[0], clients[1]), "两个会话不得共享客户端");

        await viewModel.ShutdownAsync();
        AssertEx.False(clients[0].IsRunning, "关闭后第一个客户端应退出");
        AssertEx.False(clients[1].IsRunning, "关闭后第二个客户端应退出");
    }

    [TestCase("TEST-13B", "后台会话完成时标记未读且不丢失消息")]
    public static async Task MarksUnreadWhenBackgroundTurnSettlesAsync()
    {
        using var directory = new TemporaryDirectory();
        var firstPath = WriteConversationSession(directory, "unread-first.jsonl");
        var secondPath = WriteConversationSession(directory, "unread-second.jsonl");
        await using var viewModel = CreateViewModel(directory.Path, out _);

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "后台会话", firstPath));
        viewModel.InputText = "触发事件映射";
        await viewModel.SendAsync();
        await WaitUntilAsync(() => viewModel.State == ChatSessionState.Ready, TimeSpan.FromSeconds(3));
        var firstConversation = viewModel.Active;

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "前台会话", secondPath));
        AssertEx.True(firstConversation.Messages.Any(item => item.Kind == ChatItemKind.Assistant && item.Text == "流式回复"),
            "切走后后台会话消息必须保留");
        AssertEx.Equal(0, viewModel.UnreadCount, "已读过的会话不算未读");

        firstConversation.InputText = "后台再次发送";
        await firstConversation.SendAsync();
        await WaitUntilAsync(() => firstConversation.State == ChatSessionState.Ready, TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => viewModel.UnreadCount == 1, TimeSpan.FromSeconds(3));
        AssertEx.True(firstConversation.HasUnread, "后台完成的轮次应标记未读");

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "后台会话", firstPath));
        AssertEx.False(firstConversation.HasUnread, "重新查看后未读标记应清除");
        AssertEx.Equal(0, viewModel.UnreadCount, "查看后未读计数归零");
    }

    internal static MainViewModel CreateViewModel(
        string sessionRoot,
        out List<PiRpcClient> clients,
        string? commandLog = null,
        int nStateDelayMs = 0, string? namesDirectory = null)
    {
        clients = [];
        var capturedClients = clients;
        return new MainViewModel(sessionRoot, () =>
        {
            var client = new PiRpcClient(options => CreateFakeStartInfo(options, commandLog, nStateDelayMs));
            capturedClients.Add(client);
            return client;
        }, namesDirectory);
    }

    private static ProcessStartInfo CreateFakeStartInfo(PiStartOptions options, string? commandLog, int nStateDelayMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--fake-rpc");
        if (!string.IsNullOrWhiteSpace(commandLog))
        {
            startInfo.Environment["PI_HARNESS_FAKE_COMMAND_LOG"] = commandLog;
        }
        if (nStateDelayMs > 0)
        {
            startInfo.Environment["PI_HARNESS_FAKE_STATE_DELAY_MS"] = nStateDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return startInfo;
    }

    private static SessionSummary CreateSummary(string directory, string title, string? sessionPath = null) =>
        new(sessionPath ?? Path.Combine(directory, $"{Guid.NewGuid():N}.jsonl"), directory, title, DateTimeOffset.Now, DateTimeOffset.Now);

    private static string WriteConversationSession(TemporaryDirectory directory, string fileName)
    {
        return directory.WriteSession(
            fileName,
            """
            {"type":"session","version":3,"id":"session-history","timestamp":"2026-09-06T00:00:00Z","cwd":"G:\\Code\\PI-Harness"}
            {"type":"message","id":"history-user","parentId":null,"timestamp":"2026-09-06T00:00:01Z","message":{"role":"user","content":"历史问题"}}
            {"type":"message","id":"history-answer","parentId":"history-user","timestamp":"2026-09-06T00:00:02Z","message":{"role":"assistant","content":[{"type":"text","text":"历史回答"}]}}
            """);
    }

    private static void WriteSession(TemporaryDirectory directory, string fileName, string cwd, string title)
    {
        var escapedCwd = cwd.Replace("\\", "\\\\", StringComparison.Ordinal);
        directory.WriteSession(
            fileName,
            $"{{\"type\":\"session\",\"version\":3,\"id\":\"{Guid.NewGuid()}\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"cwd\":\"{escapedCwd}\"}}\n" +
            $"{{\"type\":\"message\",\"id\":\"11111111\",\"parentId\":null,\"timestamp\":\"2026-09-06T00:00:01Z\",\"message\":{{\"role\":\"user\",\"content\":\"{title}\"}}}}");
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

    private static void AssertCurrentSession(MainViewModel viewModel, string expectedPath, string stage)
    {
        var current = viewModel.Projects.SelectMany(project => project.Sessions).Where(session => session.IsCurrent).ToArray();
        AssertEx.Equal(1, current.Length, $"{stage}应且仅应有一个当前会话");
        AssertEx.Equal(Path.GetFullPath(expectedPath), Path.GetFullPath(current[0].SessionPath), $"{stage}当前高亮路径必须正确");
    }
}
