using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 品番別移動チェックリスト。指定期間の移動伝票明細を品番(商品×色×サイズ)別に集計し、
/// 出庫数・入庫数・差異(=出庫-入庫)・伝票数・最終移動日を印字する。
/// 差異が残る品番は「出したが受けていない」状態なので、移動未受リストと合わせて突合に使う。
/// 対象は出庫(Tran10IdoOut)・入庫(Tran11IdoIn)・即時移動(Tran05Ido)。
/// 即時移動は出庫と入庫が同時に立つため、出庫・入庫の両方へ同数を計上する。
/// </summary>
public partial class HinbanIdoCheckListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "品番別移動チェックリスト";
	protected override string FormFileName => "HinbanIdoCheckList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=色サイズ別 / false=商品計（色サイズを潰す）。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; } = true;

	/// <summary>即時移動(Tran05Ido)も対象にする</summary>
	[ObservableProperty]
	public partial bool IncludeSoku { get; set; } = true;

	/// <summary>出力対象。true=差異がある品番のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsDiffOnly { get; set; }

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	/// <summary>倉庫選択ダイアログ(TenType=0)。</summary>
	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("移動日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];

		var colCode = IsByColorSize ? TranMeisaiSql.Str("Code_Col") : "''";
		var colName = IsByColorSize ? TranMeisaiSql.Str("Mei_Col") : "''";
		var sizCode = IsByColorSize ? TranMeisaiSql.Str("Code_Siz") : "''";
		var sizName = IsByColorSize ? TranMeisaiSql.Str("Mei_Siz") : "''";

		// outExpr / inExpr で出庫側・入庫側のどちらへ数量を寄せるかを切り替える。
		// @n プレースホルダは出現順の採番なので、SQL片を組む時点で毎回採番し直す。
		string Source(string table, string prefix, string outExpr, string inExpr) {
			var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
				+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
				+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo)
				+ BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
			return $@"
    SELECT
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {colCode} AS colCode,
        {colName} AS colName,
        {sizCode} AS sizCode,
        {sizName} AS sizName,
        {outExpr} AS suOut,
        {inExpr}  AS suIn,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        '{prefix}' || h.Id AS denKey,
        h.DenDay AS denDay
    FROM {table} h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}";
		}

		var su = TranMeisaiSql.Num("Su");
		List<string> sources = [
			Source("Tran10IdoOut", "O", su, "0"),
			Source("Tran11IdoIn", "I", "0", su),
		];
		if (IncludeSoku) {
			// 即時移動は出庫と入庫が同一伝票で完結するため両側に同数を立てる（差異は常に0になる）。
			sources.Add(Source("Tran05Ido", "S", su, su));
		}

		var having = IsDiffOnly ? "HAVING SUM(suOut) - SUM(suIn) != 0" : "";

		var sql = $@"
WITH meisai AS (
{string.Join("\n    UNION ALL\n", sources)}
)
SELECT
    shohinCode, shohinName,
    colCode, colName,
    sizCode, sizName,
    SUM(suOut)                               AS suOut,
    SUM(suIn)                                AS suIn,
    SUM(suOut) - SUM(suIn)                   AS suDiff,
    COUNT(DISTINCT denKey)                   AS denCount,
    {TranMeisaiSql.DateLabel("MAX(denDay)")} AS lastDay,
    SUM(kingaku)                             AS kingaku
FROM meisai
GROUP BY shohinCode, shohinName, colCode, colName, sizCode, sizName
{having}
ORDER BY shohinCode, colCode, sizCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
