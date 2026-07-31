using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 商品別受払表。年月別在庫集計(SummaryStock)を商品（または SKU）単位に合計し、
/// 前月残・入庫・出庫・調整・当月残を年月順に印字する。倉庫別受払表の商品軸版。
///
/// 前月残は前月行の CumulativeSu（累計在庫）を引く。
/// SummaryStock は在庫累計更新（月次更新処理・Phase 15）が作るので、
/// 更新していない年月は行が無く空になる。
/// </summary>
public partial class ShohinInOutReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "商品別受払表";
	protected override string FormFileName => "ShohinInOutReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-5).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜24）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "6";

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=SKU別（色サイズ別） / false=商品計。</summary>
	[ObservableProperty]
	public partial bool IsBySku { get; set; }

	/// <summary>出力対象。true=動きまたは残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

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
		var where = "1=1";
		where += BuildCodeRangeWhere(parameters, StockSql.ShohinCode(), ShohinCodeFrom, ShohinCodeTo);
		where += BuildCodeRangeWhere(parameters, StockSql.SokoCode(), SokoCodeFrom, SokoCodeTo);

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var lastOffset = months - 1;
		var colCode = IsBySku ? StockSql.ColCode() : "''";
		var colName = IsBySku ? StockSql.ColName() : "''";
		var sizName = IsBySku ? StockSql.SizName() : "''";
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
        {StockSql.ShohinCode()} AS shohinCode,
        {StockSql.ShohinName()} AS shohinName,
        {colCode}               AS colCode,
        {colName}               AS colName,
        {sizName}               AS sizName,
        s.SumMonth              AS ym,
        SUM(s.InQty)            AS inQty,
        SUM(s.OutQty)           AS outQty,
        SUM(s.AdjustQty)        AS adjustQty,
        SUM(s.CumulativeSu)     AS cumulativeSu
    FROM SummaryStock s
{StockSql.JoinSku()}
{StockSql.JoinSoko()}
    WHERE {where}
    GROUP BY shohinCode, shohinName, colCode, colName, sizName, s.SumMonth
),
keys AS (
    SELECT DISTINCT shohinCode, shohinName, colCode, colName, sizName FROM monthly
),
grid AS (
    SELECT
        k.shohinCode, k.shohinName, k.colCode, k.colName, k.sizName,
        p.ord, p.ym,
        ifnull(m.inQty, 0)        AS inQty,
        ifnull(m.outQty, 0)       AS outQty,
        ifnull(m.adjustQty, 0)    AS adjustQty,
        ifnull(m.cumulativeSu, 0) AS cumulativeSu,
        ifnull(pm.cumulativeSu, 0) AS prevSu
    FROM keys k
    CROSS JOIN periods p
    LEFT JOIN monthly m
           ON m.shohinCode = k.shohinCode AND m.colCode = k.colCode AND m.sizName = k.sizName
          AND m.ym = p.ym
    LEFT JOIN monthly pm
           ON pm.shohinCode = k.shohinCode AND pm.colCode = k.colCode AND pm.sizName = k.sizName
          AND pm.ym = p.prevYm
)
SELECT
    substr(ym,1,4) || '/' || substr(ym,5,2) AS ymLabel,
    shohinCode, shohinName,
    colName, sizName,
    prevSu,
    inQty, outQty, adjustQty,
    cumulativeSu
FROM grid
{activeOnly}
ORDER BY shohinCode, colCode, sizName, ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
