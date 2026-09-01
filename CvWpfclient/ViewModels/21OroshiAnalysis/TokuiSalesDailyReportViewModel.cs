using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 得意先別売上日報。卸売上(Tran00Uriage)を得意先×日で集計し、
/// 件数・数量・金額・消費税・総額・値引・上代金額・値入率・累計を印字する。
///
/// 対象は卸売上のみ。店舗売上(Tran01Tenuri)は店舗向けの帳票(20UriageAnalysis)が担当する。
/// 値入率 = (上代金額 − 原価金額) ÷ 上代金額。原価は明細の下代(Gedai)を使う
/// （伝票時点の原価が入っているため、商品マスタの現在原価より実態に近い）。
/// </summary>
public partial class TokuiSalesDailyReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "得意先別売上日報";
	protected override string FormFileName => "TokuiSalesDailyReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=得意先別 / false=全得意先合計。</summary>
	[ObservableProperty]
	public partial bool IsByTokui { get; set; } = true;

	/// <summary>出力対象。true=売上がある日のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo);

		var tokuiCode = IsByTokui ? TranMeisaiSql.HeaderCode("VTokui") : "''";
		var tokuiName = IsByTokui ? TranMeisaiSql.HeaderName("VTokui") : "'全得意先'";
		var having = IsActiveOnly ? "HAVING SUM(h.KingakuTotal) != 0 OR SUM(h.SuTotal) != 0" : "";

		var sql = $@"
WITH agg AS (
    SELECT
        h.DenDay AS denDaySort,
        {PeriodSql.Label("h.DenDay", PeriodUnit.Day)} AS denDayLabel,
        {PeriodSql.Youbi("h.DenDay")}                 AS youbi,
        {tokuiCode} AS tokuiCode,
        {tokuiName} AS tokuiName,
        COUNT(*)             AS denCount,
        SUM(h.SuTotal)       AS su,
        SUM(h.KingakuTotal)  AS kingaku,
        SUM(h.Tax1+h.Tax2+h.Tax3)           AS tax,
        SUM(h.Nebiki00Total) AS nebiki,
        SUM(h.JodaiTotal)    AS jodaiTotal,
        SUM(h.GedaiTotal)    AS gedaiTotal
    FROM Tran00Uriage h
    WHERE {where}
    GROUP BY h.DenDay, {tokuiCode}, {tokuiName}
    {having}
)
SELECT
    denDayLabel, youbi,
    tokuiCode, tokuiName,
    denCount, su, kingaku, tax, nebiki, jodaiTotal,
    CASE WHEN jodaiTotal != 0
         THEN ROUND(CAST(jodaiTotal - gedaiTotal AS REAL) / jodaiTotal * 100, 1)
         ELSE 0 END AS neireRatio,
    SUM(kingaku) OVER (PARTITION BY tokuiCode ORDER BY denDaySort
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumKingaku
FROM agg
ORDER BY tokuiCode, denDaySort";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
