// 创建时间：2026-09-07
// 功能：构造在系统文件管理器中打开目录的启动请求（Windows: explorer.exe；Linux: xdg-open）。
// 目的：隔离 shell 路径处理，便于直接测试跨平台行为。

using System;
using System.Diagnostics;
using System.IO;

namespace PIHarness.App.Presentation;

public static class FolderLauncher
{
    public static ProcessStartInfo CreateStartInfo(string directory) =>
        CreateStartInfo(directory, OperatingSystem.IsWindows());

    /// <summary>按平台选择文件管理器命令；bWindows 由调用方显式传入以便测试两个分支。</summary>
    internal static ProcessStartInfo CreateStartInfo(string directory, bool bWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var startInfo = new ProcessStartInfo
        {
            // Debian/Ubuntu 用 xdg-open 交给桌面环境决定文件管理器。
            FileName = bWindows ? "explorer.exe" : "xdg-open",
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(directory));
        return startInfo;
    }
}
