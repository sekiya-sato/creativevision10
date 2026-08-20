namespace CvBase;

/// <summary>
/// 親（請求先／支払先＝<c>Id_Paysaki</c>）と子（得意先／仕入先）の締日不一致を検出するためのSQL結果行。
/// Msg101_Op_Query の ItemType としてクライアント・サーバーの双方で解決できる共有DTOである。
/// </summary>
public sealed class PaysakiClosingCheckRow {
	public string ChildCode { get; set; } = string.Empty;
	public string ParentCode { get; set; } = string.Empty;
	public int ChildShime { get; set; }
	public int ParentShime { get; set; }
}

/// <summary>
/// 親子締日不一致（E7）の検査ロジックと警告文を一元化する。
/// 得意先マスタ（MasterTokui：請求先）・仕入先マスタ（MasterShiire：支払先）の双方で共有する。
/// ブロックはしない（決定事項）：検出しても保存・計算実行は継続させ、気付き用の警告のみ表示する。
/// </summary>
public static class PaysakiClosingCheck {
	public const string MismatchGuidance = "マスタ変更および請求再計算が必要です。";

	/// <summary>
	/// 計算画面（請求計算／支払計算）向け：締日・コード範囲で絞り込んだ不一致検査SQL。
	/// </summary>
	public static string BuildRangeCheckSql(string tableName, string where) => $"""
SELECT c.Code AS ChildCode, p.Code AS ParentCode, c.Shime1 AS ChildShime, p.Shime1 AS ParentShime
FROM {tableName} AS c
INNER JOIN {tableName} AS p ON p.Id = c.Id_Paysaki
{where}
ORDER BY c.Code
""";

	/// <summary>
	/// マスタメンテ画面向け：保存した1件を軸に双方向（子として／親として）で不一致検査するSQL。
	/// 親の締日を変更した場合は子側からしか検出できないため、双方向のUNIONで検査する。
	/// </summary>
	public static string BuildAffectedRowCheckSql(string tableName, long editedId) => $"""
SELECT c.Code AS ChildCode, p.Code AS ParentCode, c.Shime1 AS ChildShime, p.Shime1 AS ParentShime
FROM {tableName} AS c
INNER JOIN {tableName} AS p ON p.Id = c.Id_Paysaki
WHERE c.Id_Paysaki <> 0 AND p.Shime1 <> c.Shime1 AND (c.Id = {editedId} OR p.Id = {editedId})
ORDER BY c.Code
""";

	public static List<PaysakiClosingCheckRow> FindMismatches(IEnumerable<PaysakiClosingCheckRow> rows) =>
		[.. rows.Where(row => row.ParentShime != row.ChildShime)];

	public static string BuildMismatchWarning(string parentLabel, string childLabel, IReadOnlyList<PaysakiClosingCheckRow> mismatches) {
		if (mismatches.Count == 0) return string.Empty;
		var samples = string.Join("、", mismatches.Take(5).Select(x => $"{x.ChildCode}→{x.ParentCode}"));
		var remain = mismatches.Count > 5 ? $" ほか{mismatches.Count - 5}件" : string.Empty;
		return $"{parentLabel}（親）と{childLabel}の締日が異なるデータがあります: {samples}{remain}\n{MismatchGuidance}";
	}
}
