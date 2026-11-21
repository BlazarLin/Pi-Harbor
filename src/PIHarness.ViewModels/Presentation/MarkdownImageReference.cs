// 创建时间：2026-09-12
// 功能：解析 Markdown 图片链接为可预览的本地绝对路径或远程 http(s) 地址。
// 目的：把纯路径逻辑从 WPF 控件中抽出，供 WPF 与 Avalonia 前端及跨平台测试共用。

using System;
using System.IO;

namespace PIHarness.App.Presentation;

public static class MarkdownImageReference
{
    /// <summary>把 Markdown 图片原始链接转换为可预览路径；不满足安全/格式条件时返回 null。</summary>
    public static string? ResolveImagePath(string raw, string directory)
    {
        var path = raw.Trim().Trim('<', '>');
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                return IsPreviewImage(uri.AbsolutePath) ? uri.AbsoluteUri : null;
            }

            if (uri.IsFile && !uri.IsUnc)
            {
                path = uri.LocalPath;
            }
            else
            {
                return null;
            }
        }

        if (!IsPreviewImage(path))
        {
            return null;
        }

        // 先解码：%5c%5cserver%5cshare 这类编码后的 UNC 也必须在访问前被拒绝。
        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (decodedPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(decodedPath) && string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(decodedPath) ? decodedPath : Path.Combine(directory, decodedPath));
            return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? null : fullPath;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>只接受位图类扩展名，避免把 SVG 等非位图内容当预览渲染。</summary>
    public static bool IsPreviewImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";
}
