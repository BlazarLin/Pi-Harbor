// Created: 2026-09-06
// Function: Verify the executable exposes a deterministic large-session scroll QA path.
// Purpose: Keep visual stability measurable with the user's real read-only session data.

namespace PIHarness.Tests;

internal static class ScrollQaContractTests
{
    [TestCase("TEST-21", "滚动 QA 输出方向、耗时和静置双帧稳定性证据")]
    public static void ScrollQaEntryPointExists()
    {
        var path = Path.Combine(
            Path.GetFullPath(Environment.CurrentDirectory),
            "src",
            "PIHarness.App",
            "MainWindow.xaml.cs");
        var source = File.ReadAllText(path);

        AssertEx.True(source.Contains("--qa-scroll-capture-dir", StringComparison.Ordinal), "必须提供大会话滚动 QA 参数");
        AssertEx.True(source.Contains("StableFrames", StringComparison.Ordinal), "必须比较静置双帧");
        AssertEx.True(source.Contains("StepElapsedMilliseconds", StringComparison.Ordinal), "必须记录每次滚动响应耗时");
        AssertEx.True(source.Contains("TEST-UI-06", StringComparison.Ordinal), "必须自动验收五个位置的滑块长度稳定性");
        AssertEx.True(source.Contains("ThumbLength", StringComparison.Ordinal), "每个滚动步骤必须记录滑块长度");
        AssertEx.True(source.Contains("thumb=", StringComparison.Ordinal), "滚动报告必须输出可复核的滑块长度");
        AssertEx.True(source.Contains("TEST-UI-07", StringComparison.Ordinal), "必须验收最新内容底边对齐");
        AssertEx.True(source.Contains("TEST-UI-08", StringComparison.Ordinal), "必须验收微量上滑的像素连续性");
        AssertEx.True(source.Contains("BottomGap", StringComparison.Ordinal), "报告必须记录内容底部空隙");
        AssertEx.True(source.Contains("SmallScrollDelta", StringComparison.Ordinal), "报告必须记录微量滚动距离");
        AssertEx.True(source.Contains("CaptureElement(MessageList", StringComparison.Ordinal), "双帧稳定性只能比较消息区，不能受模型框异步状态干扰");
    }
}
