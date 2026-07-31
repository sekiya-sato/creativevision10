using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 買掛金管理表。指定年月の仕入先別 買掛残高（前月残・当月仕入・当月支払・当月残）を印字する。
///
/// 集計テーブル SummaryKaiKake を読む。これは支払計算（月次更新処理・31Monthly）の成果物であり、
/// 締め処理を回していない年月は行が無く空になる。元帳が伝票から積み上げるのとは方針が異なるが、
/// 「管理表」は締めた結果を確認する帳票なので集計テーブルが正となる。
/// 前月残は前月行の Balance を引く。
/// </summary>
public partial class KaikakeBalanceReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "買掛金管理表";
	protected override string FormFileName => "KaikakeBalanceReport.qfm";

	[ObservableProperty]
	public partial string TargetYearMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=残高または当月の動きがある仕入先のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(TargetYearMonth, out var target)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var ym = AddSqlParameter(parameters, target.ToString("yyyyMM", CultureInfo.InvariantCulture));
		var prevYm = AddSqlParameter(parameters, target.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture));
		var shiireWhere = BuildCodeRangeWhere(parameters, "Code", ShiireCodeFrom, ShiireCodeTo);

		var activeOnly = IsActiveOnly
			? "WHERE prevBalance != 0 OR totalShiire != 0 OR totalOut != 0 OR balance != 0"
			: "";

		var sql = $@"
WITH shiire AS (
    SELECT Id, Code, Name FROM MasterShiire
    WHERE 1=1 {shiireWhere}
),
cur AS (
    SELECT Id_Shiire, Balance, TotalOut, TotalShiire, Shiire, Henpin, Nebiki, Tax,
           Cash, Fee, Densai, Offset, Other
    FROM SummaryKaiKake WHERE DenMonth = {ym}
),
prev AS (
    SELECT Id_Shiire, Balance FROM SummaryKaiKake WHERE DenMonth = {prevYm}
),
joined AS (
    SELECT
        s.Code AS shiireCode, s.Name AS shiireName,
        ifnull(p.Balance, 0)     AS prevBalance,
        ifnull(c.TotalShiire, 0) AS totalShiire,
        ifnull(c.Tax, 0)         AS tax,
        ifnull(c.Henpin, 0)      AS henpin,
        ifnull(c.Nebiki, 0)      AS nebiki,
        ifnull(c.TotalOut, 0)    AS totalOut,
        ifnull(c.Cash, 0)        AS cash,
        ifnull(c.Fee, 0)         AS fee,
        ifnull(c.Balance, 0)     AS balance
    FROM shiire s
    LEFT JOIN cur c  ON c.Id_Shiire = s.Id
    LEFT JOIN prev p ON p.Id_Shiire = s.Id
)
SELECT
    shiireCode, shiireName,
    prevBalance, totalShiire, tax, henpin, nebiki,
    totalOut, cash, fee, balance
FROM joined
{activeOnly}
ORDER BY shiireCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
