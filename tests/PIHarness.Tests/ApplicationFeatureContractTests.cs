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

    [TestCase("TEST-22D", "Pi Harbor 主品牌、副标题、图标与 1.3.1 版本进入窗口和程序集")]
    public static void ApplicationIdentityIsVersionedAndBranded()
    {
        var repoRoot = Path.GetFullPath(Environment.CurrentDirectory);
        var projectPath = Path.Combine(repoRoot, "src", "PIHarness.App", "PIHarness.App.csproj");
        var xamlPath = Path.Combine(repoRoot, "src", "PIHarness.App", "MainWindow.xaml");
        var viewModelPath = Path.Combine(repoRoot, "src", "PIHarness.App", "ViewModels", "MainViewModel.cs");
        var releaseScriptPath = Path.Combine(repoRoot, "scripts", "build-release.ps1");
        var iconPath = Path.Combine(repoRoot, "src", "PIHarness.App", "Assets", "Pi-Harbor.ico");
        var project = XDocument.Load(projectPath);
        var values = project.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .ToDictionary(element => element.Name.LocalName, element => element.Value);
        var xaml = File.ReadAllText(xamlPath);
        var viewModelSource = File.ReadAllText(viewModelPath);
        var releaseScript = File.ReadAllText(releaseScriptPath);

        AssertEx.Equal("Pi-Harbor", values["AssemblyName"], "可执行程序集必须使用新品牌名");
        AssertEx.Equal("Pi Harbor — Pi Session Desk", values["AssemblyTitle"], "Windows 文件说明必须同时显示主品牌和副标题");
        AssertEx.Equal("Pi Harbor", values["Product"], "Windows 产品元数据必须使用主品牌");
        AssertEx.True(values["Description"].Contains("Pi Session Desk", StringComparison.Ordinal), "产品描述必须包含副标题");
        AssertEx.Equal("1.3.1", values["Version"], "包版本必须为 1.3.1");
        AssertEx.Equal("1.3.1.0", values["FileVersion"], "文件版本必须为 1.3.1.0");
        AssertEx.True(values["ApplicationIcon"].EndsWith("Pi-Harbor.ico", StringComparison.Ordinal), "项目必须嵌入新品牌图标");
        AssertEx.True(File.Exists(iconPath), "应用图标文件必须存在");
        AssertEx.True(new FileInfo(iconPath).Length > 1024, "应用图标不得为空壳");
        AssertEx.True(xaml.Contains("Icon=\"Assets/Pi-Harbor.ico\"", StringComparison.Ordinal), "窗口必须显示新品牌图标");
        AssertEx.True(xaml.Contains("Text=\"Pi Harbor\"", StringComparison.Ordinal), "侧栏必须显示主品牌");
        AssertEx.True(xaml.Contains("Text=\"Pi Session Desk\"", StringComparison.Ordinal), "侧栏必须显示功能副标题");
        AssertEx.True(xaml.Contains("在文件资源管理器中打开", StringComparison.Ordinal), "项目组必须提供右键打开目录入口");
        AssertEx.Equal("1.3.1", MainViewModel.ApplicationVersion, "界面版本必须取自应用程序集");
        AssertEx.True(viewModelSource.Contains("$\"{ProductName} {ApplicationVersion} — {ProductSubtitle}\"", StringComparison.Ordinal), "窗口标题必须同时表达主品牌和副标题");
        AssertEx.True(releaseScript.Contains("'Pi-Harbor-win-x64'", StringComparison.Ordinal), "发布目录必须使用新品牌名");
        AssertEx.True(releaseScript.Contains("'Pi-Harbor-win-x64.zip'", StringComparison.Ordinal), "发布压缩包必须使用新品牌名");
        AssertEx.True(releaseScript.Contains("'Pi-Harbor.exe'", StringComparison.Ordinal), "发布入口必须使用新品牌名");
        AssertEx.False(releaseScript.Contains("'PI-Harness-win-x64'", StringComparison.Ordinal), "发布脚本不得继续生成旧品牌目录");
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

    [TestCase("TEST-23A", "模型选择框在空闲悬停与聚焦状态保持统一暗色")]
    public static void ModelSelectorOwnsEveryVisualState()
    {
        var xaml = File.ReadAllText(AppSourcePath("MainWindow.xaml"));

        AssertEx.True(xaml.Contains("TargetNullValue=选择会话后可切换模型", StringComparison.Ordinal), "无会话时必须显示明确占位文字");
        AssertEx.True(xaml.Contains("<ControlTemplate TargetType=\"ToggleButton\">", StringComparison.Ordinal), "模型框内部按钮必须移除系统默认皮肤");
        AssertEx.True(xaml.Contains("Property=\"IsKeyboardFocusWithin\"", StringComparison.Ordinal), "键盘或鼠标聚焦必须使用显式主题状态");
        AssertEx.True(xaml.Contains("Property=\"IsDropDownOpen\"", StringComparison.Ordinal), "展开状态必须使用显式主题状态");
    }

    [TestCase("TEST-23B", "项目悬停只作用于标题且项目组不会进入选中态")]
    public static void ProjectTreeSeparatesParentAndChildHover()
    {
        var xaml = File.ReadAllText(AppSourcePath("MainWindow.xaml"));
        var code = File.ReadAllText(AppSourcePath("MainWindow.xaml.cs"));
        var treeStyle = System.Xml.Linq.XDocument.Parse(xaml).Descendants()
            .Single(element => element.Name.LocalName == "Style" && element.Attribute("TargetType")?.Value == "TreeViewItem").ToString();

        AssertEx.True(xaml.Contains("Binding=\"{Binding IsMouseOver, ElementName=HeaderBorder}\"", StringComparison.Ordinal), "悬停背景必须仅观察当前标题区域");
        AssertEx.False(treeStyle.Contains("<Trigger Property=\"IsMouseOver\" Value=\"True\">", StringComparison.Ordinal), "TreeViewItem 不得由包含子项的 IsMouseOver 驱动背景");
        AssertEx.True(xaml.Contains("PreviewMouseLeftButtonDown=\"OnTreeItemPreviewMouseLeftButtonDown\"", StringComparison.Ordinal), "项目点击必须由专用入口拦截");
        AssertEx.True(code.Contains("Header is ProjectGroupViewModel", StringComparison.Ordinal), "只允许拦截项目组，不能影响会话选择");
        AssertEx.True(code.Contains("args.Handled = true;", StringComparison.Ordinal), "项目组点击必须阻止进入选中态");
    }

    [TestCase("TEST-23C", "项目右键菜单使用统一暗色模板")]
    public static void ProjectContextMenuUsesDarkTheme()
    {
        var colors = File.ReadAllText(AppSourcePath("Themes", "Colors.xaml"));

        AssertEx.True(colors.Contains("<Style TargetType=\"ContextMenu\">", StringComparison.Ordinal), "ContextMenu 必须有暗色全局样式");
        AssertEx.True(colors.Contains("<Style TargetType=\"MenuItem\">", StringComparison.Ordinal), "MenuItem 必须有暗色全局样式");
        AssertEx.True(colors.Contains("Background=\"{StaticResource PanelBrush}\"", StringComparison.Ordinal), "菜单背景必须使用主题面板色");
        AssertEx.True(colors.Contains("Value=\"{StaticResource HoverBrush}\"", StringComparison.Ordinal), "菜单悬停必须使用统一 HoverBrush");
    }

    [TestCase("TEST-23D", "发布版可自动捕获关键交互样式状态")]
    public static void ApplicationProvidesStyleQaEntryPoint()
    {
        var windowCode = File.ReadAllText(AppSourcePath("MainWindow.xaml.cs"));
        var qaCode = File.ReadAllText(AppSourcePath("MainWindow.StyleQa.cs"));

        AssertEx.True(windowCode.Contains("--qa-style-capture-dir", StringComparison.Ordinal), "主窗口必须提供样式验收入口");
        AssertEx.True(qaCode.Contains("01-no-session.png", StringComparison.Ordinal), "必须捕获无会话模型框");
        AssertEx.True(qaCode.Contains("02-model-focus.png", StringComparison.Ordinal), "必须捕获模型聚焦状态");
        AssertEx.True(qaCode.Contains("03-model-dropdown.png", StringComparison.Ordinal), "必须捕获模型下拉状态");
        AssertEx.True(qaCode.Contains("04-project-not-selected.png", StringComparison.Ordinal), "必须捕获项目取消选中状态");
        AssertEx.True(qaCode.Contains("05-project-context-menu.png", StringComparison.Ordinal), "必须捕获项目右键菜单");
        AssertEx.True(qaCode.Contains("06-current-session.png", StringComparison.Ordinal), "必须捕获当前会话持久高亮状态");
        AssertEx.True(qaCode.Contains("TEST-STYLE-06", StringComparison.Ordinal), "样式报告必须验收当前会话状态");
    }

    private static string AppSourcePath(params string[] parts)
    {
        var pathParts = new[] { Path.GetFullPath(Environment.CurrentDirectory), "src", "PIHarness.App" }.Concat(parts).ToArray();
        return Path.Combine(pathParts);
    }
}
