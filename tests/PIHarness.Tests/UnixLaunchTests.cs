// Created: 2026-09-12
// Purpose: Verify the Unix discovery/launch branch used by the cross-platform frontend,
//          including PATH-based pi lookup and direct exec (no cmd.exe intermediary).

using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class UnixLaunchTests
{
    [TestCase("TEST-51A", "Unix 下按 PATH 查找 pi 命令并直接 exec 启动")]
    public static void UnixBranchFindsPiAndExecutesDirectly()
    {
        PiProcessLocator.ForceUnixMode = true;
        try
        {
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            using var directory = new TemporaryDirectory();
            var cwd = Path.Combine(directory.Path, "project");
            Directory.CreateDirectory(cwd);
            var binDir = Path.Combine(directory.Path, "bin");
            Directory.CreateDirectory(binDir);
            var piCommand = Path.Combine(binDir, "pi");
            File.WriteAllText(piCommand, "#!/bin/sh\nexit 0\n");

            // Replace PATH entirely: the machine PATH may already expose a real pi.
            Environment.SetEnvironmentVariable("PATH", binDir);
            try
            {
                var located = PiProcessLocator.Find();
                AssertEx.True(located.Found, "应通过 PATH 找到 pi");
                AssertEx.Equal(piCommand, located.PiCommandPath, "定位结果必须指向 PATH 中的 pi");
                AssertEx.True(located.ErrorMessage is null, "成功定位不得携带错误信息");

                var startInfo = PiProcessLocator.CreateStartInfo(
                    located.PiCommandPath!,
                    new PiStartOptions(cwd, SessionPath: Path.Combine(cwd, "会话 1.jsonl"), Offline: true));

                AssertEx.Equal(piCommand, startInfo.FileName, "Unix 分支必须直接 exec pi，不经 cmd/PowerShell 中转");
                AssertEx.True(startInfo.ArgumentList.Contains("--mode"), "必须携带 --mode rpc");
                AssertEx.True(startInfo.ArgumentList.Contains("--approve"), "必须携带 --approve");
                AssertEx.True(startInfo.ArgumentList.Contains("--offline"), "必须携带 --offline");
                AssertEx.True(startInfo.ArgumentList.Any(argument => argument.Contains("会话 1.jsonl", StringComparison.Ordinal)), "会话路径必须作为独立参数传递");
                AssertEx.Equal(cwd, startInfo.WorkingDirectory, "工作目录必须等于用户选择目录");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }
        finally
        {
            PiProcessLocator.ForceUnixMode = false;
        }
    }

    [TestCase("TEST-51B", "Unix 分支找不到 pi 时给出安装指引")]
    public static void UnixBranchReportsMissingPi()
    {
        PiProcessLocator.ForceUnixMode = true;
        try
        {
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", string.Empty);
            // 本机可能已把 pi 装在固定目录，测试期间清空兜底列表才能验证“完全找不到”的分支。
            PiProcessLocator.UnixFallbackDirectoryOverride = [];
            try
            {
                var located = PiProcessLocator.Find();
                AssertEx.False(located.Found, "空 PATH 下不应找到 pi");
                AssertEx.True(located.ErrorMessage!.Contains("npm", StringComparison.Ordinal), "错误信息必须包含安装指引");
            }
            finally
            {
                PiProcessLocator.UnixFallbackDirectoryOverride = null;
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }
        finally
        {
            PiProcessLocator.ForceUnixMode = false;
        }
    }
}
