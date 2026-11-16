// Created: 2026-09-08
// Purpose: Small value converters for tab indicators and other presentation-only checks.

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PIHarness.App.Presentation;

/// <summary>Visible when the bound string is non-empty (e.g. queued message count on a tab).</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
