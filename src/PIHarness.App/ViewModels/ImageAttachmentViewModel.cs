using System.IO;
using System.Windows.Media.Imaging;
using PIHarness.Core.Models;

namespace PIHarness.App.ViewModels;

public sealed class ImageAttachmentViewModel
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxImages = 4;
    private readonly byte[] _bytes;
    public string Name { get; }
    public BitmapSource Preview { get; }
    public string Detail => $"{Name} · {_bytes.Length / 1024d:F0} KB";

    private ImageAttachmentViewModel(string name, byte[] bytes, BitmapSource preview)
    {
        Name = name;
        _bytes = bytes;
        Preview = preview;
    }

    public static ImageAttachmentViewModel FromBitmap(BitmapSource source, string name)
    {
        if ((long)source.PixelWidth * source.PixelHeight > 40_000_000)
            throw new InvalidDataException("图片超过 4000 万像素，请缩小后再添加。");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        if (stream.Length > MaxImageBytes) throw new InvalidDataException("图片超过 10 MB，请缩小后再添加。");
        var bytes = stream.ToArray();
        return new ImageAttachmentViewModel(name, bytes, Decode(bytes, 160));
    }

    public static ImageAttachmentViewModel FromFile(string path)
    {
        if (new FileInfo(path).Length > MaxImageBytes) throw new InvalidDataException("图片超过 10 MB，请缩小后再添加。");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return FromBitmap(decoder.Frames[0], Path.GetFileName(path));
    }

    private static BitmapSource Decode(byte[] bytes, int width)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) bitmap.DecodePixelWidth = width;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public BitmapSource OpenPreview() => Decode(_bytes, 1600);
    public PromptImage ToPromptImage() => new(Convert.ToBase64String(_bytes), "image/png");
}
