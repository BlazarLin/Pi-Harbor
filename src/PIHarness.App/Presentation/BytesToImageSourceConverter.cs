// Created: 2026-09-12
// Purpose: Decode shared PNG byte attachments into WPF Image sources for DataTemplate bindings.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PIHarness.App.Presentation;

namespace PIHarness.App.Presentation;

public sealed class BytesToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte[] bytes ? ImageConversion.FromPngBytes(bytes, "附件").Preview() : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
