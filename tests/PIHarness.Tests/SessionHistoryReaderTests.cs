// Created: 2026-09-06
// Purpose: Verify lightweight active-branch history loading without retaining image payloads.

using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class SessionHistoryReaderTests
{
    [TestCase("TEST-15A", "本地历史只还原最后活动分支并隔离损坏行")]
    public static async Task ReadsOnlyActiveBranchAsync()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteSession(
            "branch.jsonl",
            """
            {"type":"session","version":3,"id":"session-1","timestamp":"2026-09-06T00:00:00Z","cwd":"G:\\Code\\PI-Harness"}
            {"type":"message","id":"root","parentId":null,"timestamp":"2026-09-06T00:00:01Z","message":{"role":"user","content":"根问题"}}
            {"type":"message","id":"answer","parentId":"root","timestamp":"2026-09-06T00:00:02Z","message":{"role":"assistant","content":[{"type":"text","text":"根回答"}]}}
            {"type":"message","id":"abandoned-user","parentId":"answer","timestamp":"2026-09-06T00:00:03Z","message":{"role":"user","content":"废弃分支问题"}}
            {"type":"message","id":"abandoned-answer","parentId":"abandoned-user","timestamp":"2026-09-06T00:00:04Z","message":{"role":"assistant","content":[{"type":"text","text":"废弃分支回答"}]}}
            {broken-json
            {"type":"message","id":"active-user","parentId":"answer","timestamp":"2026-09-06T00:00:05Z","message":{"role":"user","content":[{"type":"text","text":"活动分支问题"},{"type":"image","data":"IMAGE_BASE64_MUST_NOT_SURVIVE","mimeType":"image/png"}]}}
            {"type":"message","id":"active-answer","parentId":"active-user","timestamp":"2026-09-06T00:00:06Z","message":{"role":"assistant","content":[{"type":"thinking","thinking":"活动思考"},{"type":"text","text":"活动回答"},{"type":"toolCall","id":"tool-1","name":"read","arguments":{"path":"demo.txt"}}]}}
            {"type":"message","id":"tool-result","parentId":"active-answer","timestamp":"2026-09-06T00:00:07Z","message":{"role":"toolResult","toolCallId":"tool-1","toolName":"read","content":[{"type":"text","text":"工具文字"},{"type":"image","data":"TOOL_IMAGE_BASE64_MUST_NOT_SURVIVE","mimeType":"image/png"}],"isError":false}}
            {"type":"compaction","id":"compact","parentId":"tool-result","timestamp":"2026-09-06T00:00:08Z","summary":"压缩摘要"}
            """);

        var snapshot = await SessionHistoryReader.ReadAsync(path, CancellationToken.None);

        AssertEx.Equal(1, snapshot.Warnings.Count, "损坏行应被隔离并记录");
        AssertEx.True(snapshot.Items.Any(item => item.Kind == ChatItemKind.User && item.Text == "根问题"), "应保留活动分支根问题");
        AssertEx.True(snapshot.Items.Any(item => item.Kind == ChatItemKind.User && item.Text == "活动分支问题" + Environment.NewLine + "[图片附件：历史视图暂不加载原图]"), "应保留活动分支问题与轻量图片提示");
        AssertEx.True(snapshot.Items.Any(item => item.Kind == ChatItemKind.Assistant && item.Text == "活动回答"), "应保留活动分支回答");
        AssertEx.True(snapshot.Items.Any(item => item.Kind == ChatItemKind.Thinking && item.Text == "活动思考"), "应保留思考内容");
        AssertEx.True(snapshot.Items.Any(item => item.Kind == ChatItemKind.Tool && item.Text == "工具文字"), "应保留工具文字");
        AssertEx.True(snapshot.Items.Any(item => item.Kind == ChatItemKind.System && item.Text == "压缩摘要"), "应保留压缩摘要");
        AssertEx.False(snapshot.Items.Any(item => item.Text.Contains("废弃分支", StringComparison.Ordinal)), "不得显示废弃分支");
        AssertEx.False(snapshot.Items.Any(item => item.Text.Contains("BASE64", StringComparison.Ordinal)), "不得把图片数据带入显示模型");
    }

    [TestCase("TEST-15B", "超大图片字段不进入历史显示文本")]
    public static async Task SkipsLargeImagePayloadAsync()
    {
        using var directory = new TemporaryDirectory();
        var imageData = new string('A', 2 * 1024 * 1024);
        var path = directory.WriteSession(
            "large-image.jsonl",
            """
            {"type":"session","version":3,"id":"session-2","timestamp":"2026-09-06T00:00:00Z","cwd":"G:\\Code\\PI-Harness"}
            {"type":"message","id":"root","parentId":null,"timestamp":"2026-09-06T00:00:01Z","message":{"role":"toolResult","toolCallId":"tool-2","toolName":"view_image","content":[{"type":"text","text":"图片读取完成"},{"type":"image","data":"IMAGE_DATA","mimeType":"image/png"}],"isError":false}}
            """.Replace("IMAGE_DATA", imageData, StringComparison.Ordinal));

        var snapshot = await SessionHistoryReader.ReadAsync(path, CancellationToken.None);

        AssertEx.Equal(1, snapshot.Items.Count, "大图片工具结果应只生成一个轻量显示项");
        AssertEx.Equal("图片读取完成", snapshot.Items[0].Text, "只保留工具文字摘要");
        AssertEx.True(snapshot.Items[0].Text.Length < 1024, "显示项不得持有图片负载");
    }

    [TestCase("TEST-22A", "多次助手与工具用量归入同一轮并累计耗时与 token")]
    public static async Task AggregatesPerTurnMetricsAsync()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteSession(
            "turn-metrics.jsonl",
            """
            {"type":"session","version":3,"id":"metrics","timestamp":"2026-09-07T00:00:00Z","cwd":"G:\\Code\\PI-Harness"}
            {"type":"message","id":"user-1","parentId":null,"timestamp":"2026-09-07T00:00:01Z","message":{"role":"user","content":"第一轮","timestamp":1788739201000}}
            {"type":"message","id":"assistant-1","parentId":"user-1","timestamp":"2026-09-07T00:00:04Z","message":{"role":"assistant","content":[{"type":"toolCall","id":"tool-1","name":"read","arguments":{}}],"usage":{"input":100,"output":20,"cacheRead":30,"cacheWrite":0,"reasoning":5,"totalTokens":150},"stopReason":"toolUse","timestamp":1788739201100}}
            {"type":"message","id":"tool-1-result","parentId":"assistant-1","timestamp":"2026-09-07T00:00:06Z","message":{"role":"toolResult","toolCallId":"tool-1","toolName":"read","content":[{"type":"text","text":"结果"}],"usage":{"input":10,"output":5,"cacheRead":0,"cacheWrite":0,"reasoning":0,"totalTokens":15},"timestamp":1788739204000}}
            {"type":"message","id":"assistant-2","parentId":"tool-1-result","timestamp":"2026-09-07T00:00:09Z","message":{"role":"assistant","content":[{"type":"text","text":"完成"}],"usage":{"input":120,"output":50,"cacheRead":15,"cacheWrite":0,"reasoning":8,"totalTokens":185},"stopReason":"stop","timestamp":1788739206000}}
            {"type":"message","id":"user-2","parentId":"assistant-2","timestamp":"2026-09-07T00:01:00Z","message":{"role":"user","content":"第二轮","timestamp":1788739260000}}
            {"type":"message","id":"assistant-3","parentId":"user-2","timestamp":"2026-09-07T00:01:02Z","message":{"role":"assistant","content":[{"type":"text","text":"第二轮完成"}],"usage":{"input":20,"output":10,"cacheRead":0,"cacheWrite":0,"reasoning":0,"totalTokens":30},"stopReason":"stop","timestamp":1788739260100}}
            """);

        var snapshot = await SessionHistoryReader.ReadAsync(path, CancellationToken.None);
        var metrics = snapshot.Items.Where(item => item.Kind == ChatItemKind.Metrics).ToArray();

        AssertEx.Equal(2, metrics.Length, "两个用户问题必须生成两个独立轮次统计");
        AssertEx.Equal(350L, metrics[0].Metrics!.TotalTokens, "第一轮必须累计 assistant 与 toolResult 的 token");
        AssertEx.Equal(TimeSpan.FromSeconds(8), metrics[0].Metrics!.Duration, "第一轮耗时必须从用户开始到最终助手完成");
        AssertEx.Equal(30L, metrics[1].Metrics!.TotalTokens, "第二轮 token 不得混入第一轮");
        AssertEx.True(metrics[0].Text.Contains("输入 230", StringComparison.Ordinal), "统计行应显示输入分项");
    }
}
