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
            // Green if processed, transparent/background if not
            return isProcessed 
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) 
                : new SolidColorBrush(Colors.Transparent);
        }
        return new SolidColorBrush(Colors.Transparent); // Default transparent
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
