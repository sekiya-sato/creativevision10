/*
# description
DataRowColumnIsDbNullConverter は DataRowView と列名(ConverterParameter)を受け取り、
その列の値が DBNull(または null)かどうかを bool で返す IValueConverter です。
DataTable を DefaultView 経由で DataGrid にバインドする画面で、値が無いセルをスタイルの
DataTrigger でグレー表示にする、といった用途を想定する。
コード側で動的に Binding/Style を組み立てて使うため、App.xaml への登録は不要（StaticResource では使わない）。

# example
column.CellStyle.Triggers.Add(new DataTrigger {
    Binding = new Binding(".") { Converter = new DataRowColumnIsDbNullConverter(), ConverterParameter = "予算計" },
    Value = true,
});
 */
using System.Data;
using System.Globalization;
using System.Windows.Data;

namespace CvWpfclient.Helpers;

public sealed class DataRowColumnIsDbNullConverter : IValueConverter {
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
		if (value is not DataRowView row) return false;
		string columnName = parameter as string ?? string.Empty;
		if (columnName.Length == 0 || !row.Row.Table.Columns.Contains(columnName)) return false;

		object cell = row[columnName];
		return cell is null or DBNull;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
		throw new NotSupportedException();
	}
}
