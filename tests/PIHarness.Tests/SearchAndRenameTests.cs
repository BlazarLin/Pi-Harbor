using System.Text.Json;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;
using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class SearchAndRenameTests
{
    [TestCase("TEST-33A", "全文搜索覆盖大记录中间、转义中文、工具和思考，忽略图片及协议字段")]
    public static async Task SearchesActualText()
    {
        using var directory = new TemporaryDirectory();
        var session = Write(directory, "one", "Original title", [
            Message("assistant", new[] { new { type = "text", text = new string('a', 1100000) + "中间关键词 Needle" + new string('b', 1100000) } }),
            Message("toolResult", "工具报告 MATCHTOOL"),
            Message("assistant", new[] { new { type = "thinking", thinking = "思考 THINKMATCH" } }),
            Message("user", new[] { new { type = "image", data = "NOT_TEXT_MATCH", mimeType = "image/png" } }),
        ]);
        var result = await SessionSearch.FindAsync([session], "中间关键词 needle", default);
        AssertEx.Equal(1, result.Matches.Count, "JSON 转义的中文及大小写不敏感全文匹配");
        AssertEx.True(result.Matches[0].Preview.Length < 180, "只保留有界片段，不能持有大段正文");
        AssertEx.Equal("工具输出", (await SessionSearch.FindAsync([session], "matchtool", default)).Matches[0].Source, "工具正文");
        AssertEx.Equal("思考", (await SessionSearch.FindAsync([session], "thinkmatch", default)).Matches[0].Source, "思考正文");
        AssertEx.Equal(0, (await SessionSearch.FindAsync([session], "NOT_TEXT_MATCH", default)).Matches.Count, "不能命中图片数据");
        AssertEx.Equal(0, (await SessionSearch.FindAsync([session], "mimeType", default)).Matches.Count, "不能命中协议字段");
    }

    [TestCase("TEST-33B", "全局搜索容错、取消、排序和结果上限")]
    public static async Task HandlesSearchBoundaries()
    {
        using var directory = new TemporaryDirectory();
        var session = Write(directory, "valid", "title", ["{bad json", "42", Message("user", "contains needle")]);
        var missing = session with { SessionPath = Path.Combine(directory.Path, "missing.jsonl") };
        var result = await SessionSearch.FindAsync([missing, session], "needle", default);
        AssertEx.Equal(1, result.Matches.Count, "损坏内容后仍继续搜索");
        AssertEx.Equal(1, result.UnreadableFiles, "报告无法读取文件");
        AssertEx.Equal(2, result.InvalidRecords, "报告损坏和无效记录");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await AssertEx.ThrowsAsync<OperationCanceledException>(() => SessionSearch.FindAsync([session], "needle", cts.Token), "支持取消");
        var many = Enumerable.Range(0, 103).Select(i => session with { Title = "needle " + i, LastActivityAt = session.LastActivityAt.AddMinutes(i) });
        result = await SessionSearch.FindAsync(many, "needle", default);
        AssertEx.True(result.Truncated, "不能静默遗漏超出上限的结果");
        AssertEx.Equal(100, result.Matches.Count, "结果数量有界");
        AssertEx.Equal("needle 102", result.Matches[0].Session.Title, "最新会话优先");
    }

    [TestCase("TEST-33C", "重命名持久化、恢复默认，原会话字节和活动时间保持一致")]
    public static async Task NamesPersistWithoutModifyingPi()
    {
        using var directory = new TemporaryDirectory();
        var session = Write(directory, "named", "Original title", [Message("user", "Original title")]);
        var original = await File.ReadAllBytesAsync(session.SessionPath);
        var modified = File.GetLastWriteTimeUtc(session.SessionPath);
        var namesDir = Path.Combine(directory.Path, "names");
        var store = new SessionNameStore(namesDir);
        await store.SaveAsync(session.SessionPath, "  自定义 <名称> & \"引号\"  ");
        using var catalog = new SessionCatalog(directory.Path, new SessionNameStore(namesDir));
        var item = (await catalog.ScanCurrentAsync(default)).Projects[0].Sessions[0];
        AssertEx.Equal("自定义 <名称> & \"引号\"", item.Title, "新实例读取持久化名称");
        var afterRename = await File.ReadAllBytesAsync(session.SessionPath);
        AssertEx.True(original.SequenceEqual(afterRename), "不改写 Pi 会话");
        AssertEx.Equal(modified, File.GetLastWriteTimeUtc(session.SessionPath), "重命名不伪造最近交流时间");
        await AssertEx.ThrowsAsync<ArgumentException>(() => store.SaveAsync(session.SessionPath, "  "), "拒绝空名称");
        await AssertEx.ThrowsAsync<ArgumentException>(() => store.SaveAsync(session.SessionPath, new string('x', 121)), "拒绝超长名称");
        await AssertEx.ThrowsAsync<ArgumentException>(() => store.SaveAsync(session.SessionPath, "a\nb"), "拒绝多行名称");
        await store.SaveAsync(session.SessionPath, null);
        AssertEx.Equal("Original title", (await catalog.ScanCurrentAsync(default)).Projects[0].Sessions[0].Title, "恢复 Pi 默认名称");
    }

    [TestCase("TEST-33D", "搜索切词和清空不会被过期结果覆盖，重命名更新当前标题与结果")]
    public static async Task CoordinatesSearchAndRename()
    {
        using var directory = new TemporaryDirectory();
        var first = Write(directory, "first", "First", [Message("user", "First apple")]);
        Write(directory, "second", "Second", [Message("user", "Second banana")]);
        await using var vm = MainViewModelTests.CreateViewModel(directory.Path, out var clients, namesDirectory: Path.Combine(directory.Path, "names"));
        await vm.InitializeAsync();
        vm.SearchText = "apple";
        var stale = vm.SearchNowAsync();
        vm.SearchText = "banana";
        await vm.SearchNowAsync(); await stale;
        AssertEx.Equal(1, vm.SearchResults.Count, "只保留最新查询结果");
        AssertEx.Equal("Second banana", vm.SearchResults[0].Title, "过期 apple 不得覆盖 banana");
        vm.SearchText = "apple";
        var clearing = vm.SearchNowAsync();
        vm.SearchText = ""; await clearing;
        AssertEx.False(vm.IsSearchActive, "清空返回普通会话树");
        AssertEx.Equal(0, vm.SearchResults.Count, "取消后不会回填旧结果");
        AssertEx.Equal(0, clients.Count, "搜索不启动 Pi");
        await vm.OpenSessionAsync(first);
        await vm.RenameSessionAsync(first, "Renamed project");
        AssertEx.Equal("Renamed project", vm.CurrentTitle, "当前对话标题同步更新");
        vm.SearchText = "renamed"; await vm.SearchNowAsync();
        AssertEx.Equal(1, vm.SearchResults.Count, "可按自定义名称搜索");
        AssertEx.Equal(1, clients.Count, "重命名不创建其他 Pi 进程");
    }

    private static string Message(string role, object content) => JsonSerializer.Serialize(new { type = "message", id = Guid.NewGuid().ToString("N"), timestamp = DateTimeOffset.UtcNow, message = new { role, content } });
    private static SessionSummary Write(TemporaryDirectory directory, string file, string title, string[] records)
    {
        var now = DateTimeOffset.UtcNow;
        var path = directory.WriteSession(file + ".jsonl", JsonSerializer.Serialize(new { type = "session", id = file, cwd = directory.Path, timestamp = now }) + "\n" + string.Join("\n", records));
        return new SessionSummary(path, directory.Path, title, now, now);
    }
}
