/*
# description
InverseBooleanToVisibilityConverter は bool 値を反転した上で Visibility へ変換する IValueConverter です。
（true→Collapsed、false→Visible）。条件パネルと結果パネルのように、片方が真の間はもう片方を隠す
排他表示の切替に使う。

# example
<Grid Visibility="{Binding IsResultVisible, Converter={StaticResource InverseBooleanToVisibilityConverter}}" />
 */
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		bool isTrue = value is bool b && b;
		return isTrue ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}
