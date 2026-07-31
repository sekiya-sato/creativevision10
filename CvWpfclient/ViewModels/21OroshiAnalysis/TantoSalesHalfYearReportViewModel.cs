using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 担当別売上実績半期報。営業担当ごとに半期（6ヶ月）の月別売上実績を、前年同月比と累計付きで印字する。
///
/// 担当は得意先マスタの営業担当(MasterTokui.Id_Shain)。卸売上は「どの得意先を担当しているか」で
/// 担当実績が決まるため、伝票の入力社員ではなく得意先の営業担当を使う。
/// （店舗売上の販売員実績は明細の担当社員を使う。軸が違うので混同しないこと。）
///
/// 半期の起点月を指定し、そこから6ヶ月を出す。上期/下期の区切りは会社ごとに違うので固定していない。
/// </summary>
public partial class TantoSalesHalfYearReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "担当別売上実績半期報";
	protected override string FormFileName => "TantoSalesHalfYearReport.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-5).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShainCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=担当×年月 / false=担当の半期合計のみ。</summary>
	[ObservableProperty]
	public partial bool IsByMonth { get; set; } = true;

	/// <summary>出力対象。true=実績がある担当のみ / false=全担当。</summary>
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
		ct.ThrowIfCancellationRequested();

		const int Months = 6;
		var end = start.AddMonths(Months - 1);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start.AddYears(-1)));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var shainWhere = BuildCodeRangeWhere(parameters, "sn.Code", ShainCodeFrom, ShainCodeTo);

		var startDate = start.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
		var halfLabel = $"{start:yyyy/MM}～{end:yyyy/MM}";
		// 半期合計のときは年月キーを潰す。
		// GROUP BY に素の整数リテラルを書くと SQLite は「列の序数」と解釈してエラーになるため、
		// 定数を使う場合は CAST でリテラルでない式にする。
		var ymGroup = IsByMonth ? "p.ym" : "'ALL'";
		var ymLabel = IsByMonth ? "substr(ym,1,4) || '/' || substr(ym,5,2)" : $"'{halfLabel}'";
		var ordGroup = IsByMonth ? "p.ord" : "CAST(0 AS INTEGER)";
		var activeOnly = IsActiveOnly ? "WHERE kingaku != 0 OR su != 0" : "";

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
-- 担当実績は「得意先の営業担当」で決まる
tokui_shain AS (
    SELECT t.Id AS idTokui, t.Id_Shain AS idShain
    FROM MasterTokui t
    WHERE t.TenType = 1
),
monthly AS (
    SELECT
        ts.idShain           AS idShain,
        substr(h.DenDay,1,6) AS ym,
        COUNT(*)             AS denCount,
        SUM(h.SuTotal)       AS su,
        SUM(h.KingakuTotal)  AS kingaku,
        SUM(h.JodaiTotal)    AS jodaiTotal,
        SUM(h.GedaiTotal)    AS gedaiTotal,
        COUNT(DISTINCT h.Id_Tokui) AS tokuiCount
    FROM Tran00Uriage h
    JOIN tokui_shain ts ON ts.idTokui = h.Id_Tokui
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY ts.idShain, substr(h.DenDay,1,6)
),
shains AS (
    SELECT sn.Id, sn.Code, sn.Name FROM MasterShain sn
    WHERE 1=1 {shainWhere}
),
grid AS (
    SELECT
        s.Code AS shainCode, s.Name AS shainName,
        {ordGroup} AS ord,
        {ymGroup}  AS ym,
        SUM(ifnull(m.denCount, 0))   AS denCount,
        SUM(ifnull(m.su, 0))         AS su,
        SUM(ifnull(m.kingaku, 0))    AS kingaku,
        SUM(ifnull(m.jodaiTotal, 0)) AS jodaiTotal,
        SUM(ifnull(m.gedaiTotal, 0)) AS gedaiTotal,
        MAX(ifnull(m.tokuiCount, 0)) AS tokuiCount,
        SUM(ifnull(pm.kingaku, 0))   AS prevKingaku
    FROM shains s
    CROSS JOIN periods p
    LEFT JOIN monthly m  ON m.idShain = s.Id AND m.ym = p.ym
    LEFT JOIN monthly pm ON pm.idShain = s.Id AND pm.ym = p.prevYm
    GROUP BY shainCode, shainName, {ordGroup}, {ymGroup}
)
SELECT
    shainCode, shainName,
    {ymLabel} AS ymLabel,
    tokuiCount, denCount, su, kingaku,
    CASE WHEN jodaiTotal != 0
         THEN ROUND(CAST(jodaiTotal - gedaiTotal AS REAL) / jodaiTotal * 100, 1)
         ELSE 0 END AS neireRatio,
    prevKingaku,
    CASE WHEN prevKingaku != 0 THEN ROUND(CAST(kingaku AS REAL) / prevKingaku * 100, 1) ELSE 0 END AS prevRatio,
    SUM(kingaku) OVER (PARTITION BY shainCode ORDER BY ord
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumKingaku
FROM grid
{activeOnly}
ORDER BY shainCode, ord";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
