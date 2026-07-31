using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 得意先別売上月報。卸売上を得意先×年月で集計し、前年同月比と累計を付けて印字する。
/// 得意先別売上日報の月次版。営業担当も併記して担当ごとの動きを追えるようにしている。
///
/// レイアウトは年月を1列持つ縦持ち(long形式)。理由は Phase 3b と同じ（qfmの見出しは静的テキスト）。
/// </summary>
public partial class TokuiSalesMonthlyReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "得意先別売上月報";
	protected override string FormFileName => "TokuiSalesMonthlyReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-11).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜36）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "12";

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=実績がある月のみ / false=実績0の月も行を出す。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

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
		// 前年同月比のため1年前から集計する
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start.AddYears(-1)));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "t.Code", TokuiCodeFrom, TokuiCodeTo);

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var lastOffset = months - 1;
		var activeOnly = IsActiveOnly ? "WHERE kingaku != 0 OR su != 0" : "";

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
tokui AS (
    SELECT t.Id, t.Code, t.Name, ifnull(sn.Code,'') AS shainCode, ifnull(sn.Name,'') AS shainName
    FROM MasterTokui t
    LEFT JOIN MasterShain sn ON sn.Id = t.Id_Shain
    WHERE t.TenType = 1 {tokuiWhere}
),
monthly AS (
    SELECT
        h.Id_Tokui           AS idTokui,
        substr(h.DenDay,1,6) AS ym,
        COUNT(*)             AS denCount,
        SUM(h.SuTotal)       AS su,
        SUM(h.KingakuTotal)  AS kingaku,
        SUM(h.JodaiTotal)    AS jodaiTotal,
        SUM(h.GedaiTotal)    AS gedaiTotal
    FROM Tran00Uriage h
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY h.Id_Tokui, substr(h.DenDay,1,6)
),
grid AS (
    SELECT
        t.Code AS tokuiCode, t.Name AS tokuiName,
        t.shainCode, t.shainName,
        p.ord, p.ym,
        ifnull(m.denCount, 0)   AS denCount,
        ifnull(m.su, 0)         AS su,
        ifnull(m.kingaku, 0)    AS kingaku,
        ifnull(m.jodaiTotal, 0) AS jodaiTotal,
        ifnull(m.gedaiTotal, 0) AS gedaiTotal,
        ifnull(pm.kingaku, 0)   AS prevKingaku
    FROM tokui t
    CROSS JOIN periods p
    LEFT JOIN monthly m  ON m.idTokui = t.Id AND m.ym = p.ym
    LEFT JOIN monthly pm ON pm.idTokui = t.Id AND pm.ym = p.prevYm
)
SELECT
    tokuiCode, tokuiName,
    shainName,
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    denCount, su, kingaku,
    CASE WHEN jodaiTotal != 0
         THEN ROUND(CAST(jodaiTotal - gedaiTotal AS REAL) / jodaiTotal * 100, 1)
         ELSE 0 END AS neireRatio,
    prevKingaku,
    CASE WHEN prevKingaku != 0 THEN ROUND(CAST(kingaku AS REAL) / prevKingaku * 100, 1) ELSE 0 END AS prevRatio,
    SUM(kingaku) OVER (PARTITION BY tokuiCode ORDER BY ord
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumKingaku
FROM grid
{activeOnly}
ORDER BY tokuiCode, ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
