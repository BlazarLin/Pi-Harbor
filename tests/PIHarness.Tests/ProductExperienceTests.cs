using System.Text.Json;
using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class ProductExperienceTests
{
    [TestCase("TEST-41A", "文件候选扩充、源码优先，支持 /file 中文带空格查询")]
    public static void FileCandidatePriority()
    {
        using var temp = new TemporaryDirectory();
        for (var i = 0; i < 180; i++) File.WriteAllText(Path.Combine(temp.Path, $"a{i}.bin"), "");
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        File.WriteAllText(Path.Combine(temp.Path, "src", "z.cpp"), "");
        File.WriteAllText(Path.Combine(temp.Path, "src", "中文 文件.h"), "");
        var candidates = ComposerCompletion.FindFiles(temp.Path, "", CancellationToken.None);
        AssertEx.Equal(120, candidates.Count, "候选上限增加");
        AssertEx.True(candidates.Take(2).All(item => item.Label.EndsWith(".cpp") || item.Label.EndsWith(".h")), "子目录源码不能被前面的二进制挤掉");
        var query = ComposerCompletion.GetQuery("/file 中文 文件", 11);
        AssertEx.Equal("中文 文件", query?.Filter, "文件命令支持空格");
    }

    [TestCase("TEST-41B", "短文件占位符发送时恢复原文，草稿与新建会话隔离")]
    public static async Task ReferencesAndFreshSessionsAsync()
    {
        using var temp = new TemporaryDirectory();
        var log = Path.Combine(temp.Path, "rpc.log");
        await using var vm = MainViewModelTests.CreateViewModel(temp.Path, out var clients, log);
        await vm.CreateSessionAsync(temp.Path);
        var first = vm.Active;
        var reference = first.AddFileReference("src/中文 文件.cpp", "\"src/中文 文件.cpp\" ");
        first.InputText = "检查 " + reference;
        var draft = first.InputText;
        await vm.CreateSessionAsync(temp.Path);
        AssertEx.Equal(2, clients.Count, "同一目录新建得到独立会话");
        AssertEx.Equal(draft, first.InputText, "旧草稿仍保留");
        AssertEx.Equal(0, vm.FileReferences.Count, "新会话不混入旧文件");
        await first.SendAsync();
        var actual = JsonSerializer.Deserialize<string>((await File.ReadAllLinesAsync(log + ".prompts")).Single());
        AssertEx.Equal("检查 \"src/中文 文件.cpp\"", actual, "RPC 中必须是原始路径文字");
        AssertEx.Equal(0, first.FileReferences.Count, "发送确认后清除文件标签");
    }

    [TestCase("TEST-41C", "整轮过程分组，完成后自动折叠且保留手动展开")]
    public static void TurnGrouping()
    {
        var conversation = new ConversationViewModel("qa", () => throw new InvalidOperationException(), null);
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.User, "问题一"));
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.Thinking, "思考"));
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.Tool, "检查"));
        var group = conversation.DisplayMessages.Single(item => item.Kind == ChatItemKind.ActivityGroup);
        group.ExpandAutomatically();
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.Assistant, "最终结果"));
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.Metrics, "完成"));
        AssertEx.False(group.IsExpanded, "未干预自动折叠");
        AssertEx.Equal(2, group.Children.Count, "思考工具均属于同一轮");
        AssertEx.True(conversation.DisplayMessages.Any(item => item.Text == "最终结果"), "结果保持可见");
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.User, "问题二"));
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.Thinking, "第二轮"));
        var second = conversation.DisplayMessages.Last();
        second.IsExpanded = true;
        conversation.Messages.Add(new ChatItemViewModel(ChatItemKind.Metrics, "完成"));
        AssertEx.True(second.IsExpanded, "用户展开不自动收起");
        AssertEx.Equal(2, conversation.DisplayMessages.Count(item => item.Kind == ChatItemKind.ActivityGroup), "按问题独立分组");
    }

    [TestCase("TEST-41D", "完成通知和窗口标题使用首条问题而不是新对话")]
    public static async Task NotificationTitleAsync()
    {
        using var temp = new TemporaryDirectory();
        await using var vm = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await vm.CreateSessionAsync(temp.Path);
        await vm.Active.ReloadAsync();
        var attention = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConversationAttention += (conversation, _) => attention.TrySetResult(conversation.Title);
        vm.IsWindowActive = true;
        vm.IsOverviewVisible = true;
        vm.InputText = "检查相机标定结果";
        await vm.SendAsync();
        AssertEx.Equal("检查相机标定结果", await attention.Task.WaitAsync(TimeSpan.FromSeconds(5)), "通知携带对话名称");
        AssertEx.Equal("检查相机标定结果", vm.CurrentTitle, "窗口同步标题");
        AssertEx.True(vm.Active.HasUnread, "停留 Overview 不等于正在查看当前会话");
    }

    [TestCase("TEST-41E", "用量包含终端和归档会话，刷新新增记录、去重和容错")]
    public static async Task UsageOverviewAsync()
    {
        using var temp = new TemporaryDirectory();
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.Date);
        string Entry(string id, long tokens) => JsonSerializer.Serialize(new { type = "message", id, timestamp = now, message = new { role = "assistant", usage = new { totalTokens = tokens } } });
        var path = temp.WriteSession("external.jsonl", JsonSerializer.Serialize(new { type = "session", version = 3, cwd = temp.Path, timestamp = now }) + "\n" + Entry("a", 125) + "\n" + Entry("a", 125) + "\n{broken\n");
        var index = new SessionUsageIndex();
        var first = await index.ReadAsync([path], today, CancellationToken.None);
        AssertEx.Equal(125L, first.Days.Last().Tokens, "同一消息不重复计数");
        AssertEx.Equal(1, first.InvalidRecords, "损坏记录反馈");
        File.AppendAllText(path, Entry("b", 75) + "\n");
        AssertEx.Equal(200L, (await index.ReadAsync([path], today, CancellationToken.None)).Days.Last().Tokens, "缓存发现追加");
        await using var vm = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await vm.InitializeAsync();
        var session = vm.Projects.Single().Sessions.Single().Session;
        await vm.SetArchivedAsync([session], true);
        await vm.RefreshOverviewAsync();
        AssertEx.Equal("200", vm.TodayTokens, "归档不移除统计");
        AssertEx.Equal(1, vm.RecentSessions.Count, "未从 Harbor 新建也进入首页");
        AssertEx.Equal(28, vm.UsageDays.Count, "连续 28 天包含零值日");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await AssertEx.ThrowsAsync<OperationCanceledException>(() => index.ReadAsync([path], today, cancel.Token), "关闭时允许取消");
    }

    [TestCase("TEST-41F", "图片路径支持项目相对路径与 file URI，拒绝网络共享自动读取")]
    public static void ImageReferences()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "中文 图.png");
        AssertEx.Equal(path, MarkdownTextBlock.ResolveImagePath("中文%20图.png", temp.Path), "相对路径解码");
        AssertEx.Equal(path, MarkdownTextBlock.ResolveImagePath(new Uri(path).AbsoluteUri, temp.Path), "file URI");
        AssertEx.Equal<string?>(null, MarkdownTextBlock.ResolveImagePath(@"\\server\share\a.png", temp.Path), "UNC 不自动访问");
        AssertEx.Equal<string?>(null, MarkdownTextBlock.ResolveImagePath("%5c%5cserver%5cshare%5ca.png", temp.Path), "编码后 UNC 也不自动访问");
        AssertEx.Equal<string?>(null, MarkdownTextBlock.ResolveImagePath("javascript:foo.png", temp.Path), "禁用其他协议");
        AssertEx.Equal("https://example.com/a.png", MarkdownTextBlock.ResolveImagePath("https://example.com/a.png", temp.Path), "远程地址仅作为显式加载目标");
    }
}
