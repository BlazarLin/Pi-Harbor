// Created: 2026-09-06
// Function: Render selectable, dependency-free Markdown with stable streaming updates.
// Purpose: Present technical replies as readable WPF structures while preserving native text selection and copy.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlowList = System.Windows.Documents.List;

namespace PIHarness.App.Presentation;

public sealed class MarkdownTextBlock : Grid
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

    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(28, 28, 31));
    private static readonly Brush InlineCodeBackground = new SolidColorBrush(Color.FromRgb(48, 47, 51));
    private static readonly Brush StructureBorder = new SolidColorBrush(Color.FromRgb(58, 57, 62));
    private static readonly Brush QuoteBackground = new SolidColorBrush(Color.FromRgb(35, 34, 38));
    private static readonly Brush TableHeaderBackground = new SolidColorBrush(Color.FromRgb(42, 40, 46));
    private readonly DispatcherTimer _renderTimer;
    private readonly RichTextBox _textView;

    public MarkdownTextBlock()
    {
        Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
        };
        _textView = new RichTextBox(Document)
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsDocumentEnabled = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Cursor = Cursors.IBeam,
        };
        _textView.ContextMenu = CreateCopyContextMenu(_textView);
        _textView.PreviewMouseWheel += OnTextViewPreviewMouseWheel;
        Children.Add(_textView);

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

    public FlowDocument Document { get; }

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

    private static ContextMenu CreateCopyContextMenu(RichTextBox textView)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = "复制",
            Command = ApplicationCommands.Copy,
            CommandTarget = textView,
        });
        menu.Items.Add(new MenuItem
        {
            Header = "全选",
            Command = ApplicationCommands.SelectAll,
            CommandTarget = textView,
        });
        return menu;
    }

    private void OnTextViewPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        var outerScrollViewer = FindVisualAncestor<ScrollViewer>(this);
        if (outerScrollViewer is null)
        {
            return;
        }

        var nWheelLines = SystemParameters.WheelScrollLines;
        var scrollDistance = nWheelLines < 0
            ? outerScrollViewer.ViewportHeight
            : Math.Max(FontSize * 1.4, nWheelLines * FontSize * 1.4);
        var wheelNotches = args.Delta / 120.0;
        outerScrollViewer.ScrollToVerticalOffset(outerScrollViewer.VerticalOffset - wheelNotches * scrollDistance);
        args.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
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
        ApplyPresentationProperties();
        Document.Blocks.Clear();
        var lines = (Markdown ?? string.Empty).ReplaceLineEndings("\n").Split('\n');

        for (var nIndex = 0; nIndex < lines.Length;)
        {
            if (lines[nIndex].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                nIndex = AddCodeBlock(lines, nIndex);
                continue;
            }

            if (TryAddTable(lines, nIndex, out var nTableLineCount))
            {
                nIndex += nTableLineCount;
                continue;
            }

            if (TryAddList(lines, nIndex, out var nListLineCount))
            {
                nIndex += nListLineCount;
                continue;
            }

            AddTextLine(lines[nIndex]);
            nIndex++;
        }
    }

    private void ApplyPresentationProperties()
    {
        Document.Foreground = Foreground;
        Document.FontFamily = FontFamily;
        Document.FontSize = FontSize;
        _textView.Foreground = Foreground;
        _textView.FontFamily = FontFamily;
        _textView.FontSize = FontSize;
    }

    private int AddCodeBlock(IReadOnlyList<string> lines, int fenceIndex)
    {
        var codeLines = new List<string>();
        var nIndex = fenceIndex + 1;
        while (nIndex < lines.Count && !lines[nIndex].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            codeLines.Add(lines[nIndex]);
            nIndex++;
        }

        var paragraph = CreateParagraph();
        paragraph.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        paragraph.FontSize = Math.Max(12, FontSize - 1);
        paragraph.Foreground = new SolidColorBrush(Color.FromRgb(224, 222, 228));
        paragraph.Background = CodeBackground;
        paragraph.BorderBrush = StructureBorder;
        paragraph.BorderThickness = new Thickness(1);
        paragraph.Padding = new Thickness(10, 8, 10, 8);
        paragraph.Margin = new Thickness(0, 5, 0, 7);
        paragraph.Inlines.Add(new Run(string.Join(Environment.NewLine, codeLines)));
        Document.Blocks.Add(paragraph);
        return nIndex < lines.Count ? nIndex + 1 : nIndex;
    }

    private bool TryAddTable(IReadOnlyList<string> lines, int startIndex, out int lineCount)
    {
        lineCount = 0;
        if (startIndex + 1 >= lines.Count || !lines[startIndex].Contains('|'))
        {
            return false;
        }

        var headers = SplitTableCells(lines[startIndex]);
        var separators = SplitTableCells(lines[startIndex + 1]);
        if (headers.Count < 2 || separators.Count != headers.Count || !separators.All(IsTableSeparator))
        {
            return false;
        }

        var rows = new List<IReadOnlyList<string>> { headers };
        var nIndex = startIndex + 2;
        while (nIndex < lines.Count && !string.IsNullOrWhiteSpace(lines[nIndex]) && lines[nIndex].Contains('|'))
        {
            rows.Add(NormalizeTableCells(SplitTableCells(lines[nIndex]), headers.Count));
            nIndex++;
        }

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 9),
        };
        for (var nColumn = 0; nColumn < headers.Count; nColumn++)
        {
            table.Columns.Add(new TableColumn());
        }

        var rowGroup = new TableRowGroup();
        for (var nRow = 0; nRow < rows.Count; nRow++)
        {
            var row = new TableRow();
            for (var nColumn = 0; nColumn < headers.Count; nColumn++)
            {
                var paragraph = CreateParagraph(new Thickness(0));
                paragraph.TextAlignment = GetTableAlignment(separators[nColumn]);
                AppendInlineRuns(paragraph.Inlines, rows[nRow][nColumn]);
                var cell = new TableCell(paragraph)
                {
                    BorderBrush = StructureBorder,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(7, 5, 7, 5),
                    Background = nRow == 0 ? TableHeaderBackground : Brushes.Transparent,
                    FontWeight = nRow == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                };
                row.Cells.Add(cell);
            }
            rowGroup.Rows.Add(row);
        }
        table.RowGroups.Add(rowGroup);
        Document.Blocks.Add(table);
        lineCount = nIndex - startIndex;
        return true;
    }

    private bool TryAddList(IReadOnlyList<string> lines, int startIndex, out int lineCount)
    {
        lineCount = 0;
        var bOrdered = TryGetOrderedListText(lines[startIndex], out _);
        if (!bOrdered && !TryGetUnorderedListText(lines[startIndex], out _))
        {
            return false;
        }

        var list = new FlowList
        {
            MarkerStyle = bOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(22, 2, 0, 6),
            Padding = new Thickness(0),
        };
        var nIndex = startIndex;
        while (nIndex < lines.Count)
        {
            var bMatches = bOrdered
                ? TryGetOrderedListText(lines[nIndex], out var text)
                : TryGetUnorderedListText(lines[nIndex], out text);
            if (!bMatches)
            {
                break;
            }

            var paragraph = CreateParagraph(new Thickness(0, 1, 0, 2));
            AppendInlineRuns(paragraph.Inlines, text);
            list.ListItems.Add(new ListItem(paragraph));
            nIndex++;
        }

        Document.Blocks.Add(list);
        lineCount = nIndex - startIndex;
        return true;
    }

    private void AddTextLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            Document.Blocks.Add(new Paragraph
            {
                FontSize = 5,
                LineHeight = 5,
                Margin = new Thickness(0),
            });
            return;
        }

        if (IsHorizontalRule(line))
        {
            Document.Blocks.Add(new Paragraph
            {
                BorderBrush = StructureBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                FontSize = 1,
                LineHeight = 1,
                Margin = new Thickness(0, 8, 0, 8),
            });
            return;
        }

        var paragraph = CreateParagraph();
        var content = line;
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            paragraph.FontSize = FontSize + 1;
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.Margin = new Thickness(0, 7, 0, 4);
            content = line[4..];
        }
        else if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            paragraph.FontSize = FontSize + 2;
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.Margin = new Thickness(0, 9, 0, 5);
            content = line[3..];
        }
        else if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            paragraph.FontSize = FontSize + 4;
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.Margin = new Thickness(0, 10, 0, 6);
            content = line[2..];
        }
        else if (line.StartsWith("> ", StringComparison.Ordinal))
        {
            paragraph.Foreground = new SolidColorBrush(Color.FromRgb(184, 179, 190));
            paragraph.Background = QuoteBackground;
            paragraph.BorderBrush = StructureBorder;
            paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
            paragraph.Padding = new Thickness(10, 5, 8, 5);
            paragraph.Margin = new Thickness(0, 5, 0, 7);
            content = line[2..];
        }

        AppendInlineRuns(paragraph.Inlines, content);
        Document.Blocks.Add(paragraph);
    }

    private Paragraph CreateParagraph() => CreateParagraph(new Thickness(0, 1, 0, 4));

    private Paragraph CreateParagraph(Thickness margin) => new()
    {
        Foreground = Foreground,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Margin = margin,
    };

    private static IReadOnlyList<string> SplitTableCells(string line)
    {
        var content = line.Trim();
        if (content.StartsWith('|'))
        {
            content = content[1..];
        }
        if (content.EndsWith('|'))
        {
            content = content[..^1];
        }
        return content.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static IReadOnlyList<string> NormalizeTableCells(IReadOnlyList<string> cells, int columnCount)
    {
        var result = cells.Take(columnCount).ToList();
        if (cells.Count > columnCount)
        {
            result[columnCount - 1] = string.Join(" | ", cells.Skip(columnCount - 1));
        }
        while (result.Count < columnCount)
        {
            result.Add(string.Empty);
        }
        return result;
    }

    private static bool IsTableSeparator(string cell)
    {
        var content = cell.Trim().Trim(':');
        return content.Length >= 3 && content.All(character => character == '-');
    }

    private static TextAlignment GetTableAlignment(string separator)
    {
        var content = separator.Trim();
        if (content.StartsWith(':') && content.EndsWith(':'))
        {
            return TextAlignment.Center;
        }
        return content.EndsWith(':') ? TextAlignment.Right : TextAlignment.Left;
    }

    private static bool TryGetUnorderedListText(string line, out string text)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length >= 2 && trimmed[1] == ' ' && trimmed[0] is '-' or '*' or '+')
        {
            text = trimmed[2..];
            return true;
        }
        text = string.Empty;
        return false;
    }

    private static bool TryGetOrderedListText(string line, out string text)
    {
        var trimmed = line.TrimStart();
        var nDotIndex = trimmed.IndexOf('.');
        if (nDotIndex > 0 && nDotIndex + 1 < trimmed.Length && trimmed[nDotIndex + 1] == ' ' &&
            trimmed[..nDotIndex].All(char.IsDigit))
        {
            text = trimmed[(nDotIndex + 2)..];
            return true;
        }
        text = string.Empty;
        return false;
    }

    private static bool IsHorizontalRule(string line)
    {
        var content = line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        return content.Length >= 3 &&
               (content.All(character => character == '-') || content.All(character => character == '*'));
    }

    private static void AppendInlineRuns(InlineCollection target, string text)
    {
        var nIndex = 0;
        while (nIndex < text.Length)
        {
            if (text.AsSpan(nIndex).StartsWith("**", StringComparison.Ordinal))
            {
                var nEnd = text.IndexOf("**", nIndex + 2, StringComparison.Ordinal);
                if (nEnd >= 0)
                {
                    target.Add(new Bold(new Run(text[(nIndex + 2)..nEnd])));
                    nIndex = nEnd + 2;
                    continue;
                }
            }

            if (text[nIndex] == '`')
            {
                var nEnd = text.IndexOf('`', nIndex + 1);
                if (nEnd >= 0)
                {
                    target.Add(new Run(text[(nIndex + 1)..nEnd])
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        Background = InlineCodeBackground,
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

            target.Add(new Run(text[nIndex..nNext]));
            nIndex = nNext;
        }
    }
}
