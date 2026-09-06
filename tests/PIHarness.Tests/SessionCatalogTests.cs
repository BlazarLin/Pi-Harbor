// Created: 2026-09-06
// Purpose: Verify project grouping, ordering, and malformed session isolation.

using PIHarness.Core.Sessions;
using System.Text.Json;

namespace PIHarness.Tests;

internal static class SessionCatalogTests
{
    [TestCase("TEST-01", "自动发现全部有效 JSONL 会话")]
    public static async Task DiscoversValidSessionsAsync()
    {
        using var directory = new TemporaryDirectory();
        WriteSimpleSession(directory, "one.jsonl", "G:\\Code\\One", "一", "2026-09-01T08:00:00Z", "p1");
        WriteSimpleSession(directory, "two.jsonl", "G:\\Code\\Two", "二", "2026-09-02T08:00:00Z", "p2");

        var snapshot = await SessionCatalog.ScanAsync(directory.Path, CancellationToken.None);

        AssertEx.Equal(2, snapshot.SessionCount, "应发现两个有效会话");
        AssertEx.Equal(2, snapshot.Projects.Count, "应建立两个项目分组");
    }

    [TestCase("TEST-02", "Windows cwd 大小写只建立一个项目组")]
    public static async Task GroupsCwdCaseInsensitivelyAsync()
    {
        using var directory = new TemporaryDirectory();
        WriteSimpleSession(directory, "upper.jsonl", "G:\\Code\\Vision", "大写路径", "2026-09-01T08:00:00Z", "p1");
        WriteSimpleSession(directory, "lower.jsonl", "g:\\code\\vision", "小写路径", "2026-09-02T08:00:00Z", "p2");

        var snapshot = await SessionCatalog.ScanAsync(directory.Path, CancellationToken.None);

        AssertEx.Equal(1, snapshot.Projects.Count, "大小写变体不应生成重复项目");
        AssertEx.Equal(2, snapshot.Projects[0].Sessions.Count, "两个会话都应保留");
    }

    [TestCase("TEST-03C", "项目和会话按最近活动时间倒序")]
    public static async Task SortsProjectsAndSessionsByActivityAsync()
    {
        using var directory = new TemporaryDirectory();
        WriteSimpleSession(directory, "old.jsonl", "G:\\Code\\A", "旧会话", "2026-09-01T08:00:00Z", "p1");
        WriteSimpleSession(directory, "new.jsonl", "G:\\Code\\A", "新会话", "2026-09-03T08:00:00Z", "p2");
        WriteSimpleSession(directory, "middle.jsonl", "G:\\Code\\B", "中间项目", "2026-09-02T08:00:00Z", "p3");

        var snapshot = await SessionCatalog.ScanAsync(directory.Path, CancellationToken.None);

        AssertEx.Equal("A", snapshot.Projects[0].DisplayName, "最近活动项目应排在最前");
        AssertEx.Equal("新会话", snapshot.Projects[0].Sessions[0].Title, "最近活动会话应排在最前");
    }

    [TestCase("TEST-04", "损坏 JSONL 和孤立 meta 不影响其他会话")]
    public static async Task IsolatesMalformedSessionsAsync()
    {
        using var directory = new TemporaryDirectory();
        WriteSimpleSession(directory, "valid.jsonl", "G:\\Code\\Valid", "有效", "2026-09-01T08:00:00Z", "p1");
        directory.WriteSession("empty.jsonl", string.Empty, "p2");
        directory.WriteSession("broken.jsonl", "{not-json", "p3");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "orphan.meta.json"), "{}");

        var snapshot = await SessionCatalog.ScanAsync(directory.Path, CancellationToken.None);

        AssertEx.Equal(1, snapshot.SessionCount, "只应保留有效会话");
        AssertEx.Equal(2, snapshot.SkippedFileCount, "应跳过两个无效 JSONL");
        AssertEx.True(snapshot.Warnings.Count >= 2, "跳过原因应进入诊断列表");
    }

    private static void WriteSimpleSession(
        TemporaryDirectory directory,
        string fileName,
        string cwd,
        string title,
        string timestamp,
        string childDirectory)
    {
        var header = JsonSerializer.Serialize(new
        {
            type = "session",
            version = 3,
            id = Guid.NewGuid().ToString(),
            timestamp,
            cwd,
        });
        var message = JsonSerializer.Serialize(new
        {
            type = "message",
            id = "11111111",
            parentId = (string?)null,
            timestamp,
            message = new { role = "user", content = title },
        });
        var json = $"{header}\n{message}";
        directory.WriteSession(fileName, json, childDirectory);
    }
}
