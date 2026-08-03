/*
# description
V*列(CodeNameView)を「(Id) コード 名称」の一続きの文字列へ変換する共通コンバータ群です。
マスタ参照の表示は画面をまたいでこの書式に統一し、Id を主軸とした操作・表示に揃えます。

- CodeNameViewDisplayConverter : CodeNameView(V*列) を1つの表示文字列へ変換する IValueConverter。
- IdCodeNameDisplayConverter   : Id / Code / Name を個別に持つマスタ行を同じ書式へ揃える IMultiValueConverter。

いずれも空要素は書式から除外するため、未選択(Sid=0/空文字)なら空文字を返します。

## Id を出すか出さないかの使い分け **IMPORTANT**
- **検索TextBox(`SearchTextBoxAssist` で Id_* を編集する欄)が隣にある編集フォーム** → `ConverterParameter="NoId"` を付けて
  「コード 名称」のみ表示する。Id は隣のTextBoxが表示済みで、付けると二重表示になる。
- **一覧(DataGrid)列・選択ウィンドウのプレビューなど、Id 入力欄が無い箇所** → 既定のまま「(Id) コード 名称」を表示する。

# example
<!--  検索TextBox付き: コード 名称のみ  -->
<DockPanel>
	<TextBox DockPanel.Dock="Left"
		helpers:SearchTextBoxAssist.Command="{Binding DoSelectBrandCommand}"
		Style="{StaticResource MenteSearchTextBox}"
		Text="{Binding CurrentEdit.Id_Brand, UpdateSourceTrigger=PropertyChanged}" />
	<TextBlock Style="{StaticResource MasterRefText}"
		Text="{Binding CurrentEdit.VBrand, Converter={StaticResource CodeNameViewDisplayConverter}, ConverterParameter=NoId}" />
</DockPanel>

<!--  一覧列: (Id) コード 名称  -->
<DataGridTextColumn Binding="{Binding VBrand, Converter={StaticResource CodeNameViewDisplayConverter}, Mode=OneWay}"
	Header="ブランド" SortMemberPath="VBrand.Cd" />

<TextBlock>
	<TextBlock.Text>
		<MultiBinding Converter="{StaticResource IdCodeNameDisplayConverter}">
			<Binding Path="Current.Id" />
			<Binding Path="Current.Code" />
			<Binding Path="Current.Name" />
		</MultiBinding>
	</TextBlock.Text>
</TextBlock>
 */
using System.Globalization;
using System.Windows.Data;

using CvBase;

namespace CvWpfclient.Helpers;

/// <summary>
/// V*列(CodeNameView)共通表示の書式化ヘルパ。
/// </summary>
public static class CodeNameDisplay {
	/// <summary>Id を省略する場合に ConverterParameter へ渡す値。</summary>
	public const string NoIdParameter = "NoId";

	/// <summary>
	/// 「(Id) コード 名称」を組み立てる。空の要素は書式から除外する。
	/// </summary>
	public static string Format(long id, string? code, string? name, bool withId = true) {
		var cd = code?.Trim() ?? string.Empty;
		var mei = name?.Trim() ?? string.Empty;
		// 未選択(Id未設定かつコード・名称なし)は空表示にして、"(0)" の表示を避ける
		if (id == 0 && cd.Length == 0 && mei.Length == 0) return string.Empty;

		var parts = new List<string>(3);
		if (withId && id != 0) parts.Add($"({id.ToString(CultureInfo.InvariantCulture)})");
		if (cd.Length > 0) parts.Add(cd);
		if (mei.Length > 0) parts.Add(mei);
		return string.Join(" ", parts);
	}

	/// <summary>ConverterParameter が Id 省略指定かどうか。</summary>
	public static bool IsNoId(object? parameter) =>
		parameter is string text && string.Equals(text.Trim(), NoIdParameter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// CodeNameView(V*列) を「(Id) コード 名称」の1文字列へ変換する。
/// </summary>
public class CodeNameViewDisplayConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is not CodeNameView view) return string.Empty;
		return CodeNameDisplay.Format(view.Sid, view.Cd, view.Mei, !CodeNameDisplay.IsNoId(parameter));
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}

/// <summary>
/// Id / Code / Name を個別に持つマスタ行を「(Id) コード 名称」へ変換する。
/// MultiBinding の順序は Id, Code, Name。
/// </summary>
public class IdCodeNameDisplayConverter : IMultiValueConverter {
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
		if (values == null || values.Length < 3) return string.Empty;
		var id = values[0] switch {
			long l => l,
			int i => i,
			string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => 0L,
		};
		return CodeNameDisplay.Format(id, values[1] as string, values[2] as string, !CodeNameDisplay.IsNoId(parameter));
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}
