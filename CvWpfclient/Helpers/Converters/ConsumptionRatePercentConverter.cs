/*
# description
ConsumptionRatePercentConverter は MasterShohin.ConsumptionRateBasisPoints（1/100%単位。6500=65.00%）と
編集画面の%入力欄（"65.00"）を相互変換する IValueConverter です。
DBには1/100%単位のまま保存し、利用者には自然な%単位で入力させるための往復変換
（原価4項目 詳細設計 §2.5.8・§4.2）。純粋な変換ロジックは CvBase.CostPreviewDisplay に切り出してあり、
本コンバータはそれをXAMLバインディングへ橋渡しするだけの薄いラッパー。

# example
<TextBox Text="{Binding CurrentEdit.ConsumptionRateBasisPoints, Converter={StaticResource ConsumptionRatePercentConverter}}" />
 */
using CvBase;
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class ConsumptionRatePercentConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is int rateBasisPoints
			? CostPreviewDisplay.ConsumptionRateBasisPointsToPercent(rateBasisPoints).ToString("0.00", CultureInfo.InvariantCulture)
			: string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		CostPreviewDisplay.TryParseConsumptionRatePercent(value as string, out var rateBasisPoints)
			? rateBasisPoints
			: Binding.DoNothing;
}
