/*
# description
LoginRoleDisplayConverter は SysLogin.Id_Role（EnumLoginRole または数値）を、画面表示用のロール名へ変換する IValueConverter です。

# example
<TextBlock Text="{Binding Id_Role, Converter={StaticResource LoginRoleDisplayConverter}}" />
 */
using CvBase.Share;
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class LoginRoleDisplayConverter : IValueConverter {
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
		var role = value switch {
			EnumLoginRole enumRole => (int)enumRole,
			long longValue => (int)longValue,
			int intValue => intValue,
			_ => -1
		};
		return role switch {
			(int)EnumLoginRole.Standard => "0:標準",
			(int)EnumLoginRole.Shop => "1:店舗",
			(int)EnumLoginRole.Warehouse => "2:倉庫担当",
			< 0 => string.Empty,
			_ => role.ToString(CultureInfo.InvariantCulture)
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
		return Binding.DoNothing;
	}
}
