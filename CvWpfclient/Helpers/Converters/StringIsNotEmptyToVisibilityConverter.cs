/*
# description
StringIsNotEmptyToVisibilityConverter は空白以外の文字列を Visible、空または null を Collapsed へ変換する IValueConverter です。

# example
<TextBlock Visibility="{Binding ErrorMessage, Converter={StaticResource StringIsNotEmptyToVisibilityConverter}}" />
 */
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public class StringIsNotEmptyToVisibilityConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		return value is string str && !string.IsNullOrWhiteSpace(str) ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}
