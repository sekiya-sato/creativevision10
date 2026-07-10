/*
# description
MultiplyValuesConverter は複数の数値を乗算し、MultiBinding の表示値として返す IMultiValueConverter です。

# example
<TextBlock><TextBlock.Text><MultiBinding Converter="{StaticResource MultiplyValuesConverter}"><Binding Path="Quantity" /><Binding Path="Price" /></MultiBinding></TextBlock.Text></TextBlock>
 */
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class MultiplyValuesConverter : IMultiValueConverter {
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
		if (values.Length == 0) return 0;

		decimal result = 1;
		foreach (var value in values) {
			if (value == null || value == Binding.DoNothing || value == System.Windows.DependencyProperty.UnsetValue)
				return 0;
			if (!decimal.TryParse(value.ToString(), NumberStyles.Number, culture, out var number))
				return 0;
			result *= number;
		}
		return result;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
		return targetTypes.Select(_ => Binding.DoNothing).ToArray();
	}
}
