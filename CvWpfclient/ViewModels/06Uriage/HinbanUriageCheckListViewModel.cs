using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 品番別売上チェックリスト。指定期間の売上伝票明細を品番(商品×色×サイズ)別に集計し、
/// 数量・金額・上代・伝票数・最終売上日・平均単価を印字する。
/// 売上は卸売上(Tran00Uriage)と店舗売上(Tran01Tenuri)に分かれているため、対象を選べる。
/// </summary>
public partial class HinbanUriageCheckListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "品番別売上チェックリスト";
	protected override string FormFileName => "HinbanUriageCheckList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>卸売上(Tran00Uriage)を対象にする</summary>
	[ObservableProperty]
	public partial bool IncludeOroshi { get; set; } = true;

	/// <summary>店舗売上(Tran01Tenuri)を対象にする</summary>
	[ObservableProperty]
	public partial bool IncludeShop { get; set; } = true;

	/// <summary>集計単位。true=色サイズ別 / false=商品計（色サイズを潰す）。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; } = true;

	/// <summary>true=返品･値引も含める / false=売上(Kubun=10,11)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("売上日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!IncludeOroshi && !IncludeShop) {
			MessageEx.ShowWarningDialog("卸売上・店舗売上のどちらかを選択してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		// 期間と商品範囲は両テーブルへ同じ条件を掛けるが、@n プレースホルダは
		// 出現順に採番されるため、SQL片ごとに採番し直す（同じ値を2回登録する）。
		var shohinRange = () => BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		var dayRange = () => $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";

		var colCode = IsByColorSize ? TranMeisaiSql.Str("Code_Col") : "''";
		var colName = IsByColorSize ? TranMeisaiSql.Str("Mei_Col") : "''";
		var sizCode = IsByColorSize ? TranMeisaiSql.Str("Code_Siz") : "''";
		var sizName = IsByColorSize ? TranMeisaiSql.Str("Mei_Siz") : "''";

		// 卸/店舗で伝票Noが重複するため、伝票数のカウント用キーへテーブル種別を混ぜる。
		string Source(string table, string prefix, string kubunFilter) => $@"
    SELECT
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {colCode} AS colCode,
        {colName} AS colName,
        {sizCode} AS sizCode,
        {sizName} AS sizName,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        '{prefix}' || h.Id AS denKey,
        h.DenDay AS denDay
    FROM {table} h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {dayRange()}{shohinRange()}{kubunFilter}";

		List<string> sources = [];
		if (IncludeOroshi) {
			var filter = IncludeHenpin ? "" : $" AND h.Kubun IN ({(int)EnumUri00.Uriage},{(int)EnumUri00.UriSale})";
			sources.Add(Source("Tran00Uriage", "A", filter));
		}
		if (IncludeShop) {
			var filter = IncludeHenpin ? "" : $" AND h.Kubun IN ({(int)EnumUri01.Uriage},{(int)EnumUri01.UriSale})";
			sources.Add(Source("Tran01Tenuri", "B", filter));
		}

		var sql = $@"
WITH meisai AS (
{string.Join("\n    UNION ALL\n", sources)}
)
SELECT
    shohinCode, shohinName,
    colCode, colName,
    sizCode, sizName,
    SUM(su)                                  AS su,
    SUM(kingaku)                             AS kingaku,
    SUM(su * jodai)                          AS jodaiTotal,
    COUNT(DISTINCT denKey)                   AS denCount,
    {TranMeisaiSql.DateLabel("MAX(denDay)")} AS lastDay,
    CASE WHEN SUM(su) != 0
         THEN CAST(ROUND(CAST(SUM(kingaku) AS REAL) / SUM(su)) AS INTEGER)
         ELSE 0 END                          AS avgTanka
FROM meisai
GROUP BY shohinCode, shohinName, colCode, colName, sizCode, sizName
ORDER BY shohinCode, colCode, sizCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
