/*
# description
InverseBooleanConverter は bool 値を反転して返す IValueConverter です。

# example
<Button IsEnabled="{Binding IsBusy, Converter={StaticResource InverseBooleanConverter}}" />
 */
using System.Globalization;
using System.Windows.Data;

namespace CvWpfpos.Helpers;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : false;
}
