using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace K_OCRDesktop.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isProcessed)
        {
            // Red if processed, Gray if not
            return isProcessed 
                ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) 
                : new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }
        return new SolidColorBrush(Color.FromRgb(156, 163, 175)); // Default gray
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
