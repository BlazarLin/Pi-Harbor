// Created: 2026-09-06
// Purpose: Lock the sidebar interaction state to a dark-theme-safe WPF configuration.

using System.Xml.Linq;

namespace PIHarness.Tests;

internal static class StartupSidebarStyleTests
{
    [TestCase("TEST-17", "pi 启动期间侧栏保持启用配色并阻止重复选择")]
    public static void StartingStateKeepsDarkTreeAppearance()
    {
        var repoRoot = Path.GetFullPath(Environment.CurrentDirectory);
        var xamlPath = Path.Combine(repoRoot, "src", "PIHarness.App", "MainWindow.xaml");
        var codePath = Path.Combine(repoRoot, "src", "PIHarness.App", "MainWindow.xaml.cs");
        var document = XDocument.Load(xamlPath);
        var sessionTree = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TreeView" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "SessionTree"));

        AssertEx.True(sessionTree.Attribute("IsEnabled") is null, "SessionTree 不得触发系统白色禁用模板");
        AssertEx.Equal(
            "{Binding CanSwitchSession}",
            sessionTree.Attribute("IsHitTestVisible")?.Value,
            "启动期间应使用命中测试阻止鼠标切换");

        var code = File.ReadAllText(codePath);
        var nHandler = code.IndexOf("private async void OnSessionSelected", StringComparison.Ordinal);
        var nFocus = code.IndexOf("PromptBox.Focus();", nHandler, StringComparison.Ordinal);
        var nOpen = code.IndexOf("await _viewModel.OpenSessionAsync", nHandler, StringComparison.Ordinal);
        AssertEx.True(nHandler >= 0 && nFocus > nHandler && nOpen > nFocus, "会话启动前应先把键盘焦点移出 SessionTree");
    }
}
