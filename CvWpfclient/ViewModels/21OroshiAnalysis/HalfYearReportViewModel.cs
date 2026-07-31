using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 半期報。全社の半期（6ヶ月）実績を月別に並べ、卸売上・店舗売上・合計・前年同月比・累計を印字する。
/// 担当別売上実績半期報が担当軸なのに対し、こちらは全社サマリ。経営報告の1枚目に使う想定。
///
/// 卸売上(Tran00Uriage)と店舗売上(Tran01Tenuri)は別テーブルなので、月別に集計してから足し合わせる。
/// 起点月を指定して6ヶ月。上期/下期の区切りは会社ごとに違うので固定していない。
/// </summary>
public partial class HalfYearReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "半期報";
	protected override string FormFileName => "HalfYearReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-5).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>true=卸売上を含める</summary>
	[ObservableProperty]
	public partial bool IncludeOroshi { get; set; } = true;

	/// <summary>true=店舗売上を含める</summary>
	[ObservableProperty]
	public partial bool IncludeShop { get; set; } = true;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!IncludeOroshi && !IncludeShop) {
			MessageEx.ShowWarningDialog("卸売上・店舗売上のどちらかを選択してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		const int Months = 6;
		var end = start.AddMonths(Months - 1);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start.AddYears(-1)));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		// 対象外の売上区分は 0 を積む（テーブル自体は常に集計してから重み付けする）
		var oroshiWeight = IncludeOroshi ? "1" : "0";
		var shopWeight = IncludeShop ? "1" : "0";

		var sql = $@"
WITH RECURSIVE seq(n) AS (
    SELECT 0 UNION ALL SELECT n+1 FROM seq WHERE n < {Months - 1}
),
periods AS (
    SELECT
        strftime('%Y%m', date('{startDate}', '+' || n || ' months')) AS ym,
        strftime('%Y%m', date('{startDate}', '+' || n || ' months', '-1 year')) AS prevYm,
        n AS ord
    FROM seq
),
oroshi AS (
    SELECT substr(DenDay,1,6) AS ym,
           COUNT(*) AS denCount, SUM(SuTotal) AS su, SUM(KingakuTotal) AS kingaku
    FROM Tran00Uriage
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY substr(DenDay,1,6)
),
shop AS (
    SELECT substr(DenDay,1,6) AS ym,
           COUNT(*) AS denCount, SUM(SuTotal) AS su, SUM(KingakuTotal) AS kingaku
    FROM Tran01Tenuri
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY substr(DenDay,1,6)
),
merged AS (
    SELECT
        ym,
        SUM(oroshiKingaku) AS oroshiKingaku,
        SUM(shopKingaku)   AS shopKingaku,
        SUM(su)            AS su,
        SUM(denCount)      AS denCount
    FROM (
        SELECT ym, kingaku * {oroshiWeight} AS oroshiKingaku, 0 AS shopKingaku,
               su * {oroshiWeight} AS su, denCount * {oroshiWeight} AS denCount FROM oroshi
        UNION ALL
        SELECT ym, 0 AS oroshiKingaku, kingaku * {shopWeight} AS shopKingaku,
               su * {shopWeight} AS su, denCount * {shopWeight} AS denCount FROM shop
    )
    GROUP BY ym
),
grid AS (
    SELECT
        p.ord, p.ym,
        ifnull(m.oroshiKingaku, 0) AS oroshiKingaku,
        ifnull(m.shopKingaku, 0)   AS shopKingaku,
        ifnull(m.oroshiKingaku, 0) + ifnull(m.shopKingaku, 0) AS totalKingaku,
        ifnull(m.su, 0)            AS su,
        ifnull(m.denCount, 0)      AS denCount,
        ifnull(pm.oroshiKingaku, 0) + ifnull(pm.shopKingaku, 0) AS prevTotal
    FROM periods p
    LEFT JOIN merged m  ON m.ym = p.ym
    LEFT JOIN merged pm ON pm.ym = p.prevYm
)
SELECT
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    denCount, su,
    oroshiKingaku, shopKingaku, totalKingaku,
    prevTotal,
    CASE WHEN prevTotal != 0 THEN ROUND(CAST(totalKingaku AS REAL) / prevTotal * 100, 1) ELSE 0 END AS prevRatio,
    SUM(totalKingaku) OVER (ORDER BY ord ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumTotal,
    CASE WHEN totalKingaku != 0
         THEN ROUND(CAST(oroshiKingaku AS REAL) / totalKingaku * 100, 1)
         ELSE 0 END AS oroshiShare
FROM grid
ORDER BY ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
