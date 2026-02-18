using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace K_OCRDesktop.Converters;

/// <summary>
/// Converts IsValidationFailed boolean to a border thickness.
/// Returns 2px thickness for failed validation, 0 for passed.
/// </summary>
public class ValidationBorderThicknessConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isValidationFailed)
        {
            return isValidationFailed 
                ? new Avalonia.Thickness(2)  // 2px border for failed
                : new Avalonia.Thickness(0);  // No border for passed
        }
        return new Avalonia.Thickness(0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
