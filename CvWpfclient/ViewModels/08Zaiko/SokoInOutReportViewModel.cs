using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 倉庫別受払表。年月別在庫集計(SummaryStock)を倉庫単位に合計し、
/// 前月残・入庫・出庫・移動中・調整・当月残・棚卸数・棚卸差異を年月順に印字する。
///
/// 前月残は前月行の CumulativeSu（累計在庫）を引く。当月残も CumulativeSu。
/// SummaryStock は在庫累計更新（月次更新処理・Phase 15）が作るので、
/// 更新していない年月は行が無く空になる。
/// </summary>
public partial class SokoInOutReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "倉庫別受払表";
	protected override string FormFileName => "SokoInOutReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-5).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜24）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "6";

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=動きまたは残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 24) {
			MessageEx.ShowWarningDialog("出力月数は 1〜24 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var sokoWhere = BuildCodeRangeWhere(parameters, StockSql.SokoCode(), SokoCodeFrom, SokoCodeTo);

		// 前月残のため1ヶ月前から集計する。startDate は検証済みなので直接埋め込む。
		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var lastOffset = months - 1;
		var activeOnly = IsActiveOnly
			? "WHERE inQty != 0 OR outQty != 0 OR adjustQty != 0 OR cumulativeSu != 0 OR prevSu != 0"
			: "";

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
monthly AS (
    SELECT
        {StockSql.SokoCode()} AS sokoCode,
        {StockSql.SokoName()} AS sokoName,
        s.SumMonth            AS ym,
        SUM(s.InQty)          AS inQty,
        SUM(s.OutQty)         AS outQty,
        SUM(s.TransitQty)     AS transitQty,
        SUM(s.AdjustQty)      AS adjustQty,
        SUM(s.CumulativeSu)   AS cumulativeSu,
        SUM(s.ActualQty)      AS actualQty
    FROM SummaryStock s
{StockSql.JoinSoko()}
    WHERE 1=1 {sokoWhere}
    GROUP BY sokoCode, sokoName, s.SumMonth
),
sokos AS (
    SELECT DISTINCT sokoCode, sokoName FROM monthly
),
grid AS (
    SELECT
        k.sokoCode, k.sokoName, p.ord, p.ym,
        ifnull(m.inQty, 0)        AS inQty,
        ifnull(m.outQty, 0)       AS outQty,
        ifnull(m.transitQty, 0)   AS transitQty,
        ifnull(m.adjustQty, 0)    AS adjustQty,
        ifnull(m.cumulativeSu, 0) AS cumulativeSu,
        ifnull(m.actualQty, 0)    AS actualQty,
        ifnull(pm.cumulativeSu, 0) AS prevSu
    FROM sokos k
    CROSS JOIN periods p
    LEFT JOIN monthly m  ON m.sokoCode = k.sokoCode AND m.ym = p.ym
    LEFT JOIN monthly pm ON pm.sokoCode = k.sokoCode AND pm.ym = p.prevYm
)
SELECT
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    sokoCode, sokoName,
    prevSu,
    inQty, outQty, transitQty, adjustQty,
    cumulativeSu,
    actualQty,
    actualQty - cumulativeSu AS tanaDiff
FROM grid
{activeOnly}
ORDER BY sokoCode, ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
