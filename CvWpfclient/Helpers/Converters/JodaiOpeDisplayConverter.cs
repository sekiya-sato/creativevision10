/*
# description
JodaiOpeDisplayConverter は上代一括変更の抽出条件行(TranJodaiCond.Ope)の 0/1 を
「AND」「OR」の表示文字列へ変換する IValueConverter です。

# example
<TextBlock Text="{Binding Ope, Converter={StaticResource JodaiOpeDisplayConverter}}" />
 */
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class JodaiOpeDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return value is int ope && ope == 1 ? "OR" : "AND";
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return Binding.DoNothing;
	}
}
