using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 卸・店舗売上実績表。卸売上(Tran00Uriage)と店舗売上(Tran01Tenuri)を年月別に並べ、
/// それぞれの数量・金額・構成比と合計・前年同月比を印字する。販路ミックスの推移を見る。
///
/// 半期報が6ヶ月固定の全社サマリなのに対し、こちらは月数を自由に指定でき、
/// 卸と店舗の構成比を明示的に出す点が違う。
/// 2テーブルを月別に集計してから外側で突き合わせる（テーブルを跨いだ結合はしない）。
/// </summary>
public partial class OroshiShopSalesActualReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "卸・店舗売上実績表";
	protected override string FormFileName => "OroshiShopSalesActualReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-11).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜36）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "12";

	/// <summary>出力対象。true=実績がある月のみ / false=実績0の月も行を出す。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 36) {
			MessageEx.ShowWarningDialog("出力月数は 1〜36 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var end = start.AddMonths(months - 1);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start.AddYears(-1)));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var lastOffset = months - 1;
		var activeOnly = IsActiveOnly ? "WHERE totalKingaku != 0" : "";

		var sql = $@"
WITH RECURSIVE seq(n) AS (
    SELECT 0 UNION ALL SELECT n+1 FROM seq WHERE n < {lastOffset}
),
periods AS (
    SELECT
        strftime('%Y%m', date('{startDate}', '+' || n || ' months')) AS ym,
        strftime('%Y%m', date('{startDate}', '+' || n || ' months', '-1 year')) AS prevYm,
        n AS ord
    FROM seq
),
oroshi AS (
    SELECT substr(DenDay,1,6) AS ym, SUM(SuTotal) AS su, SUM(KingakuTotal) AS kingaku
    FROM Tran00Uriage
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY substr(DenDay,1,6)
),
shop AS (
    SELECT substr(DenDay,1,6) AS ym, SUM(SuTotal) AS su, SUM(KingakuTotal) AS kingaku
    FROM Tran01Tenuri
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY substr(DenDay,1,6)
),
grid AS (
    SELECT
        p.ord, p.ym,
        ifnull(o.su, 0)      AS oroshiSu,
        ifnull(o.kingaku, 0) AS oroshiKingaku,
        ifnull(s.su, 0)      AS shopSu,
        ifnull(s.kingaku, 0) AS shopKingaku,
        ifnull(o.kingaku, 0) + ifnull(s.kingaku, 0) AS totalKingaku,
        ifnull(po.kingaku, 0) + ifnull(ps.kingaku, 0) AS prevTotal
    FROM periods p
    LEFT JOIN oroshi o  ON o.ym = p.ym
    LEFT JOIN shop s    ON s.ym = p.ym
    LEFT JOIN oroshi po ON po.ym = p.prevYm
    LEFT JOIN shop ps   ON ps.ym = p.prevYm
)
SELECT
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    oroshiSu, oroshiKingaku,
    CASE WHEN totalKingaku != 0
         THEN ROUND(CAST(oroshiKingaku AS REAL) / totalKingaku * 100, 1)
         ELSE 0 END AS oroshiShare,
    shopSu, shopKingaku,
    CASE WHEN totalKingaku != 0
         THEN ROUND(CAST(shopKingaku AS REAL) / totalKingaku * 100, 1)
         ELSE 0 END AS shopShare,
    totalKingaku,
    prevTotal,
    CASE WHEN prevTotal != 0 THEN ROUND(CAST(totalKingaku AS REAL) / prevTotal * 100, 1) ELSE 0 END AS prevRatio,
    SUM(totalKingaku) OVER (ORDER BY ord ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumTotal
FROM grid
{activeOnly}
ORDER BY ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
