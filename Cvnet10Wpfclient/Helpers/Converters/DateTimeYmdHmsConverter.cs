using System.Globalization;
using System.Windows.Data;

namespace Cvnet10Wpfclient.Helpers;

public sealed class DateTimeYmdHmsConverter : IValueConverter {
	private const string SourceFormat = "yyyyMMddHHmmss";
	private const string DisplayFormat = "yyyy/MM/dd HH:mm:ss";

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return value switch {
			string s when DateTime.TryParseExact(s, SourceFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
				=> date.ToString(DisplayFormat, CultureInfo.InvariantCulture),
			DateTime dt => dt.ToString(DisplayFormat, CultureInfo.InvariantCulture),
			_ => string.Empty
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is not string s) return Binding.DoNothing;
		s = s.Trim();
		if (string.IsNullOrEmpty(s)) return string.Empty;

		if (DateTime.TryParseExact(s, DisplayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
			return date.ToString(SourceFormat, CultureInfo.InvariantCulture);

		if (DateTime.TryParseExact(s, SourceFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
			return s;

		return Binding.DoNothing;
	}
}
