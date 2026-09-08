/*
# description
上代一括変更 ② 適用範囲（Scope）DataGridの読み取り専用セル表示用コンバータ群。
CvBase の EnumJodaiRangeType / EnumJodaiGroupAxis / EnumJodaiIncExc / EnumJodaiPriceMethod の値を
日本語表示へ変換する。編集用ComboBoxの選択肢は MasterJouDaiBulkChangeViewModel の
RangeTypeOptions 等（CodeOption）を使うため、ここでは表示専用（ConvertBackはしない）。

# example
<TextBlock Text="{Binding RangeType, Converter={StaticResource JodaiRangeTypeDisplayConverter}}" />
 */
using System.Globalization;
using System.Windows.Data;
using CvBase;

namespace CvWpfclient.Helpers;

public sealed class JodaiRangeTypeDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v ? (EnumJodaiRangeType)v switch {
			EnumJodaiRangeType.All => "全店",
			EnumJodaiRangeType.PriceGroup => "価格グループ",
			EnumJodaiRangeType.Store => "個別店舗",
			_ => v.ToString(CultureInfo.InvariantCulture),
		} : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JodaiGroupAxisDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v ? (EnumJodaiGroupAxis)v switch {
			EnumJodaiGroupAxis.PriceGroup => "価格グループ",
			EnumJodaiGroupAxis.PriceArea => "地域",
			EnumJodaiGroupAxis.PriceChannel => "チャネル",
			_ => v.ToString(CultureInfo.InvariantCulture),
		} : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JodaiIncExcDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v && (EnumJodaiIncExc)v == EnumJodaiIncExc.Exclude ? "除外" : "対象";

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JodaiPriceMethodDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v ? (EnumJodaiPriceMethod)v switch {
			EnumJodaiPriceMethod.FixedPrice => "固定額",
			EnumJodaiPriceMethod.RateOff => "値下率",
			EnumJodaiPriceMethod.Amount => "値引額",
			EnumJodaiPriceMethod.RateOn => "掛率",
			EnumJodaiPriceMethod.RateOffFromEffective => "実効上代からの値下率",
			EnumJodaiPriceMethod.PricePoint => "価格ポイント",
			_ => v.ToString(CultureInfo.InvariantCulture),
		} : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JodaiRoundUnitDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v ? v switch { 1 => "10円", 2 => "百円", 3 => "千円", _ => "1円" } : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JodaiRoundTypeDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v ? v switch { 1 => "四捨五入", 2 => "切上", _ => "切捨" } : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// <see cref="TranJodaiScope.RangeType"/>が価格グループ(1)のときだけtrueを返す。
/// 「軸」ComboBoxのIsEnabledに使う（全店・個別店舗のときは軸を選ばせない）。
/// </summary>
public sealed class JodaiIsPriceGroupConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int v && (EnumJodaiRangeType)v == EnumJodaiRangeType.PriceGroup;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
