/*
# description
SqlWhere は SQL の WHERE 句組み立てに関する純粋な処理を集めた静的ヘルパーです。

帳票基底(BaseReportViewModel)と照会基底(BaseQueryViewModel)の両方が同じ処理を必要とするため、
どちらかに実装を置いて他方がコピーする形を避けるためにここへ出しています。
UI(警告ダイアログ)を伴う入力検証は ActiveWindow が必要なので各基底クラス側に残しています。

# example
List<string> parameters = [];
var where = SqlWhere.CodeRange(parameters, "Code", codeFrom, codeTo);
 */
namespace CvWpfclient.Helpers;

internal static class SqlWhere {
	/// <summary>
	/// SQLへ埋め込むユーザ入力値を `@n` プレースホルダとして採番する。戻り値をそのままSQL文へ連結する。
	/// </summary>
	internal static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	/// <summary>
	/// コード範囲（〜）の AND 条件を組み立てる。空欄の側は条件を付けない。
	/// 先頭に " AND " が付くので、呼び出し側は "1=1" などから始める前提。
	/// </summary>
	internal static string CodeRange(List<string> parameters, string columnName, string? codeFrom, string? codeTo) {
		var where = "";
		if (!string.IsNullOrWhiteSpace(codeFrom)) {
			where += $" AND {columnName} >= {AddParameter(parameters, codeFrom.Trim())}";
		}
		if (!string.IsNullOrWhiteSpace(codeTo)) {
			where += $" AND {columnName} <= {AddParameter(parameters, codeTo.Trim())}";
		}
		return where;
	}
}
