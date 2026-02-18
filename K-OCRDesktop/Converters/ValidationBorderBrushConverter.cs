using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace K_OCRDesktop.Converters;

/// <summary>
/// Converts IsValidationFailed boolean to a border brush.
/// Returns red brush for failed validation, transparent for passed.
/// </summary>
public class ValidationBorderBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isValidationFailed)
        {
            return isValidationFailed 
                ? new SolidColorBrush(Color.FromRgb(220, 38, 38))  // Red for failed
                : Brushes.Transparent;  // Transparent for passed
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
