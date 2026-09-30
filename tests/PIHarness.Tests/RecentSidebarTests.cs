using System.Text.Json;
using PIHarness.App.ViewModels;

namespace PIHarness.Tests;

internal static class RecentSidebarTests
{
    [TestCase("TEST-42A", "最近列表跨文件夹取十条，保留全部索引，同步归档、名称和活动时间")]
    public static async Task RecentTenAsync()
    {
        using var temp = new TemporaryDirectory();
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 12; i++)
        {
            var cwd = Path.Combine(temp.Path, "project-" + i % 2);
            Directory.CreateDirectory(cwd);
            var timestamp = now.AddMinutes(-i - 2);
            temp.WriteSession($"recent-{i:D2}.jsonl",
                JsonSerializer.Serialize(new { type = "session", version = 3, cwd, timestamp }) + "\n" +
                JsonSerializer.Serialize(new { type = "message", id = "u", timestamp, message = new { role = "user", content = $"对话 {i}" } }) + "\n");
        }
        await using var vm = MainViewModelTests.CreateViewModel(temp.Path, out _);
        await vm.InitializeAsync();
        AssertEx.Equal(12, vm.TotalSessionCount, "最近区不限制本机索引");
        AssertEx.Equal(10, vm.SidebarRecentSessions.Count, "最多十条");
        AssertEx.True(ReferenceEquals(vm.RecentSessionGroup, vm.SidebarGroups[0]), "最近分组在同一列表顶部");
        AssertEx.Equal(3, vm.SidebarGroups.Count, "最近分组与两个文件夹处于同一级");
        vm.RecentSessionGroup.IsExpanded = false;
        AssertEx.Equal("对话 0", vm.SidebarRecentSessions[0].Title, "跨文件夹倒序");
        AssertEx.Equal("对话 9", vm.SidebarRecentSessions[9].Title, "最旧两条不进快捷区");
        var first = vm.SidebarRecentSessions[0];
        AssertEx.True(vm.Projects.SelectMany(project => project.Sessions).Any(item => ReferenceEquals(first, item)), "两个区域共享状态");
        first.UpdateRelativeActivity(now.AddMinutes(3));
        AssertEx.True(first.ActivityToolTip.Contains("距上次对话：5 分钟前"), "悬停相对时间刷新");
        first.UpdateTitle("新名称");
        AssertEx.True(first.ActivityToolTip.StartsWith("新名称"), "悬停同步名称");
        await vm.SetArchivedAsync([first.Session], true);
        AssertEx.Equal(10, vm.SidebarRecentSessions.Count, "归档后补入下一条");
        AssertEx.Equal("对话 1", vm.SidebarRecentSessions[0].Title, "默认不显示已归档");
        vm.SessionFilter = 1;
        AssertEx.Equal(1, vm.SidebarRecentSessions.Count, "跟随归档范围");
        vm.SessionFilter = 2;
        AssertEx.Equal(10, vm.SidebarRecentSessions.Count, "全部范围仍限制十条");
        File.AppendAllText(Path.Combine(temp.Path, "recent-11.jsonl"),
            JsonSerializer.Serialize(new { type = "message", id = "a", timestamp = now, message = new { role = "assistant", content = "最新结果" } }) + "\n");
        await vm.RefreshCatalogAsync();
        AssertEx.Equal("对话 11", vm.SidebarRecentSessions[0].Title, "终端追加后重新排序");
        AssertEx.False(vm.RecentSessionGroup.IsExpanded, "刷新、归档和筛选不重置用户折叠选择");
    }
}
