// Created: 2026-09-06
// Function: Lock the detailed-conversation surface to stable reading contracts.
// Purpose: Prevent regressions in virtualization, recovery controls and persistent details.

using System.Xml.Linq;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.Tests;

internal static class ConversationUiContractTests
{
    [TestCase("TEST-20A", "详细对话使用稳定虚拟化、相邻页缓存和项目锚点滚动")]
    public static void MessageListUsesStableVirtualization()
    {
        var document = XDocument.Load(SourcePath("MainWindow.xaml"));
        var list = document.Descendants().Single(element =>
            element.Name.LocalName == "ListBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "MessageList"));

        AssertEx.Equal("Standard", AttributeValue(list, "VirtualizationMode"), "消息容器不得复用旧 Markdown 视觉树");
        AssertEx.Equal("1", AttributeValue(list, "CacheLength"), "必须提前实现相邻一页消息");
        AssertEx.Equal("Page", AttributeValue(list, "CacheLengthUnit"), "缓存长度单位必须为页");
        AssertEx.Equal("Item", AttributeValue(list, "ScrollUnit"), "可变高度虚拟消息必须按稳定项目锚点滚动");
    }

    [TestCase("TEST-20B", "历史阅读提供回到最新、加载反馈和消息数量")]
    public static void ConversationSurfaceExposesReadingFeedback()
    {
        var xaml = File.ReadAllText(SourcePath("MainWindow.xaml"));
        AssertEx.True(xaml.Contains("回到最新", StringComparison.Ordinal), "离开底部后必须提供恢复入口");
        AssertEx.True(xaml.Contains("正在读取会话", StringComparison.Ordinal), "大会话加载期间必须提供明确反馈");
        AssertEx.True(xaml.Contains("MessageCountText", StringComparison.Ordinal), "标题区必须显示当前消息规模");
        AssertEx.True(xaml.Contains("PreviewMouseWheel=\"OnMessagePreviewMouseWheel\"", StringComparison.Ordinal), "滚轮输入必须先于布局事件更新阅读意图");

        var code = File.ReadAllText(SourcePath("MainWindow.xaml.cs"));
        var nHandler = code.IndexOf("private void OnMessagePreviewMouseWheel", StringComparison.Ordinal);
        var nCancel = code.IndexOf("CancelPendingAutoScroll();", nHandler, StringComparison.Ordinal);
        AssertEx.True(nHandler >= 0 && nCancel > nHandler, "用户上滚时必须取消尚未执行的自动滚底任务");
    }

    [TestCase("TEST-20C", "思考与工具详情的展开状态离开视口后仍保留")]
    public static void DetailExpansionStateLivesInViewModel()
    {
        var item = new ChatItemViewModel(ChatItemKind.Tool, "结果") { IsExpanded = true };
        AssertEx.True(item.IsExpanded, "展开状态必须由消息项持有，不能只保存在回收容器中");

        var xaml = File.ReadAllText(SourcePath("MainWindow.xaml"));
        AssertEx.True(xaml.Contains("IsExpanded=\"{Binding IsExpanded, Mode=TwoWay}\"", StringComparison.Ordinal), "详情折叠器必须双向绑定状态");
    }

    private static string SourcePath(string fileName) =>
        Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), "src", "PIHarness.App", fileName);

    private static string? AttributeValue(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName ||
            attribute.Name.LocalName.EndsWith($".{localName}", StringComparison.Ordinal))?.Value;
}
