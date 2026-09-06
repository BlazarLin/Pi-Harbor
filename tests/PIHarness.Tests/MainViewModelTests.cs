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
        await using var viewModel = CreateViewModel(directory.Path, out _);

        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "历史会话"));

        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.User && item.Text == "历史问题"), "应显示历史用户消息");
        AssertEx.True(viewModel.Messages.Any(item => item.Kind == ChatItemKind.Assistant && item.Text == "历史回答"), "应显示历史助手消息");
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

    [TestCase("TEST-13", "连续切换空闲会话始终只保留一个 RPC 客户端")]
    public static async Task KeepsOnlyOneActiveClientAsync()
    {
        using var directory = new TemporaryDirectory();
        await using var viewModel = CreateViewModel(directory.Path, out var clients);
        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "会话一"));
        await viewModel.OpenSessionAsync(CreateSummary(directory.Path, "会话二"));

        AssertEx.Equal(2, clients.Count, "两次打开应创建两个顺序客户端");
        AssertEx.False(clients[0].IsRunning, "切换后旧客户端必须退出");
        AssertEx.True(clients[1].IsRunning, "新客户端应保持运行");
    }

    private static MainViewModel CreateViewModel(string sessionRoot, out List<PiRpcClient> clients)
    {
        clients = [];
        var capturedClients = clients;
        return new MainViewModel(sessionRoot, () =>
        {
            var client = new PiRpcClient(CreateFakeStartInfo);
            capturedClients.Add(client);
            return client;
        });
    }

    private static ProcessStartInfo CreateFakeStartInfo(PiStartOptions options)
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
        return startInfo;
    }

    private static SessionSummary CreateSummary(string directory, string title) =>
        new(Path.Combine(directory, $"{Guid.NewGuid():N}.jsonl"), directory, title, DateTimeOffset.Now, DateTimeOffset.Now);

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
}
