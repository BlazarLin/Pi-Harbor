// Created: 2026-09-12
// Purpose: Presentation helpers for the Avalonia frontend: kind-keyed template resolution,
//          palette converters and small behaviors shared across windows.

using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Templates;
using Avalonia;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.Avalonia.Presentation;

/// <summary>Resolves the keyed message DataTemplate by ChatItemKind (WPF template selector equivalent).</summary>
public sealed class MessageTemplateSelector : IDataTemplate
{
    /// <summary>Window-level resources take precedence (templates carry Click handlers).</summary>
    public static IResourceDictionary? WindowResources { get; set; }


    public Control? Build(object? param)
    {
        if (param is not ChatItemViewModel item)
        {
            return new TextBlock { Text = param?.ToString() ?? string.Empty };
        }

        var key = item.Kind switch
        {
            ChatItemKind.User => "UserMessageTemplate",
            ChatItemKind.Assistant => "AssistantMessageTemplate",
            ChatItemKind.Thinking => "ThinkingMessageTemplate",
            ChatItemKind.Tool => "ToolMessageTemplate",
            ChatItemKind.Metrics => "MetricsMessageTemplate",
            ChatItemKind.Error => "ErrorMessageTemplate",
            ChatItemKind.Queued => "QueuedMessageTemplate",
            ChatItemKind.ActivityGroup => "ActivityGroupTemplate",
            _ => "SystemMessageTemplate",
        };

        if (WindowResources?.TryGetResource(key, null, out var template) == true &&
            template is IDataTemplate dataTemplate)
        {
            return dataTemplate.Build(param);
        }

        if (Application.Current?.Resources.TryGetResource(key, null, out var appTemplate) == true &&
            appTemplate is IDataTemplate appDataTemplate)
        {
            return appDataTemplate.Build(param);
        }

        return new TextBlock { Text = item.Text };
    }

    public bool Match(object? data) => data is ChatItemViewModel;
}

public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Decodes shared PNG byte attachments into Avalonia bitmaps (thumbnail width).</summary>
public sealed class BytesToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, 160);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class AvaloniaImageConversion
{
    public static ImageAttachmentViewModel FromPngBytes(byte[] pngBytes, string name) =>
        ImageAttachmentViewModel.FromPngBytes(pngBytes, name);

    public static ImageAttachmentViewModel FromBitmap(Bitmap bitmap, string name)
    {
        using var stream = new MemoryStream();
        #pragma warning disable CS0618
        bitmap.Save(stream);
        #pragma warning restore CS0618
        return ImageAttachmentViewModel.FromPngBytes(stream.ToArray(), name);
    }

    public static Bitmap OpenPreview(ImageAttachmentViewModel image)
    {
        using var stream = new MemoryStream(image.Data, writable: false);
        return Bitmap.DecodeToWidth(stream, 1600);
    }
}

/// <summary>Resolves sidebar tree templates by item type (recent group / project / session).</summary>
public sealed class SidebarTemplateSelector : IDataTemplate
{
    public Control? Build(object? param)
    {
        var key = param switch
        {
            RecentSessionsGroupViewModel => "RecentGroupTemplate",
            ProjectGroupViewModel => "ProjectGroupTemplate",
            SessionItemViewModel => "SessionItemTemplate",
            SessionSearchItemViewModel => "SearchResultTemplate",
            _ => null,
        };

        if (key is not null &&
            MessageTemplateSelector.WindowResources?.TryGetResource(key, null, out var template) == true &&
            template is IDataTemplate dataTemplate)
        {
            return dataTemplate.Build(param);
        }

        return new TextBlock { Text = param?.ToString() ?? string.Empty };
    }

    public bool Match(object? data) =>
        data is RecentSessionsGroupViewModel or ProjectGroupViewModel or SessionItemViewModel or SessionSearchItemViewModel;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag ? !flag : true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
