/*
# description
InverseBooleanConverter は bool 値を反転して返す IValueConverter です。

# example
<Button IsEnabled="{Binding IsLoading, Converter={StaticResource InverseBooleanConverter}}" />
 */
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class InverseBooleanConverter : IValueConverter {

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is bool b) return !b;
		return false;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is bool b) return !b;
		return false;
	}
}
