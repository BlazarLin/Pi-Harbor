// Created: 2026-09-06
// Function: Verify detailed-conversation scroll intent without driving WPF input.
// Purpose: Prevent layout changes from taking control away from a user reading history.

using PIHarness.App.Presentation;

namespace PIHarness.Tests;

internal static class ChatScrollCoordinatorTests
{
    [TestCase("TEST-18A", "用户上滚立即停止跟随，返回底部后恢复跟随")]
    public static void UserIntentControlsFollowMode()
    {
        var coordinator = new ChatScrollCoordinator(80);

        coordinator.OnUserWheel(120);
        AssertEx.False(coordinator.IsFollowingLatest, "向上滚轮后必须立即停止自动回底");

        coordinator.OnViewportPositionChanged(20);
        AssertEx.True(coordinator.IsFollowingLatest, "进入底部阈值后必须恢复跟随");
    }

    [TestCase("TEST-18B", "内容高度变化不会覆盖历史阅读意图")]
    public static void LayoutChangesKeepExistingIntent()
    {
        var coordinator = new ChatScrollCoordinator(80);
        coordinator.OnUserWheel(120);

        AssertEx.False(coordinator.ShouldFollowExtentChange, "历史阅读时布局变化不得触发滚到底部");

        coordinator.ReturnToLatest();
        AssertEx.True(coordinator.ShouldFollowExtentChange, "显式回到最新后布局变化应继续跟随");

        coordinator.Reset();
        AssertEx.True(coordinator.IsFollowingLatest, "切换会话后应从最新消息开始");
    }
}
