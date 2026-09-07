// Created: 2026-09-06
// Function: Verify history and streaming Markdown use different update policies.
// Purpose: Prevent delayed history layout from moving the reading viewport.

using PIHarness.App.Presentation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PIHarness.Tests;

internal static class MarkdownRenderingPolicyTests
{
    [TestCase("TEST-19A", "历史 Markdown 在绑定时同步完成布局")]
    public static void HistoryMarkdownRendersSynchronously()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { Markdown = "历史正文" };
            AssertEx.True(block.Document.Blocks.Count > 0, "历史 Markdown 不得等待计时器后才出现");
        });
    }

    [TestCase("TEST-19B", "流式 Markdown 合并刷新并在结束时立即定稿")]
    public static void StreamingMarkdownIsThrottledUntilCompleted()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { IsStreaming = true, Markdown = "增量正文" };
            AssertEx.Equal(0, block.Document.Blocks.Count, "流式更新应在短窗口内合并，避免每个 token 重建视觉树");

            block.IsStreaming = false;
            AssertEx.True(block.Document.Blocks.Count > 0, "流式结束时必须立即生成最终布局");
        });
    }

    [TestCase("TEST-19C", "同步 Markdown 在主题颜色绑定后立即更新前景色")]
    public static void ThemeChangesRefreshRenderedMarkdown()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { Markdown = "主题正文" };
            block.Foreground = Brushes.Orange;

            AssertEx.Equal(Brushes.Orange, block.Document.Foreground, "Markdown 文档不得固化为 XAML 绑定前的默认颜色");
        });
    }

    [TestCase("TEST-26A", "详细对话为只读且支持跨段选择复制")]
    public static void MarkdownTextIsSelectableButNotEditable()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { Markdown = "第一段\n\n第二段" };
            var textView = block.Children.OfType<RichTextBox>().Single();

            AssertEx.True(textView.IsReadOnly, "详细内容必须保持只读");
            textView.SelectAll();
            AssertEx.True(textView.Selection.Text.Contains("第一段", StringComparison.Ordinal), "选区必须包含第一段");
            AssertEx.True(textView.Selection.Text.Contains("第二段", StringComparison.Ordinal), "选区必须跨越多个段落");
        });
    }

    [TestCase("TEST-26B", "Markdown 表格渲染为可选择的表头与数据单元格")]
    public static void MarkdownTableRendersAsFlowTable()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock
            {
                Markdown = """
                           | 路径 | TLS 握手 | 评价 |
                           |---|---:|---|
                           | 代理端口 | 1.03s | 正常 |
                           | TUN | 10.39s | 严重异常 |
                           """,
            };

            var table = block.Document.Blocks.OfType<Table>().Single();
            AssertEx.Equal(3, table.Columns.Count, "表格必须包含三列");
            AssertEx.Equal(3, table.RowGroups.Single().Rows.Count, "表格必须包含一行表头和两行数据");

            var textView = block.Children.OfType<RichTextBox>().Single();
            textView.SelectAll();
            AssertEx.True(textView.Selection.Text.Contains("10.39s", StringComparison.Ordinal), "表格文字必须进入可复制选区");
            AssertEx.False(textView.Selection.Text.Contains("|---|", StringComparison.Ordinal), "不得显示 Markdown 表格分隔源码");
        });
    }

    [TestCase("TEST-26C", "引用、分隔线和有序列表形成独立视觉结构")]
    public static void MarkdownBlockStructuresAreVisualized()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { Markdown = "## 小结\n\n> 关键结论\n\n---\n\n1. 第一项\n2. 第二项\n\n**重点** 与 `code`" };
            var paragraphs = block.Document.Blocks.OfType<Paragraph>().ToArray();

            AssertEx.True(paragraphs.Any(paragraph => paragraph.FontSize > block.FontSize), "标题必须具有更高的视觉层级");
            AssertEx.True(paragraphs.Any(paragraph => paragraph.BorderThickness.Left > 0), "引用必须显示左侧强调线");
            AssertEx.True(paragraphs.Any(paragraph => paragraph.BorderThickness.Top > 0), "分隔线必须形成独立横线");
            AssertEx.True(block.Document.Blocks.OfType<List>().Any(list => list.MarkerStyle == TextMarkerStyle.Decimal), "有序列表必须按数字标记渲染");
            AssertEx.True(paragraphs.SelectMany(paragraph => paragraph.Inlines).OfType<Bold>().Any(), "双星号必须渲染为粗体");
            AssertEx.True(paragraphs.SelectMany(paragraph => paragraph.Inlines).OfType<Run>().Any(run => run.Background is not null), "行内代码必须使用独立背景");
        });
    }

    [TestCase("TEST-26D", "正文获得焦点后滚轮仍驱动外层对话滚动")]
    public static void SelectableMarkdownRoutesWheelToConversation()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { Markdown = string.Join('\n', Enumerable.Repeat("可选择的长正文", 80)) };
            var scrollViewer = new ScrollViewer
            {
                Width = 320,
                Height = 120,
                Content = block,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            scrollViewer.Measure(new Size(320, 120));
            scrollViewer.Arrange(new Rect(0, 0, 320, 120));
            scrollViewer.UpdateLayout();
            var textView = block.Children.OfType<RichTextBox>().Single();
            var wheelArgs = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            };

            textView.RaiseEvent(wheelArgs);
            scrollViewer.UpdateLayout();

            AssertEx.True(wheelArgs.Handled, "正文必须接管内层滚轮事件");
            AssertEx.True(
                scrollViewer.VerticalOffset > 0,
                $"滚轮必须推动外层对话视口而不是被正文吞掉；offset={scrollViewer.VerticalOffset:F1}, " +
                $"extent={scrollViewer.ExtentHeight:F1}, viewport={scrollViewer.ViewportHeight:F1}, desired={block.DesiredSize.Height:F1}");
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
