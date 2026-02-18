using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace K_OCRDesktop.Converters;

public class ValidationTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isValidationFailed)
        {
            // Red if validation failed, Black if passed
            return isValidationFailed 
                ? new SolidColorBrush(Color.FromRgb(220, 38, 38))  // Red
                : new SolidColorBrush(Colors.Black);                // Black
        }
        return new SolidColorBrush(Colors.Black); // Default black
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
