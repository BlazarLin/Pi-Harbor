// Created: 2026-09-06
// Purpose: Verify safe launch configuration without a machine-wide Pi installation.

using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class PiProcessLocatorTests
{
    [TestCase("TEST-06", "启动参数使用指定项目作为工作目录")]
    public static void UsesSelectedWorkingDirectory()
    {
        using var directory = new TemporaryDirectory();
        var cwd = Path.Combine(directory.Path, "project with spaces");
        Directory.CreateDirectory(cwd);
        var piCommand = Path.Combine(cwd, "pi.cmd");
        File.WriteAllText(piCommand, "@exit /b 0\r\n");

        var startInfo = PiProcessLocator.CreateStartInfo(
            piCommand,
            new PiStartOptions(
                cwd,
                SessionPath: Path.Combine(cwd, "会话 1.jsonl"),
                NoSession: false,
                Offline: true,
                SessionDirectory: Path.Combine(cwd, "sessions")));

        AssertEx.Equal(cwd, startInfo.WorkingDirectory, "工作目录必须等于用户选择目录");
        AssertEx.True(startInfo.Arguments.Contains($"\"{piCommand}\"", StringComparison.Ordinal), "带空格的 Pi 路径必须保留引号");
        AssertEx.True(startInfo.RedirectStandardInput && startInfo.RedirectStandardOutput, "RPC 标准流必须重定向");
        AssertEx.True(startInfo.Arguments.Contains("--session", StringComparison.Ordinal), "启动命令必须包含会话参数");
        AssertEx.True(startInfo.Arguments.Contains("--session-dir", StringComparison.Ordinal), "启动命令必须支持隔离会话目录");
    }
}
