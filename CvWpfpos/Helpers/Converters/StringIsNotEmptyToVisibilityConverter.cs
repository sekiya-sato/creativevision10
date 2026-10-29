/*
# description
StringIsNotEmptyToVisibilityConverter は空白以外の文字列を Visible、空または null を Collapsed へ変換する IValueConverter です。

# example
<TextBlock Visibility="{Binding StatusMessage, Converter={StaticResource StringIsNotEmptyToVisibilityConverter}}" />
 */
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CvWpfpos.Helpers;

public sealed class StringIsNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string str && !string.IsNullOrWhiteSpace(str) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
