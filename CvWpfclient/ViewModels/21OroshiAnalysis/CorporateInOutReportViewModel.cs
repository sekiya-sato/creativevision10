using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 全社受払表。年月別在庫集計(SummaryStock)を全倉庫合計して、
/// 前月残・入庫・出庫・移動中・調整・当月残・棚卸数・棚卸差異を年月順に印字する。
/// 倉庫別受払表(08Zaiko)の全社版で、倉庫を跨いだ在庫の増減を1本の流れとして見る。
///
/// SummaryStock は在庫累計更新（月次更新処理・Phase 15）が作るので、
/// 更新していない年月は行が無く空になる。
/// 全社合計なので倉庫間移動は入庫と出庫の両方に計上され、差引としては相殺される。
/// </summary>
public partial class CorporateInOutReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "全社受払表";
	protected override string FormFileName => "CorporateInOutReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-11).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜36）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "12";

	/// <summary>true=金額（原価）も併記する / false=数量のみ。</summary>
	[ObservableProperty]
	public partial bool ShowKingaku { get; set; } = true;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 36) {
			MessageEx.ShowWarningDialog("出力月数は 1〜36 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var lastOffset = months - 1;
		// 数量のみ表示のときは金額欄を空文字にする（0埋めだと集計値と誤読されるため）
		var kingakuCol = ShowKingaku
			? "CAST(cumulativeSu * avgGenka AS INTEGER)"
			: "''";

		List<string> parameters = [];

		var sql = $@"
WITH RECURSIVE seq(n) AS (
    SELECT 0 UNION ALL SELECT n+1 FROM seq WHERE n < {lastOffset}
),
periods AS (
    SELECT
        strftime('%Y%m', date('{startDate}', '+' || n || ' months')) AS ym,
        strftime('%Y%m', date('{startDate}', '+' || n || ' months', '-1 month')) AS prevYm,
        n AS ord
    FROM seq
),
-- 全倉庫を合計する（倉庫間移動は入庫･出庫の両方に立つので差引では相殺される）
monthly AS (
    SELECT
        s.SumMonth          AS ym,
        SUM(s.InQty)        AS inQty,
        SUM(s.OutQty)       AS outQty,
        SUM(s.TransitQty)   AS transitQty,
        SUM(s.AdjustQty)    AS adjustQty,
        SUM(s.CumulativeSu) AS cumulativeSu,
        SUM(s.ActualQty)    AS actualQty,
        -- 平均原価: 在庫数で重み付けした商品マスタ原価
        CASE WHEN SUM(s.CumulativeSu) != 0
             THEN CAST(SUM(s.CumulativeSu * ifnull(sh.TankaGenka,0)) AS REAL) / SUM(s.CumulativeSu)
             ELSE 0 END     AS avgGenka
    FROM SummaryStock s
    LEFT JOIN MasterShohin sh ON sh.Id = s.Id_Shohin
    GROUP BY s.SumMonth
),
grid AS (
    SELECT
        p.ord, p.ym,
        ifnull(m.inQty, 0)         AS inQty,
        ifnull(m.outQty, 0)        AS outQty,
        ifnull(m.transitQty, 0)    AS transitQty,
        ifnull(m.adjustQty, 0)     AS adjustQty,
        ifnull(m.cumulativeSu, 0)  AS cumulativeSu,
        ifnull(m.actualQty, 0)     AS actualQty,
        ifnull(m.avgGenka, 0)      AS avgGenka,
        ifnull(pm.cumulativeSu, 0) AS prevSu
    FROM periods p
    LEFT JOIN monthly m  ON m.ym = p.ym
    LEFT JOIN monthly pm ON pm.ym = p.prevYm
)
SELECT
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    prevSu,
    inQty, outQty, transitQty, adjustQty,
    cumulativeSu,
    actualQty,
    actualQty - cumulativeSu AS tanaDiff,
    {kingakuCol} AS stockKingaku
FROM grid
ORDER BY ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
