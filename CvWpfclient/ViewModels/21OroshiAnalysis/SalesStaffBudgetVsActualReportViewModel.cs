using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 販売員別予算実績対比表。販売員予算(MasterYosanHanbai)と店舗売上の販売員実績を年月別に対比する。
///
/// 販売員予算表(02Yosan)が日別の推移を出すのに対し、こちらは年月単位で
/// 予算・実績・差異・達成率・前年同月比を並べて評価に使う。
/// 実績は店舗売上明細の担当社員(Id_Shain)を優先し、未設定(0)の明細は伝票の入力社員へ寄せる。
/// </summary>
public partial class SalesStaffBudgetVsActualReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "販売員別予算実績対比表";
	protected override string FormFileName => "SalesStaffBudgetVsActualReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-5).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜24）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "6";

	[ObservableProperty]
	public partial string ShainCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=販売員×年月 / false=販売員の期間合計のみ。</summary>
	[ObservableProperty]
	public partial bool IsByMonth { get; set; } = true;

	/// <summary>出力対象。true=予算または実績があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShainCodeFrom() => ShainCodeFrom = SelectShainCode() ?? ShainCodeFrom;

	[RelayCommand]
	void SelectShainCodeTo() => ShainCodeTo = SelectShainCode() ?? ShainCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 24) {
			MessageEx.ShowWarningDialog("出力月数は 1〜24 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var end = start.AddMonths(months - 1);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start.AddYears(-1)));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var shainWhere = BuildCodeRangeWhere(parameters, "sn.Code", ShainCodeFrom, ShainCodeTo);

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var termLabel = $"{start:yyyy/MM}～{end:yyyy/MM}";
		// GROUP BY に素の整数リテラルを書くと SQLite は「列の序数」と解釈してエラーになるため、
		// 定数を使う場合は CAST でリテラルでない式にする。
		var ymGroup = IsByMonth ? "p.ym" : "'ALL'";
		var ordGroup = IsByMonth ? "p.ord" : "CAST(0 AS INTEGER)";
		var ymLabel = IsByMonth ? "substr(ym,1,4) || '/' || substr(ym,5,2)" : $"'{termLabel}'";
		var activeOnly = IsActiveOnly ? "WHERE yosan != 0 OR jisseki != 0" : "";

		var sql = $@"
WITH RECURSIVE seq(n) AS (
    SELECT 0 UNION ALL SELECT n+1 FROM seq WHERE n < {months - 1}
),
periods AS (
    SELECT
        strftime('%Y%m', date('{startDate}', '+' || n || ' months')) AS ym,
        strftime('%Y%m', date('{startDate}', '+' || n || ' months', '-1 year')) AS prevYm,
        n AS ord
    FROM seq
),
shains AS (
    SELECT sn.Id, sn.Code, sn.Name FROM MasterShain sn
    WHERE 1=1 {shainWhere}
),
budget AS (
    SELECT Id_Shain AS idShain, substr(DenDay,1,6) AS ym, SUM(UriYosan) AS yosan
    FROM MasterYosanHanbai
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY Id_Shain, substr(DenDay,1,6)
),
actual AS (
    SELECT
        COALESCE(NULLIF({TranMeisaiSql.Num("Id_Shain")}, 0), h.Id_Shain) AS idShain,
        substr(h.DenDay,1,6) AS ym,
        SUM({TranMeisaiSql.Num("Su")})      AS su,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS jisseki
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY idShain, ym
),
grid AS (
    SELECT
        s.Code AS shainCode, s.Name AS shainName,
        {ordGroup} AS ord,
        {ymGroup}  AS ym,
        SUM(ifnull(b.yosan, 0))   AS yosan,
        SUM(ifnull(a.su, 0))      AS su,
        SUM(ifnull(a.jisseki, 0)) AS jisseki,
        SUM(ifnull(pa.jisseki, 0)) AS prevJisseki
    FROM shains s
    CROSS JOIN periods p
    LEFT JOIN budget b  ON b.idShain = s.Id AND b.ym = p.ym
    LEFT JOIN actual a  ON a.idShain = s.Id AND a.ym = p.ym
    LEFT JOIN actual pa ON pa.idShain = s.Id AND pa.ym = p.prevYm
    GROUP BY shainCode, shainName, {ordGroup}, {ymGroup}
)
SELECT
    shainCode, shainName,
    {ymLabel} AS ymLabel,
    su, yosan, jisseki,
    jisseki - yosan AS diff,
    CASE WHEN yosan != 0 THEN ROUND(CAST(jisseki AS REAL) / yosan * 100, 1) ELSE 0 END AS tasseiRatio,
    prevJisseki,
    CASE WHEN prevJisseki != 0 THEN ROUND(CAST(jisseki AS REAL) / prevJisseki * 100, 1) ELSE 0 END AS prevRatio,
    SUM(jisseki) OVER (PARTITION BY shainCode ORDER BY ord
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumJisseki
FROM grid
{activeOnly}
ORDER BY shainCode, ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
