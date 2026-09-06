// Created: 2026-09-06
// Function: Verify history and streaming Markdown use different update policies.
// Purpose: Prevent delayed history layout from moving the reading viewport.

using PIHarness.App.Presentation;

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
