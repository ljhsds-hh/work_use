using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CreateTpl.Helpers;

/// <summary>布尔取反转 Visibility：true → Collapsed，false → Visible（用于空态提示展示）。</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
