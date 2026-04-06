using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class IntToBoolConverter : IValueConverter {

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		int.TryParse(value?.ToString(), out var i);
		return i != 0;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is not string s) return Binding.DoNothing;
		s = s.Trim();
		if (string.IsNullOrEmpty(s)) return string.Empty;
		bool.TryParse(s, out var b);
		int retVal = 0;
		if (b)
			retVal = 1;
		return retVal;
	}
}
