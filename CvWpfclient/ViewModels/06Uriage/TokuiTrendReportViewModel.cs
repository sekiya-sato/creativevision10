using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 得意先別売上推移表。指定年月から指定月数分、得意先×年月の推移（数量・金額・累計・前年同月比）を印字する。
///
/// レイアウトは「月を列に並べる」形ではなく「年月を1列持つ縦持ち(long形式)」にしている。
/// qfm の見出しは静的テキストしか持てず、開始年月に応じて列見出しを差し替えられないため、
/// 年月をデータ列にする方が指定月数を自由に変えられる。
///
/// 対象は卸売上(Tran00Uriage)。店舗売上は店舗別の帳票(20UriageAnalysis)が担当する。
/// </summary>
public partial class TokuiTrendReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "得意先別売上推移表";
	protected override string FormFileName => "TokuiTrendReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-11).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜36）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "12";

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>true=返品･値引も含める / false=売上(Kubun=10,11)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

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

		// 前年同月比のため、抽出範囲は「開始年月の1年前」から「終了年月末」まで広げる。
		var end = start.AddMonths(months - 1);
		var dataFrom = ToDenDay(start.AddYears(-1));
		var dataTo = ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month)));

		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, dataFrom);
		var dayTo = AddSqlParameter(parameters, dataTo);
		var tokuiWhere = BuildCodeRangeWhere(parameters, "Code", TokuiCodeFrom, TokuiCodeTo);

		const string Kingaku = "CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END";
		var kubunFilter = IncludeHenpin
			? ""
			: $" AND h.Kubun IN ({(int)EnumUri00.Uriage},{(int)EnumUri00.UriSale})";
		var activeOnly = IsActiveOnly ? "WHERE total != 0 OR su != 0" : "";

		// startDate は検証済みの DateTime 由来なのでSQLへ直接埋め込んでよい。
		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var lastOffset = months - 1;

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
    SELECT Id, Code, Name FROM MasterTokui
    WHERE TenType = 1 {tokuiWhere}
),
monthly AS (
    SELECT
        h.Id_Tokui AS idTori,
        substr(h.DenDay,1,6) AS ym,
        SUM(h.SuTotal)      AS su,
        SUM(h.KingakuTotal) AS kingaku,
        SUM(h.Tax)          AS tax,
        SUM({Kingaku})      AS total,
        COUNT(*)            AS denCount
    FROM Tran00Uriage h
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}{kubunFilter}
    GROUP BY h.Id_Tokui, substr(h.DenDay,1,6)
),
grid AS (
    SELECT
        t.Code AS toriCode, t.Name AS toriName,
        p.ord, p.ym,
        ifnull(m.su, 0)        AS su,
        ifnull(m.kingaku, 0)   AS kingaku,
        ifnull(m.tax, 0)       AS tax,
        ifnull(m.total, 0)     AS total,
        ifnull(m.denCount, 0)  AS denCount,
        ifnull(pm.total, 0)    AS prevTotal
    FROM tokui t
    CROSS JOIN periods p
    LEFT JOIN monthly m  ON m.idTori = t.Id AND m.ym = p.ym
    LEFT JOIN monthly pm ON pm.idTori = t.Id AND pm.ym = p.prevYm
)
SELECT
    toriCode, toriName,
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    su, kingaku, tax, total, denCount,
    SUM(total) OVER (PARTITION BY toriCode ORDER BY ord
                     ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumTotal,
    prevTotal,
    CASE WHEN prevTotal != 0
         THEN ROUND(CAST(total AS REAL) / prevTotal * 100, 1)
         ELSE 0 END AS prevRatio
FROM grid
{activeOnly}
ORDER BY toriCode, ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
