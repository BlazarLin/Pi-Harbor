using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.Tests;

internal static class ComposerTests
{
    [TestCase("TEST-31A", "补全只在有效位置触发且保留中文、路径与命令边界")]
    public static void CompletionBoundaries()
    {
        AssertEx.True(ComposerCompletion.GetQuery("mail@example.com", 16) is null, "邮箱不触发文件菜单");
        AssertEx.True(ComposerCompletion.GetQuery("解释 /skill", 9) is null, "正文中的斜线不执行命令");
        AssertEx.Equal("skill:rev", ComposerCompletion.GetQuery("/skill:rev", 10)?.Filter, "保留 skill 前缀");
        var query = ComposerCompletion.GetQuery("查看 @src/main.cs 然后说明", 15);
        AssertEx.Equal(3, query?.Start, "替换范围从 @ 开始");
        AssertEx.Equal("src/main.cs", query?.Filter, "从光标处而非整段文本匹配");
    }

    [TestCase("TEST-31B", "文件补全跳过构建目录并支持空格路径、取消和结果上限")]
    public static async Task FileCompletion()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "node_modules"));
        File.WriteAllText(Path.Combine(directory.Path, "node_modules", "ignore.cs"), "");
        File.WriteAllText(Path.Combine(directory.Path, "中文 file.cs"), "");
        var matches = ComposerCompletion.FindFiles(directory.Path, ".cs", CancellationToken.None);
        AssertEx.Equal(1, matches.Count, "忽略依赖目录");
        AssertEx.Equal("\"中文 file.cs\" ", matches[0].InsertText, "空格文件名引用必须完整");
        for (var index = 0; index < 60; index++) File.WriteAllText(Path.Combine(directory.Path, $"file{index}.txt"), "");
        AssertEx.Equal(40, ComposerCompletion.FindFiles(directory.Path, "", CancellationToken.None).Count, "结果应有上限");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await AssertEx.ThrowsAsync<OperationCanceledException>(() => Task.Run(() => ComposerCompletion.FindFiles(directory.Path, "", cts.Token)), "过期查询应取消");
    }

    [TestCase("TEST-31C", "发送拒绝保留完整草稿，重新打开后可恢复")]
    public static async Task RejectedPromptKeepsDraft()
    {
        using var directory = new TemporaryDirectory();
        await using var vm = MainViewModelTests.CreateViewModel(directory.Path, out _);
        await vm.CreateSessionAsync(directory.Path);
        vm.InputText = "拒绝发送";
        await vm.SendAsync();
        AssertEx.Equal("拒绝发送", vm.InputText, "RPC 拒绝不能清空输入");
        AssertEx.Equal(ChatSessionState.Faulted, vm.State, "发送未确认必须核对会话");
        AssertEx.False(vm.Messages.Any(item => item.Kind == ChatItemKind.User), "失败消息不能伪装成发送成功");
        AssertEx.True(vm.CanEditComposer, "错误后恢复输入");
        AssertEx.True(vm.CanReconnect, "失败后提供重新连接入口");
        await vm.ReconnectAsync();
        AssertEx.Equal("拒绝发送", vm.InputText, "重新连接恢复草稿");
        AssertEx.True(vm.CanSend, "恢复后允许发送");
    }

    [TestCase("TEST-31D", "不同会话草稿隔离且无生成的扩展命令恢复就绪")]
    public static async Task DraftsAndCommands()
    {
        using var directory = new TemporaryDirectory();
        await using var vm = MainViewModelTests.CreateViewModel(directory.Path, out _);
        var first = new SessionSummary(Path.Combine(directory.Path, "first.jsonl"), directory.Path, "一", DateTimeOffset.Now, DateTimeOffset.Now);
        var second = first with { SessionPath = Path.Combine(directory.Path, "second.jsonl") };
        await vm.OpenSessionAsync(first);
        vm.InputText = "第一份草稿";
        await vm.OpenSessionAsync(second);
        AssertEx.Equal("", vm.InputText, "新会话不携带旧草稿");
        vm.InputText = "第二份草稿";
        await vm.OpenSessionAsync(first);
        AssertEx.Equal("第一份草稿", vm.InputText, "返回后恢复对应草稿");
        AssertEx.True(vm.SlashCommands.Any(item => item.Label == "/skill:review"), "从 RPC 加载 skill");
        vm.InputText = "/local-command";
        await vm.SendAsync();
        AssertEx.Equal(ChatSessionState.Ready, vm.State, "未启动 agent 的命令不应卡在生成中");
    }

    [TestCase("TEST-31E", "图片按 Pi images 协议发送且 Base64 不进入消息正文")]
    public static async Task ImageProtocol()
    {
        using var directory = new TemporaryDirectory();
        await using var vm = MainViewModelTests.CreateViewModel(directory.Path, out var clients);
        await vm.CreateSessionAsync(directory.Path);
        var response = await clients[0].SendPromptAsync("验证图片", [new PromptImage("aW1hZ2U=", "image/png")], CancellationToken.None);
        AssertEx.True(response.GetProperty("success").GetBoolean(), "图片字段必须通过实际 stdin/stdout 往返校验");
    }

    [TestCase("TEST-31F", "历史纯图片消息保留附件提示且不持有 Base64")]
    public static async Task HistoricalImagesRemainVisible()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteSession("image.jsonl", """
            {"type":"message","id":"image-user","parentId":null,"message":{"role":"user","content":[{"type":"image","data":"do-not-display","mimeType":"image/png"}]}}
            """);
        var history = await PIHarness.Core.Sessions.SessionHistoryReader.ReadAsync(path, CancellationToken.None);
        AssertEx.True(history.Items.Single(item => item.Kind == ChatItemKind.User).Text.Contains("图片附件"), "纯图片历史不能消失");
        AssertEx.False(history.Items[0].Text.Contains("do-not-display"), "保持轻量历史边界");
    }

    [TestCase("TEST-31G", "先输入再选择项目时保留首次草稿")]
    public static async Task InitialDraftSurvivesProjectSelection()
    {
        using var directory = new TemporaryDirectory();
        await using var vm = MainViewModelTests.CreateViewModel(directory.Path, out _);
        vm.InputText = "先写好问题";
        await vm.CreateSessionAsync(directory.Path);
        AssertEx.Equal("先写好问题", vm.InputText, "选择首个项目不能丢失已有输入");
    }
}
