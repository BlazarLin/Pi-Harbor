// Created: 2026-09-06
// Function: Verify history and streaming Markdown use different update policies.
// Purpose: Prevent delayed history layout from moving the reading viewport.

using PIHarness.App.Presentation;
using System.Windows.Controls;
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
            AssertEx.True(block.Children.Count > 0, "历史 Markdown 不得等待计时器后才出现");
        });
    }

    [TestCase("TEST-19B", "流式 Markdown 合并刷新并在结束时立即定稿")]
    public static void StreamingMarkdownIsThrottledUntilCompleted()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { IsStreaming = true, Markdown = "增量正文" };
            AssertEx.Equal(0, block.Children.Count, "流式更新应在短窗口内合并，避免每个 token 重建视觉树");

            block.IsStreaming = false;
            AssertEx.True(block.Children.Count > 0, "流式结束时必须立即生成最终布局");
        });
    }

    [TestCase("TEST-19C", "同步 Markdown 在主题颜色绑定后立即更新前景色")]
    public static void ThemeChangesRefreshRenderedMarkdown()
    {
        RunInSta(() =>
        {
            var block = new MarkdownTextBlock { Markdown = "主题正文" };
            block.Foreground = Brushes.Orange;

            var text = block.Children.OfType<TextBlock>().Single();
            AssertEx.Equal(Brushes.Orange, text.Foreground, "Markdown 子文本不得固化为 XAML 绑定前的默认颜色");
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
