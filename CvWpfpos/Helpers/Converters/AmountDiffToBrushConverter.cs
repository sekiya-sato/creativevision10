using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CvWpfpos.Helpers;

/// <summary>精算差異値をブラシに変換（マイナス=赤、プラス=緑、ゼロ=黒）。</summary>
public sealed class AmountDiffToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int diff
            ? diff < 0 ? Brushes.Red : diff > 0 ? Brushes.Green : Brushes.Black
            : Brushes.Black;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
