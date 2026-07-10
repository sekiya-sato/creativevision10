/*
# description
LongToLocalDateTimeStringConverter は ticks をローカル時刻の文字列へ変換し、必要に応じて Binding の ConverterParameter で表示書式を指定する IValueConverter です。

# example
<TextBlock Text="{Binding Timestamp, Converter={StaticResource LongToLocalDateTimeStringConverter}}" />
 */
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class LongToLocalDateTimeStringConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		long.TryParse(value?.ToString() ?? "0", out var ticks);
		var format = parameter as string ?? "yyyy/MM/dd HH:mm:ss.ffff";
		var dt = new DateTime(ticks).ToLocalTime();
		return dt.ToString(format, culture);
	}
	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> Binding.DoNothing;
}
