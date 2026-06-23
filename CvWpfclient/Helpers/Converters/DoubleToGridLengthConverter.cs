using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class DoubleToGridLengthConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is double width && !double.IsNaN(width) && width > 0) {
			return new GridLength(width);
		}
		return GridLength.Auto;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		return Binding.DoNothing;
	}
}
