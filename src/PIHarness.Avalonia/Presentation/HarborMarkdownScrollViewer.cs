// Created: 2026-09-12
// Purpose: Markdown viewer that ships the Pi Harbor dark markdown style instead of the
//          auto-selected FluentTheme style (whose table/code colors are too light).

using Markdown.Avalonia.Full;
using PIHarness.Avalonia.Presentation;

namespace PIHarness.Avalonia.Presentation;

public class HarborMarkdownScrollViewer : MarkdownScrollViewer
{
    public HarborMarkdownScrollViewer()
    {
        MarkdownStyle = new HarborMarkdownStyle();
    }
}
