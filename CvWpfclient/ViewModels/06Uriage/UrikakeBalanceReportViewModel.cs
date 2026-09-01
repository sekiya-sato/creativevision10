using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 売掛金管理表。指定年月の得意先別 売掛残高（前月残・当月売上・当月入金・当月残）を印字する。
///
/// 集計テーブル SummaryUriKake を読む。これは請求計算（月次更新処理・31Monthly）の成果物であり、
/// 締め処理を回していない年月は行が無く空になる。元帳が伝票から積み上げるのとは方針が異なるが、
/// 「管理表」は締めた結果を確認する帳票なので集計テーブルが正となる。
///
/// SummaryUriKake は対象年月のみの集計（繰越なし）。前月残(prevBalance)は、対象年月より前の
/// 全行を SUM(TotalSales - TotalIn) で積んで都度算出する（PreviousBalance、
/// `Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 2.3）。
/// 前月に行が無い得意先でも、前々月以前の残があれば前月残に反映される（前月行の直読みだった旧仕様は 0 になっていた）。
/// 当月残(balance)は PreviousBalance + Balance。
/// </summary>
public partial class UrikakeBalanceReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "売掛金管理表";
	protected override string FormFileName => "UrikakeBalanceReport.qfm";

	[ObservableProperty]
	public partial string TargetYearMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=残高または当月の動きがある得意先のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(TargetYearMonth, out var target)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var ym = AddSqlParameter(parameters, target.ToString("yyyyMM", CultureInfo.InvariantCulture));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "Code", TokuiCodeFrom, TokuiCodeTo);

		var activeOnly = IsActiveOnly
			? "WHERE prevBalance != 0 OR totalSales != 0 OR totalIn != 0 OR balance != 0"
			: "";

		var sql = $@"
WITH tokui AS (
    SELECT Id, Code, Name FROM MasterTokui
    WHERE TenType = 1 {tokuiWhere}
),
cur AS (
    SELECT Id_Tokui, Balance, TotalIn, TotalSales, Uriage, Henpin, Nebiki, Tax1, Tax2, Tax3,
           Cash, Fee, Densai, Offset, Other
    FROM SummaryUriKake WHERE DenMonth = {ym}
),
previousBalance AS (
    SELECT Id_Tokui, SUM(TotalSales - TotalIn) AS PreviousBalance
    FROM SummaryUriKake WHERE DenMonth < {ym} GROUP BY Id_Tokui
),
joined AS (
    SELECT
        t.Code AS tokuiCode, t.Name AS tokuiName,
        ifnull(pb.PreviousBalance, 0) AS prevBalance,
        ifnull(c.TotalSales, 0) AS totalSales,
        ifnull(c.Tax1, 0) + ifnull(c.Tax2, 0) + ifnull(c.Tax3, 0) AS tax,
        ifnull(c.Henpin, 0)     AS henpin,
        ifnull(c.Nebiki, 0)     AS nebiki,
        ifnull(c.TotalIn, 0)    AS totalIn,
        ifnull(c.Cash, 0)       AS cash,
        ifnull(c.Fee, 0)        AS fee,
        ifnull(pb.PreviousBalance, 0) + ifnull(c.Balance, 0) AS balance
    FROM tokui t
    LEFT JOIN cur c  ON c.Id_Tokui = t.Id
    LEFT JOIN previousBalance pb ON pb.Id_Tokui = t.Id
)
SELECT
    tokuiCode, tokuiName,
    prevBalance, totalSales, tax, henpin, nebiki,
    totalIn, cash, fee, balance
FROM joined
{activeOnly}
ORDER BY tokuiCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
