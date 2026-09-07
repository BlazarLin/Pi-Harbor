// Created: 2026-09-06
// Function: Lock the detailed-conversation surface to stable reading contracts.
// Purpose: Prevent regressions in virtualization, recovery controls and persistent details.

using System.Xml.Linq;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.Tests;

internal static class ConversationUiContractTests
{
    [TestCase("TEST-20A", "详细对话使用稳定虚拟化、相邻页缓存和像素连续滚动")]
    public static void MessageListUsesStableVirtualization()
    {
        var document = XDocument.Load(SourcePath("MainWindow.xaml"));
        var list = document.Descendants().Single(element =>
            element.Name.LocalName == "ListBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "MessageList"));

        AssertEx.Equal("Standard", AttributeValue(list, "VirtualizationMode"), "消息容器不得复用旧 Markdown 视觉树");
        AssertEx.Equal("1", AttributeValue(list, "CacheLength"), "必须提前实现相邻一页消息");
        AssertEx.Equal("Page", AttributeValue(list, "CacheLengthUnit"), "缓存长度单位必须为页");
        AssertEx.Equal("Pixel", AttributeValue(list, "ScrollUnit"), "可变高度消息必须支持像素级连续滚动");

        var code = File.ReadAllText(SourcePath("MainWindow.xaml.cs"));
        AssertEx.True(code.Contains("MessageList.ScrollIntoView", StringComparison.Ordinal), "自动跟随必须先生成最后一条消息");
        AssertEx.True(code.Contains("scrollViewer.ScrollToEnd()", StringComparison.Ordinal), "自动跟随必须精确对齐内容底边");
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

    [TestCase("TEST-24B", "当前会话使用独立持久颜色且优先于悬停状态")]
    public static void CurrentSessionUsesPersistentVisualState()
    {
        var xaml = File.ReadAllText(SourcePath("MainWindow.xaml"));
        var colors = File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath("MainWindow.xaml"))!, "Themes", "Colors.xaml"));

        AssertEx.True(xaml.Contains("<DataTrigger Binding=\"{Binding IsCurrent}\" Value=\"True\">", StringComparison.Ordinal), "树项目必须绑定会话 ViewModel 的持久当前状态");
        AssertEx.True(xaml.Contains("CurrentSessionBrush", StringComparison.Ordinal), "当前会话必须使用独立于悬停和普通选择的颜色");
        AssertEx.True(colors.Contains("x:Key=\"CurrentSessionBrush\"", StringComparison.Ordinal), "主题必须定义当前会话画刷");
    }

    [TestCase("TEST-25A", "滚动条值绑定完整且垂直滑块固定为 56 DIP")]
    public static void ScrollBarUsesFixedVisualThumbLength()
    {
        var colors = File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath("MainWindow.xaml"))!, "Themes", "Colors.xaml"));

        AssertEx.True(colors.Contains("Minimum=\"{TemplateBinding Minimum}\"", StringComparison.Ordinal), "滚动轨道必须绑定最小值");
        AssertEx.True(colors.Contains("Maximum=\"{TemplateBinding Maximum}\"", StringComparison.Ordinal), "滚动轨道必须绑定最大值");
        AssertEx.True(colors.Contains("Value=\"{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\"", StringComparison.Ordinal), "滚动轨道必须双向同步滚动值");
        AssertEx.True(colors.Contains("ViewportSize=\"NaN\"", StringComparison.Ordinal), "轨道必须禁用按虚拟视口估算滑块长度");
        AssertEx.True(colors.Contains("Orientation=\"{TemplateBinding Orientation}\"", StringComparison.Ordinal), "滚动轨道必须绑定方向");
        AssertEx.True(colors.Contains("x:Name=\"PART_Thumb\" Height=\"56\"", StringComparison.Ordinal), "垂直滚动滑块必须使用固定视觉长度");
    }

    private static string SourcePath(string fileName) =>
        Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), "src", "PIHarness.App", fileName);

    private static string? AttributeValue(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName ||
            attribute.Name.LocalName.EndsWith($".{localName}", StringComparison.Ordinal))?.Value;
}
