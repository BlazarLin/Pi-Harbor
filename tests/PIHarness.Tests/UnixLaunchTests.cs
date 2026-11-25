// 创建时间：2026-09-12
// 功能：验证跨平台前端使用的 Unix 发现/启动分支：PATH 查找、同名命令过滤与直接 exec。
// 目的：保证 Ubuntu 桌面启动时不会把系统自带的 GNU pi 误当成 Pi 编码代理。

using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class UnixLaunchTests
{
    [TestCase("TEST-51A", "Unix 下按 PATH 查找 pi 命令并直接 exec 启动")]
    public static void UnixBranchFindsPiAndExecutesDirectly()
    {
        PiProcessLocator.ForceUnixMode = true;
        // 清空固定候选目录，避免本机真实安装的 pi 抢先命中，使用例只验证 PATH 分支。
        PiProcessLocator.UnixFallbackDirectoryOverride = [];
        try
        {
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            using var directory = new TemporaryDirectory();
            var cwd = Path.Combine(directory.Path, "project");
            Directory.CreateDirectory(cwd);
            var binDir = Path.Combine(directory.Path, "bin");
            Directory.CreateDirectory(binDir);
            var piCommand = Path.Combine(binDir, "pi");
            File.WriteAllText(piCommand, "#!/bin/sh\necho 1.9.0\nexit 0\n");
            MakeExecutable(piCommand);

            // 替换 PATH 整个内容：机器上真实安装的 pi 不能影响本用例。
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
            PiProcessLocator.UnixFallbackDirectoryOverride = null;
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

    [TestCase("TEST-51C", "Unix 下跳过同名的 GNU pi 并选择真正的 Pi 编码代理")]
    public static void UnixBranchSkipsForeignPiCommand()
    {
        PiProcessLocator.ForceUnixMode = true;
        PiProcessLocator.UnixFallbackDirectoryOverride = [];
        try
        {
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            using var directory = new TemporaryDirectory();
            var foreignDir = Path.Combine(directory.Path, "usr-bin");
            var agentDir = Path.Combine(directory.Path, "npm-bin");
            Directory.CreateDirectory(foreignDir);
            Directory.CreateDirectory(agentDir);
            var foreignPi = Path.Combine(foreignDir, "pi");
            var agentPi = Path.Combine(agentDir, "pi");
            // 模拟 Debian/Ubuntu 自带的 GNU pi：同名但输出自有版本格式。
            File.WriteAllText(foreignPi, "#!/bin/sh\necho 'pi (CLN 1.3.7)'\nexit 0\n");
            File.WriteAllText(agentPi, "#!/bin/sh\necho 1.6.0\nexit 0\n");
            MakeExecutable(foreignPi);
            MakeExecutable(agentPi);

            Environment.SetEnvironmentVariable("PATH", foreignDir + Path.PathSeparator + agentDir);
            try
            {
                var located = PiProcessLocator.Find();
                AssertEx.True(located.Found, "应跳过 GNU pi 后找到真正的 pi");
                AssertEx.Equal(agentPi, located.PiCommandPath, "必须返回能输出版本号的 Pi 编码代理");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", foreignDir);
                var onlyForeign = PiProcessLocator.Find();
                AssertEx.False(onlyForeign.Found, "只有同名 GNU pi 时不得误判为已安装");
                AssertEx.True(onlyForeign.ErrorMessage!.Contains("圆周率", StringComparison.Ordinal), "错误信息必须说明同名命令不是 Pi 编码代理");
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }
        finally
        {
            PiProcessLocator.UnixFallbackDirectoryOverride = null;
            PiProcessLocator.ForceUnixMode = false;
        }
    }

    /// <summary>Windows 下没有可执行位，跳过权限设置；Unix 下必须让脚本可执行。</summary>
    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                   UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
