using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 支払一覧表。指定支払日の仕入先別 支払額（対象期間・仕入・返品・値引・消費税・支払・残高）を一覧で印字する。
///
/// 集計テーブル SummaryKaiShi を読む。これは支払計算（月次更新処理・31Monthly）が
/// 支払日単位で作る成果物であり、締め処理を回していない支払日は行が無く空になる。
/// 支払残高明細書が仕入先1件ごとの明細を出すのに対し、こちらは支払日単位の一覧。
///
/// SummaryKaiShi は対象期間のみの集計（繰越なし）。当月残(balance)は、対象期間の開始(DayFrom)
/// より前の全行を SUM(TotalShiire - TotalOut) で積んだ PreviousBalance に当期間の Balance を
/// 加えて求める（`Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 2.3）。
/// </summary>
public partial class ShiharaiListReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "支払一覧表";
	protected override string FormFileName => "ShiharaiListReport.qfm";

	[ObservableProperty]
	public partial string PayDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string PayDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=支払額または残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(PayDayFrom, out var from) || !TryParseDate(PayDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("支払日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var shiireWhere = BuildCodeRangeWhere(parameters, "s.Code", ShiireCodeFrom, ShiireCodeTo);

		// PreviousBalance は対象期間の開始(DayFrom)より前の全行を SUM(TotalShiire - TotalOut) で積む
		// （設計書 2.3）。行ごとに DayFrom が異なるため仕入先＋DayFrom の相関スカラサブクエリにする。
		const string PrevBalanceExpr =
			"(SELECT ifnull(SUM(pb.TotalShiire - pb.TotalOut),0) FROM SummaryKaiShi pb WHERE pb.Id_Shiire = k.Id_Shiire AND pb.DayTo < k.DayFrom)";
		var activeOnly = IsActiveOnly ? $"AND (k.TotalOut != 0 OR ({PrevBalanceExpr} + k.Balance) != 0)" : "";

		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("k.DenDay")}  AS payDayLabel,
    s.Code AS shiireCode,
    s.Name AS shiireName,
    {TranMeisaiSql.DateLabel("k.DayFrom")} || '～' || {TranMeisaiSql.DateLabel("k.DayTo")} AS termLabel,
    k.TotalShiire AS totalShiire,
    k.Henpin      AS henpin,
    k.Nebiki      AS nebiki,
    (k.Tax1+k.Tax2+k.Tax3) AS tax,
    k.TotalOut    AS totalOut,
    {PrevBalanceExpr} + k.Balance AS balance
FROM SummaryKaiShi k
JOIN MasterShiire s ON s.Id = k.Id_Shiire
WHERE k.DenDay >= {dayFrom} AND k.DenDay <= {dayTo}
  {activeOnly}{shiireWhere}
ORDER BY k.DenDay, s.Code";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
