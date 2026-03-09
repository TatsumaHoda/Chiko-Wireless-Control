using System.Globalization;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App.Converters;

public sealed class BoolToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isOn = value is bool b && b;

        var res = Application.Current?.Resources;
        if (res == null) return Colors.Transparent;

        // 選択中はアクセント、非選択はSurface2
        if (isOn && res.TryGetValue("ChikoBlue", out var on) && on is Color cOn) return cOn;
        if (!isOn && res.TryGetValue("Surface2", out var off) && off is Color cOff) return cOff;

        return Colors.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
