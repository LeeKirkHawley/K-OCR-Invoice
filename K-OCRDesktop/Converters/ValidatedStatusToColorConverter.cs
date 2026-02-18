using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace K_OCRDesktop.Converters;

public class ValidatedStatusToColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is bool isValidated && values[1] is bool hasSuspect)
        {
            if (isValidated && !hasSuspect)
                return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
            if (hasSuspect)
                return new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
            return new SolidColorBrush(Colors.Transparent);
        }
        return new SolidColorBrush(Colors.Transparent);
    }
}