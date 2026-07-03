using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

/// <summary>
/// 数値（または数値文字列）の符号に応じて前景色ブラシを返すコンバータ。
/// マイナスは赤、ゼロは淡色、プラスは既定色（UnsetValue）を返す。
/// DataGrid の在庫数など、値の正負を一目で判別させたい表示に使う。
/// </summary>
public sealed class NumericSignBrushConverter : IValueConverter {
	static readonly SolidColorBrush NegativeBrush = CreateFrozen(Color.FromRgb(0xD3, 0x2F, 0x2F));
	static readonly SolidColorBrush ZeroBrush = CreateFrozen(Color.FromRgb(0x9E, 0x9E, 0x9E));

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (!TryGetNumber(value, culture, out double number)) return DependencyProperty.UnsetValue;
		if (number < 0) return NegativeBrush;
		if (number == 0) return ZeroBrush;
		return DependencyProperty.UnsetValue;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		Binding.DoNothing;

	static bool TryGetNumber(object? value, CultureInfo culture, out double number) {
		number = 0;
		switch (value) {
			case null:
			case DBNull:
				return false;
			case double d:
				number = d;
				return true;
			case int i:
				number = i;
				return true;
			case long l:
				number = l;
				return true;
			case decimal m:
				number = (double)m;
				return true;
		}

		string text = value.ToString()?.Trim() ?? string.Empty;
		if (text.Length == 0) return false;
		return double.TryParse(text, NumberStyles.Any, culture, out number);
	}

	static SolidColorBrush CreateFrozen(Color color) {
		SolidColorBrush brush = new(color);
		brush.Freeze();
		return brush;
	}
}
