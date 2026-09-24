using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PIHarness.App.Presentation;

public sealed partial class MarkdownTextBlock
{
    public static readonly DependencyProperty BaseDirectoryProperty = DependencyProperty.Register(nameof(BaseDirectory), typeof(string),
        typeof(MarkdownTextBlock), new PropertyMetadata("", OnMarkdownChanged));
    public string BaseDirectory { get => (string)GetValue(BaseDirectoryProperty); set => SetValue(BaseDirectoryProperty, value); }
    private int _renderedImageCount;
    private static readonly Regex ImageLink = new(@"!?\[[^\]]*\]\((?<path><[^>]+>|(?:[^()]|\([^)]*\))+)\)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly HttpClient ImageClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const int MaxPreviewBytes = 10 * 1024 * 1024;

    public static string? ResolveImagePath(string raw, string directory)
    {
        var path = raw.Trim().Trim('<', '>');
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https") return IsPreviewImage(uri.AbsolutePath) ? uri.AbsoluteUri : null;
            if (uri.IsFile && !uri.IsUnc) path = uri.LocalPath;
            else return null;
        }
        if (!IsPreviewImage(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        if (!Path.IsPathFullyQualified(path) && string.IsNullOrWhiteSpace(directory)) return null;
        try
        {
            var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(directory, Uri.UnescapeDataString(path)));
            return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? null : fullPath;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static bool IsPreviewImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";

    private bool TryAddImageLine(string line)
    {
        (Match Match, string? Path)[] matches;
        try
        {
            matches = ImageLink.Matches(line).Cast<Match>().Select(match => (Match: match, Path: ResolveImagePath(match.Groups["path"].Value, BaseDirectory)))
                .Where(item => item.Path is not null).ToArray();
        }
        catch (RegexMatchTimeoutException) { return false; }
        if (matches.Length == 0) return false;
        var offset = 0;
        foreach (var (match, path) in matches)
        {
            if (match.Index > offset) AddTextLine(line[offset..match.Index]);
            offset = match.Index + match.Length;
            if (++_renderedImageCount > 8) { AddTextLine(match.Value); continue; }
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
            var label = new TextBlock { Text = path, Foreground = Foreground, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            var button = new Button { Content = "加载图片预览", Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 720, ToolTip = path };
            if (TryFindResource("FlatButtonStyle") is Style style) button.Style = style;
            panel.Children.Add(button);
            panel.Children.Add(label);
            var remote = path!.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            BitmapSource? loaded = null;
            async Task LoadAsync()
            {
                button.IsEnabled = false;
                try
                {
                    byte[] bytes;
                    if (remote)
                    {
                        using var response = await ImageClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > MaxPreviewBytes) throw new InvalidDataException("图片超过 10 MB");
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                        bytes = await ReadPreviewBytesAsync(input, timeout.Token);
                    }
                    else
                    {
                        if (new FileInfo(path).Length > MaxPreviewBytes) throw new InvalidDataException("图片超过 10 MB");
                        await using var input = File.OpenRead(path);
                        bytes = await ReadPreviewBytesAsync(input, CancellationToken.None);
                    }
                    using var probe = new MemoryStream(bytes, writable: false);
                    var frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000) throw new InvalidDataException("图片超过 4000 万像素");
                    using var stream = new MemoryStream(bytes, writable: false);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = Math.Min(1200, frame.PixelWidth);
                    else bitmap.DecodePixelHeight = Math.Min(1200, frame.PixelHeight);
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    loaded = bitmap;
                    button.Content = new Image { Source = loaded, MaxWidth = 680, MaxHeight = 320, Stretch = Stretch.Uniform };
                    button.ToolTip = "点击放大图片";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or HttpRequestException or OperationCanceledException)
                { button.Content = "图片不可用 · 点击重试"; label.Text = $"{path}\n{error.Message}"; }
                finally { button.IsEnabled = true; }
            }
            button.Click += async (_, _) =>
            {
                if (loaded is null) { await LoadAsync(); return; }
                var preview = new Window { Owner = Window.GetWindow(this), Title = "图片预览", Width = 960, Height = 720,
                    Background = Background == Brushes.Transparent ? Brushes.Black : (Brush?)TryFindResource("WindowBrush") ?? Brushes.Black,
                    Content = new Image { Source = loaded, Stretch = Stretch.Uniform, Margin = new Thickness(12) }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                preview.PreviewKeyDown += (_, key) => { if (key.Key == System.Windows.Input.Key.Escape) preview.Close(); };
                preview.ShowDialog();
            };
            if (remote) button.Content = "加载远程图片（联网）";
            else if (!IsStreaming) _ = LoadAsync();
            Document.Blocks.Add(new BlockUIContainer(panel));
        }
        if (offset < line.Length) AddTextLine(line[offset..]);
        return true;
    }

    private static async Task<byte[]> ReadPreviewBytesAsync(Stream input, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > MaxPreviewBytes) throw new InvalidDataException("图片超过 10 MB");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
