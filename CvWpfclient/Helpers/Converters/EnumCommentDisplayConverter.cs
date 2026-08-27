/*
# description
EnumCommentDisplayConverter は enum 値を、そのメンバーに付与された [Comment] 属性の文言へ変換する IValueConverter です。
属性が無い値は enum の ToString() をそのまま返します。

# example
<DataGridTextColumn Binding="{Binding EnKubun, Converter={StaticResource EnumCommentDisplayConverter}}" Header="区分" />
 */
using CvBase;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class EnumCommentDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		if (value is not Enum enumValue) return value?.ToString() ?? string.Empty;
		var field = enumValue.GetType().GetField(enumValue.ToString());
		var comment = field?.GetCustomAttributes(typeof(CommentAttribute), false).OfType<CommentAttribute>().FirstOrDefault();
		return comment?.Content ?? enumValue.ToString();
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return Binding.DoNothing;
	}
}
