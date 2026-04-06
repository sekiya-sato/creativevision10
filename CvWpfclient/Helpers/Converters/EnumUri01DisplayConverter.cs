using CvBase;
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class EnumUri01DisplayConverter : IValueConverter {
	static readonly Dictionary<EnumUri01, string> DisplayNames = new() {
		[EnumUri01.Uriage] = "売上",
		[EnumUri01.UriSale] = "セール売上",
		[EnumUri01.Henpin] = "返品",
		[EnumUri01.HenSale] = "セール返品",
		[EnumUri01.Other] = "その他",
	};

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is EnumUri01 kubun && DisplayNames.TryGetValue(kubun, out var name))
			return name;
		return value?.ToString() ?? string.Empty;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return Binding.DoNothing;
	}
}
