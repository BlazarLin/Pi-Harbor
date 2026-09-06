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
    }
}
