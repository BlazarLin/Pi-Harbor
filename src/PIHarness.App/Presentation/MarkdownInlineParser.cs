// Created: 2026-09-06
// Purpose: Render a safe, dependency-free subset of Markdown with throttled streaming updates.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace PIHarness.App.Presentation;

public sealed class MarkdownTextBlock : StackPanel
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MarkdownTextBlock),
        new FrameworkPropertyMetadata(
            Brushes.White,
            FrameworkPropertyMetadataOptions.Inherits,
            OnPresentationPropertyChanged));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(MarkdownTextBlock),
        new FrameworkPropertyMetadata(
            new FontFamily("Segoe UI"),
            FrameworkPropertyMetadataOptions.Inherits,
            OnPresentationPropertyChanged));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(MarkdownTextBlock),
        new FrameworkPropertyMetadata(
            14.0,
            FrameworkPropertyMetadataOptions.Inherits,
            OnPresentationPropertyChanged));

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnMarkdownChanged));

    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(
        nameof(IsStreaming),
        typeof(bool),
        typeof(MarkdownTextBlock),
        new FrameworkPropertyMetadata(false, OnIsStreamingChanged));

    private readonly DispatcherTimer _renderTimer;

    public MarkdownTextBlock()
    {
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            RenderMarkdown();
        };
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (MarkdownTextBlock)dependencyObject;
        control._renderTimer.Stop();
        if (control.IsStreaming)
        {
            control._renderTimer.Start();
        }
        else
        {
            control.RenderMarkdown();
        }
    }

    private static void OnPresentationPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (MarkdownTextBlock)dependencyObject;
        if (!control.IsStreaming)
        {
            control.RenderMarkdown();
        }
    }

    private static void OnIsStreamingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (MarkdownTextBlock)dependencyObject;
        if (!(bool)args.NewValue)
        {
            control._renderTimer.Stop();
            control.RenderMarkdown();
        }
    }

    private void RenderMarkdown()
    {
        Children.Clear();
        var lines = (Markdown ?? string.Empty).ReplaceLineEndings("\n").Split('\n');
        var codeLines = new List<string>();
        var bInCode = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (bInCode)
                {
                    AddCodeBlock(codeLines);
                    codeLines.Clear();
                }

                bInCode = !bInCode;
                continue;
            }

            if (bInCode)
            {
                codeLines.Add(line);
                continue;
            }

            AddTextLine(line);
        }

        if (codeLines.Count > 0)
        {
            AddCodeBlock(codeLines);
        }
    }

    private void AddTextLine(string line)
    {
        var textBlock = new TextBlock
        {
            Foreground = Foreground,
            FontFamily = FontFamily,
            FontSize = FontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 3),
        };

        var content = line;
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            textBlock.FontSize = FontSize + 1;
            textBlock.FontWeight = FontWeights.SemiBold;
            content = line[4..];
        }
        else if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            textBlock.FontSize = FontSize + 2;
            textBlock.FontWeight = FontWeights.SemiBold;
            content = line[3..];
        }
        else if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            textBlock.FontSize = FontSize + 4;
            textBlock.FontWeight = FontWeights.SemiBold;
            content = line[2..];
        }
        else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            content = $"•  {line[2..]}";
        }

        AppendInlineRuns(textBlock, content);
        Children.Add(textBlock);
    }

    private void AddCodeBlock(IReadOnlyCollection<string> lines)
    {
        var textBox = new TextBox
        {
            Text = string.Join(Environment.NewLine, lines),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = Math.Max(12, FontSize - 1),
            Foreground = new SolidColorBrush(Color.FromRgb(224, 222, 228)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 57, 62)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 5, 0, 7),
            Child = textBox,
        };
        Children.Add(border);
    }

    private static void AppendInlineRuns(TextBlock target, string text)
    {
        var nIndex = 0;
        while (nIndex < text.Length)
        {
            if (text.AsSpan(nIndex).StartsWith("**", StringComparison.Ordinal))
            {
                var nEnd = text.IndexOf("**", nIndex + 2, StringComparison.Ordinal);
                if (nEnd >= 0)
                {
                    target.Inlines.Add(new Bold(new Run(text[(nIndex + 2)..nEnd])));
                    nIndex = nEnd + 2;
                    continue;
                }
            }

            if (text[nIndex] == '`')
            {
                var nEnd = text.IndexOf('`', nIndex + 1);
                if (nEnd >= 0)
                {
                    target.Inlines.Add(new Run(text[(nIndex + 1)..nEnd])
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        Background = new SolidColorBrush(Color.FromRgb(48, 47, 51)),
                    });
                    nIndex = nEnd + 1;
                    continue;
                }
            }

            var nNextBold = text.IndexOf("**", nIndex, StringComparison.Ordinal);
            var nNextCode = text.IndexOf('`', nIndex);
            var nNext = new[] { nNextBold, nNextCode }.Where(value => value >= 0).DefaultIfEmpty(text.Length).Min();
            if (nNext == nIndex)
            {
                nNext++;
            }

            target.Inlines.Add(new Run(text[nIndex..nNext]));
            nIndex = nNext;
        }
    }
}
