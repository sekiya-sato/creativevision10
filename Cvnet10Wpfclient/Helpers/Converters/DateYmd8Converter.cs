/*
# file name
DateYmd8Converter.cs

# description
"yyyyMMdd" 形式の string と DateTime? の相互変換を行う IValueConverter

# example
View側:
<Window
    xmlns:helpers="clr-namespace:Cvnet10Wpfclient.Helpers">
    <Window.Resources>
        <helpers:DateYmd8Converter x:Key="DateYmd8Converter" />
    </Window.Resources>

    <StackPanel>
        <DatePicker
            SelectedDate="{Binding Ymd8Text, Converter={StaticResource DateYmd8Converter}}" />

        <TextBox
            Foreground="{DynamicResource MaterialDesignBody}"
            Text="{Binding Ymd8Text, Converter={StaticResource DateYmd8Converter}, UpdateSourceTrigger=PropertyChanged}" />
    </StackPanel>
</Window>
あるいは、
App.xaml 内でリソース定義:
 <helpers:DateYmd8Converter x:Key="DateYmd8Converter" />
View側:
<TextBox
    Foreground="{DynamicResource MaterialDesignBody}"
    Text="{Binding Ymd8Text, Converter={StaticResource DateYmd8Converter}}" /> 
 */
using System.Globalization;
using System.Windows.Data;

namespace Cvnet10Wpfclient.Helpers;

public sealed class DateYmd8Converter : IValueConverter {
	private const string Format = "yyyyMMdd";

	/// <summary>
	/// "yyyyMMdd" 形式の string → DateTime?
	/// </summary>
	/// <param name="value"></param>
	/// <param name="targetType"></param>
	/// <param name="parameter"></param>
	/// <param name="culture"></param>
	/// <returns></returns>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is string s && !string.IsNullOrWhiteSpace(s) &&
			DateTime.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
			return date;
		}
		return null;
	}
	/// <summary>
	/// DateTime → "yyyyMMdd" 形式の string
	/// </summary>
	/// <param name="value"></param>
	/// <param name="targetType"></param>
	/// <param name="parameter"></param>
	/// <param name="culture"></param>
	/// <returns></returns>
	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is DateTime date) {
			return date.ToString(Format, CultureInfo.InvariantCulture);
		}
		return string.Empty;
	}
}
