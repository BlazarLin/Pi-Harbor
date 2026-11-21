// 创建时间：2026-09-12
// 功能：Avalonia 前端入口，支持 --version 供打包与冒烟脚本探测版本。
// 目的：Windows/Linux 共用同一份跨平台桌面入口。

using System;
using System.Linq;
using Avalonia;
using Avalonia.X11;
using PIHarness.App.ViewModels;

namespace PIHarness.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 打包脚本与冒烟测试用 --version 校验产物版本，不启动 UI。
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine($"Pi Harbor {MainViewModel.ApplicationVersion}");
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // X11 的 WM_CLASS 必须与 pi-harbor.desktop 同名，GNOME 才能把运行中的窗口归到快捷启动栏图标。
        if (OperatingSystem.IsLinux())
        {
            builder = builder.With(new X11PlatformOptions { WmClass = "pi-harbor" });
        }

        return builder;
    }
}
