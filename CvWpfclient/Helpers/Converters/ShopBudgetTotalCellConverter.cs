/*
# description
ShopBudgetTotalCellConverter は DailyShopBudgetQueryView の合計グリッド専用の表示整形コンバータです。
合計 DataTable(TotalTable) は5行(売上計/予算計/予算比/前年売上計/前年売上比)を持ち、店舗列は
double型で行ごとに「金額」と「比率」が混在する。列単独の StringFormat では行ごとの書式切替が
できないため、DataRowView 全体(Path=".")を受け取り、ConverterParameter の列名と「日付」列
(行ラベル)の両方を見て書式を決める。
DBNullの場合は空文字を返す（背景色は別途 DataRowColumnIsDbNullConverter 側でグレー表示にする）。
コード側で動的に Binding を組み立てて使うため、App.xaml への登録は不要（StaticResource では使わない）。

# example
column.Binding = new Binding(".") { Converter = new ShopBudgetTotalCellConverter(), ConverterParameter = "予算比" };
 */
using System.Data;
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class ShopBudgetTotalCellConverter : IValueConverter {
	static readonly string[] PercentColumns = ["予算比", "前年売上比"];
	static readonly string[] KnownColumns = ["日付", "曜日", "売上計", "予算計", "予算比", "前年売上計", "前年売上比"];

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is not DataRowView row) return string.Empty;
		string columnName = parameter as string ?? string.Empty;
		if (columnName.Length == 0 || !row.Row.Table.Columns.Contains(columnName)) return string.Empty;

		object cell = row[columnName];
		if (cell is null or DBNull) return string.Empty;
		if (cell is not double number) return cell.ToString() ?? string.Empty;

		bool isShopColumn = !KnownColumns.Contains(columnName);
		bool usePercentFormat = PercentColumns.Contains(columnName) || (isShopColumn && IsPercentRow(row));
		return usePercentFormat
			? number.ToString("0.0", culture)
			: number.ToString("#,##0", culture);
	}

	static bool IsPercentRow(DataRowView row) {
		if (!row.Row.Table.Columns.Contains("日付")) return false;
		var label = row["日付"] as string;
		return label is "予算比" or "前年売上比";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}
