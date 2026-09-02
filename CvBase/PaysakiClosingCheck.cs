namespace CvBase;

/// <summary>
/// 親（請求先／支払先＝<c>Id_Paysaki</c>）と子（得意先／仕入先）の締日不一致を検出するためのSQL結果行。
/// Msg101_Op_Query の ItemType としてクライアント・サーバーの双方で解決できる共有DTOである。
/// 締日1/2/3の全列と自社締日(<c>OwnShime</c>)を持ち、<see cref="ClosingDaySet.Resolve"/> による
/// 有効締日集合の比較へ渡す(4.5)。
/// </summary>
public sealed class PaysakiClosingCheckRow {
	public string ChildCode { get; set; } = string.Empty;
	public string ParentCode { get; set; } = string.Empty;
	public int ChildShime1 { get; set; }
	public int ChildShime2 { get; set; }
	public int ChildShime3 { get; set; }
	public int ParentShime1 { get; set; }
	public int ParentShime2 { get; set; }
	public int ParentShime3 { get; set; }
	public int OwnShime { get; set; }
}

/// <summary>親子の有効締日集合が一致しない1件。警告文はこの集合を並べて表示する(4.5)。</summary>
public sealed record PaysakiClosingMismatch(string ChildCode, string ParentCode, IReadOnlyList<int> ChildDays, IReadOnlyList<int> ParentDays);

/// <summary>
/// 親子締日不一致（E7）の検査ロジックと警告文を一元化する。
/// 得意先マスタ（MasterTokui：請求先）・仕入先マスタ（MasterShiire：支払先）の双方で共有する。
/// ブロックはしない（決定事項）：検出しても保存・計算実行は継続させ、気付き用の警告のみ表示する。
/// </summary>
public static class PaysakiClosingCheck {
	public const string MismatchGuidance = "マスタ変更および請求再計算が必要です。";

	/// <summary>
	/// 計算画面（請求計算／支払計算）向け：締日・コード範囲で絞り込んだ不一致検査SQL。
	/// 一致判定は集合比較のためC#側で行う。ここでは候補行を取得するだけで、締日の不一致は絞らない。
	/// </summary>
	public static string BuildRangeCheckSql(string tableName, string where) => $"""
SELECT c.Code AS ChildCode, p.Code AS ParentCode,
	c.Shime1 AS ChildShime1, c.Shime2 AS ChildShime2, c.Shime3 AS ChildShime3,
	p.Shime1 AS ParentShime1, p.Shime2 AS ParentShime2, p.Shime3 AS ParentShime3,
	{ClosingDaySet.OwnShimeSubquerySql} AS OwnShime
FROM {tableName} AS c
INNER JOIN {tableName} AS p ON p.Id = c.Id_Paysaki
{where}
ORDER BY c.Code
""";

	/// <summary>
	/// マスタメンテ画面向け：保存した1件を軸に双方向（子として／親として）で検査対象行を取得するSQL。
	/// 親の締日を変更した場合は子側からしか検出できないため、双方向のUNION相当(OR条件)で取得する。
	/// 一致判定は集合比較のためC#側で行う。
	/// </summary>
	public static string BuildAffectedRowCheckSql(string tableName, long editedId) => $"""
SELECT c.Code AS ChildCode, p.Code AS ParentCode,
	c.Shime1 AS ChildShime1, c.Shime2 AS ChildShime2, c.Shime3 AS ChildShime3,
	p.Shime1 AS ParentShime1, p.Shime2 AS ParentShime2, p.Shime3 AS ParentShime3,
	{ClosingDaySet.OwnShimeSubquerySql} AS OwnShime
FROM {tableName} AS c
INNER JOIN {tableName} AS p ON p.Id = c.Id_Paysaki
WHERE c.Id_Paysaki <> 0 AND (c.Id = {editedId} OR p.Id = {editedId})
ORDER BY c.Code
""";

	/// <summary>
	/// 親子の有効締日集合(<see cref="ClosingDaySet.Resolve"/>)が一致しない行を抽出する。
	/// 要素の順序違いは一致扱いとする（集合として比較する）。
	/// </summary>
	public static List<PaysakiClosingMismatch> FindMismatches(IEnumerable<PaysakiClosingCheckRow> rows) {
		List<PaysakiClosingMismatch> mismatches = [];
		foreach (var row in rows) {
			var childDays = ClosingDaySet.Resolve(row.ChildShime1, row.ChildShime2, row.ChildShime3, row.OwnShime);
			var parentDays = ClosingDaySet.Resolve(row.ParentShime1, row.ParentShime2, row.ParentShime3, row.OwnShime);
			if (new HashSet<int>(childDays).SetEquals(parentDays)) continue;
			mismatches.Add(new PaysakiClosingMismatch(row.ChildCode, row.ParentCode, childDays, parentDays));
		}
		return mismatches;
	}

	public static string BuildMismatchWarning(string parentLabel, string childLabel, IReadOnlyList<PaysakiClosingMismatch> mismatches) {
		if (mismatches.Count == 0) return string.Empty;
		var samples = string.Join("、", mismatches.Take(5).Select(x =>
			$"{x.ChildCode}({ClosingDaySet.FormatDays(x.ChildDays)})→{x.ParentCode}({ClosingDaySet.FormatDays(x.ParentDays)})"));
		var remain = mismatches.Count > 5 ? $" ほか{mismatches.Count - 5}件" : string.Empty;
		return $"{parentLabel}（親）と{childLabel}の締日が異なるデータがあります: {samples}{remain}\n{MismatchGuidance}";
	}
}
