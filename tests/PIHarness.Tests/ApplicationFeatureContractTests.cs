// Created: 2026-09-07
// Function: Verify shell launching, application identity and project context-menu contracts.
// Purpose: Prevent unsafe path handling or release branding regressions.

using System.Xml.Linq;
using PIHarness.App.Presentation;
using PIHarness.App.ViewModels;

namespace PIHarness.Tests;

internal static class ApplicationFeatureContractTests
{
    [TestCase("TEST-22C", "项目目录使用独立参数安全打开资源管理器")]
    public static void FolderLauncherUsesArgumentList()
    {
        var path = Path.Combine(Path.GetTempPath(), "PI Harness 路径 & 空格");
        var startInfo = FolderLauncher.CreateStartInfo(path);

        AssertEx.Equal("explorer.exe", startInfo.FileName, "必须直接调用 Windows 资源管理器");
        AssertEx.True(startInfo.UseShellExecute, "资源管理器应按桌面 Shell 方式启动");
        AssertEx.Equal(Path.GetFullPath(path), startInfo.ArgumentList.Single(), "目录必须作为一个独立参数传递");
        AssertEx.True(string.IsNullOrEmpty(startInfo.Arguments), "不得拼接可注入的 Arguments 字符串");
    }

    [TestCase("TEST-22D", "软件图标与 1.1.0 版本号进入窗口和程序集")]
    public static void ApplicationIdentityIsVersionedAndBranded()
    {
        var repoRoot = Path.GetFullPath(Environment.CurrentDirectory);
        var projectPath = Path.Combine(repoRoot, "src", "PIHarness.App", "PIHarness.App.csproj");
        var xamlPath = Path.Combine(repoRoot, "src", "PIHarness.App", "MainWindow.xaml");
        var iconPath = Path.Combine(repoRoot, "src", "PIHarness.App", "Assets", "PI-Harness.ico");
        var project = XDocument.Load(projectPath);
        var values = project.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .ToDictionary(element => element.Name.LocalName, element => element.Value);
        var xaml = File.ReadAllText(xamlPath);

        AssertEx.Equal("1.1.0", values["Version"], "包版本必须为 1.1.0");
        AssertEx.Equal("1.1.0.0", values["FileVersion"], "文件版本必须为 1.1.0.0");
        AssertEx.True(values["ApplicationIcon"].EndsWith("PI-Harness.ico", StringComparison.Ordinal), "项目必须嵌入应用图标");
        AssertEx.True(File.Exists(iconPath), "应用图标文件必须存在");
        AssertEx.True(new FileInfo(iconPath).Length > 1024, "应用图标不得为空壳");
        AssertEx.True(xaml.Contains("Icon=\"Assets/PI-Harness.ico\"", StringComparison.Ordinal), "窗口必须显示应用图标");
        AssertEx.True(xaml.Contains("在文件资源管理器中打开", StringComparison.Ordinal), "项目组必须提供右键打开目录入口");
        AssertEx.Equal("1.1.0", MainViewModel.ApplicationVersion, "界面版本必须取自应用程序集");
    }

    [TestCase("TEST-22E", "窗口关闭流程可重入且只执行一次异步回收")]
    public static void WindowShutdownIsIdempotent()
    {
        var code = File.ReadAllText(Path.Combine(
            Path.GetFullPath(Environment.CurrentDirectory),
            "src",
            "PIHarness.App",
            "MainWindow.xaml.cs"));

        AssertEx.True(code.Contains("if (_shutdownStarted)", StringComparison.Ordinal), "重复 Closing 必须在异步等待前被拦截");
        AssertEx.True(code.Contains("_shutdownStarted = true;", StringComparison.Ordinal), "首次 Closing 必须同步登记关闭状态");
    }
}
