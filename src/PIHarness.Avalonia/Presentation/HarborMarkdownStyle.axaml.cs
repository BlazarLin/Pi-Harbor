// Created: 2026-09-12
// Purpose: Markdown.Avalonia style themed with the Pi Harbor dark palette (WPF MarkdownTextBlock look).

using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace PIHarness.Avalonia.Presentation;

public class HarborMarkdownStyle : Styles
{

    public HarborMarkdownStyle()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
