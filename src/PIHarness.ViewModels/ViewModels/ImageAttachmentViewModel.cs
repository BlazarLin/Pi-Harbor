// Created: 2026-09-12
// Purpose: Platform-neutral image attachment state. PNG bytes are the single source of truth;
//          each UI framework decodes/encodes on its side (see ImageConversion in the WPF app
//          and the Avalonia converters).

using System.IO;
using PIHarness.Core.Models;

namespace PIHarness.App.ViewModels;

public sealed class ImageAttachmentViewModel
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxImages = 4;

    public string Name { get; }
    public byte[] Data { get; }
    public string Detail => $"{Name} · {Data.Length / 1024d:F0} KB";

    private ImageAttachmentViewModel(string name, byte[] pngBytes)
    {
        Name = name;
        Data = pngBytes;
    }

    public static ImageAttachmentViewModel FromPngBytes(byte[] pngBytes, string name)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length > MaxImageBytes)
        {
            throw new InvalidDataException("图片超过 10 MB，请缩小后再添加。");
        }
        return new ImageAttachmentViewModel(name, pngBytes);
    }

    public PromptImage ToPromptImage() => new(Convert.ToBase64String(Data), "image/png");
}
