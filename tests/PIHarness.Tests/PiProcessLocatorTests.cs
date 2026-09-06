// Created: 2026-09-06
// Purpose: Verify local pi discovery and safe launch configuration.

using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class PiProcessLocatorTests
{
    [TestCase("TEST-06", "启动参数使用指定项目作为工作目录")]
    public static void UsesSelectedWorkingDirectory()
    {
        var cwd = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PIHarness cwd with spaces"));
        var result = PiProcessLocator.Find();
        AssertEx.True(result.Found, "本机开发环境应能找到 pi.cmd");

        var startInfo = PiProcessLocator.CreateStartInfo(
            result.PiCommandPath!,
            new PiStartOptions(cwd, SessionPath: "G:\\会话 1.jsonl", NoSession: false, Offline: true));

        AssertEx.Equal(cwd, startInfo.WorkingDirectory, "工作目录必须等于用户选择目录");
        AssertEx.True(startInfo.RedirectStandardInput && startInfo.RedirectStandardOutput, "RPC 标准流必须重定向");
        AssertEx.True(startInfo.Arguments.Contains("--session", StringComparison.Ordinal), "启动命令必须包含会话参数");
    }
}
