using System.Globalization;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App.Converters;

public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isOn = value is bool b && b;

        var res = Application.Current?.Resources;
        if (res == null) return Colors.White;

        // 選択中はWhite、非選択はTextSecondary
        if (isOn) return Colors.White;
        if (res.TryGetValue("TextSecondary", out var ts) && ts is Color cTs) return cTs;

        return Colors.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
