using Cvnet10Base.Share;
using System.Globalization;
using System.Windows.Data;

namespace Cvnet10Wpfclient.Helpers;

public sealed class EnumShimeDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is EnumShime shime) {
			return shime == EnumShime.DayLast ? "(末)" : $"({(int)shime:00})";
		}
		if (value is int number) {
			return number == (int)EnumShime.DayLast ? "(末)" : $"({number:00})";
		}
		return string.Empty;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return Binding.DoNothing;
	}
}
