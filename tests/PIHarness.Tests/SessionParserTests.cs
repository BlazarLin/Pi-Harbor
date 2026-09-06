// Created: 2026-09-06
// Purpose: Verify tolerant parsing of pi JSONL session indexes.

using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class SessionParserTests
{
    [TestCase("TEST-03A", "会话名称优先于第一条用户消息")]
    public static async Task SessionNameOverridesFirstMessageAsync()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteSession(
            "named.jsonl",
            """
            {"type":"session","version":3,"id":"session-1","timestamp":"2026-09-01T08:00:00Z","cwd":"G:\\Code\\Demo"}
            {"type":"message","id":"11111111","parentId":null,"timestamp":"2026-09-01T08:00:01Z","message":{"role":"user","content":"第一条需求"}}
            {broken-json
            {"type":"session_info","id":"22222222","parentId":"11111111","timestamp":"2026-09-01T08:00:02Z","name":"正式会话名"}
            """);

        var result = await SessionParser.ParseAsync(path, CancellationToken.None);

        AssertEx.True(result.IsValid, "包含单行损坏记录的会话仍应建立索引");
        AssertEx.NotNull(result.Session, "有效会话必须返回摘要");
        AssertEx.Equal("正式会话名", result.Session!.Title, "应使用最新会话名称");
        AssertEx.Equal(1, result.Warnings.Count, "应记录一条损坏行警告");
    }

    [TestCase("TEST-03B", "没有会话名称时使用第一条用户文本")]
    public static async Task FirstUserTextBecomesTitleAsync()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteSession(
            "first-message.jsonl",
            """
            {"type":"session","version":3,"id":"session-2","timestamp":"2026-09-02T08:00:00Z","cwd":"E:\\Vision"}
            {"type":"message","id":"11111111","parentId":null,"timestamp":"2026-09-02T08:00:01Z","message":{"role":"user","content":[{"type":"text","text":"  分析  图像\n异常  "}]}}
            """);

        var result = await SessionParser.ParseAsync(path, CancellationToken.None);

        AssertEx.Equal("分析 图像 异常", result.Session!.Title, "标题应压缩连续空白");
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PIHarness.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteSession(string fileName, string content, string? childDirectory = null)
    {
        var targetDirectory = childDirectory is null ? Path : System.IO.Path.Combine(Path, childDirectory);
        Directory.CreateDirectory(targetDirectory);
        var filePath = System.IO.Path.Combine(targetDirectory, fileName);
        File.WriteAllText(filePath, content.ReplaceLineEndings("\n") + "\n");
        return filePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
