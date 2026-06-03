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
