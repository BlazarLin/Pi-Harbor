// Created: 2026-09-12
// Purpose: WPF-side image codec bridge. The shared ImageAttachmentViewModel holds PNG bytes;
//          this class decodes/encodes them for WPF controls and keeps the legacy factory
//          call sites (paste, file pick, QA) working unchanged.

using System.IO;
using System.Windows.Media.Imaging;
using PIHarness.App.ViewModels;

namespace PIHarness.App.Presentation;

public static class ImageConversion
{
    /// <summary>Small decoded thumbnail for list displays.</summary>
    public static BitmapSource Preview(this ImageAttachmentViewModel image) => Decode(image.Data, 160);

    /// <summary>Large decoded preview for the popup window.</summary>
    public static BitmapSource OpenPreview(this ImageAttachmentViewModel image) => Decode(image.Data, 1600);

    public static ImageAttachmentViewModel FromPngBytes(byte[] pngBytes, string name) =>
        ImageAttachmentViewModel.FromPngBytes(pngBytes, name);

    public static ImageAttachmentViewModel FromBitmap(BitmapSource source, string name) =>
        ImageAttachmentViewModel.FromPngBytes(EncodePng(source), name);

    public static ImageAttachmentViewModel FromImageFile(string path)
    {
        if (new FileInfo(path).Length > ImageAttachmentViewModel.MaxImageBytes)
        {
            throw new InvalidDataException("图片超过 10 MB，请缩小后再添加。");
        }

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return FromBitmap(decoder.Frames[0], Path.GetFileName(path));
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource Decode(byte[] bytes, double decodeWidth)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0)
        {
            bitmap.DecodePixelWidth = (int)decodeWidth;
        }

        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
