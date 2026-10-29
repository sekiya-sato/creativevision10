using System.Globalization;
using System.Windows.Data;

namespace CvWpfpos.Helpers;

public sealed class KubunToPsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int kubun && kubun == 1 ? "S" : "P";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && s == "S" ? 1 : 0;
}
