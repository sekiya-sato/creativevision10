/*
# description
TranKubunBrushConverter は伝票の取引区分(Kubun)に応じて総合計などの前景色ブラシを返す IValueConverter です。
返品系(20-29)は赤、それ以外(通常10-19 を含む)は既定色(UnsetValue=継承色)を返します。

# example
<TextBlock Foreground="{Binding CurrentEdit.Kubun, Converter={StaticResource TranKubunBrushConverter}}" />
 */
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

/// <summary>
/// 伝票の取引区分(Kubun)に応じて前景色ブラシを返すコンバータ。
/// 20-29(返品系)は赤、それ以外(10-19 の通常取引を含む)は既定色(UnsetValue=継承色)を返す。
/// 総合計など、返品伝票を一目で判別させたい表示に使う。
/// </summary>
public sealed class TranKubunBrushConverter : IValueConverter {
	static readonly SolidColorBrush FallbackReturnBrush = CreateFrozen(Color.FromRgb(0xD3, 0x2F, 0x2F));

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (!TryGetInt(value, culture, out int kubun)) return DependencyProperty.UnsetValue;
		// 20-29 = 返品系 → 赤。10-19(通常取引)・その他 → 既定色(継承色=MaterialDesignBody)。
		if (kubun is >= 20 and <= 29) return FindBrush("NegativeForegroundBrush") ?? FallbackReturnBrush;
		return DependencyProperty.UnsetValue;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		Binding.DoNothing;

	static bool TryGetInt(object? value, CultureInfo culture, out int number) {
		number = 0;
		switch (value) {
			case null:
			case DBNull:
				return false;
			case int i:
				number = i;
				return true;
			case long l:
				number = (int)l;
				return true;
			case Enum e:
				number = System.Convert.ToInt32(e, culture);
				return true;
		}

		string text = value.ToString()?.Trim() ?? string.Empty;
		if (text.Length == 0) return false;
		return int.TryParse(text, NumberStyles.Any, culture, out number);
	}

	static Brush? FindBrush(string key) {
		var resource = Application.Current?.TryFindResource(key);
		return resource switch {
			Brush brush => brush,
			Color color => CreateFrozen(color),
			_ => null
		};
	}

	static SolidColorBrush CreateFrozen(Color color) {
		SolidColorBrush brush = new(color);
		brush.Freeze();
		return brush;
	}
}
