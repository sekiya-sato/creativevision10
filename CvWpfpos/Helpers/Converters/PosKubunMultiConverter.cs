using System.Globalization;
using System.Windows.Data;

namespace CvWpfpos.Helpers;

/// <summary>
/// Tran01Tenuri の Kubun + PosClientSaleId を「売上/返品/取消」ラベルに変換。
/// MultiBinding で Kubun（int）と PosClientSaleId（string）を受け取る。
/// </summary>
public sealed class PosKubunMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not int kubun) return string.Empty;
        var posClientSaleId = values[1] as string ?? string.Empty;
        return kubun switch
        {
            10 or 11 => "売上",
            20 or 21 => posClientSaleId.EndsWith(":C", StringComparison.Ordinal) ? "取消" : "返品",
            _ => "その他"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
