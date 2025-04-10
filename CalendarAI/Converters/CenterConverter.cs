using System;
using System.Windows.Data;

namespace CalendarAI.Converters;

public class CenterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double screenSize)
        {
            // For width: (screen width - window width) / 2
            // For height: (screen height - window height) / 2
            return (screenSize - 600) / 2; // 600 is the window width
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
} 