using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 支払台帳（発行控え）。支払計算（月次更新処理・31Monthly）が SummaryKaiShi へ保存した
/// 確定済み支払予定日(ShiharaiYoteiDay)を含む確定結果を、仕入先・支払日単位で一覧印字する。
///
/// 「支払一覧表」が金額中心の一覧で支払予定日を持たず、「月別支払予定表」が MasterShiire の
/// 支払条件からライブ再計算するのに対し、こちらは支払計算結果の突合・発行控え用であり、
/// 保存済み ShiharaiYoteiDay を出力する唯一の帳票。締め処理を回していない支払日は行が無く空になる。
///
/// 請求台帳（発行控え）の支払側の対。ただし SummaryKaiShi には請求側の SeikyuNo/Renban に
/// 相当する列が無いため、番号・再発行世代は持たない。
///
/// SummaryKaiShi は対象期間のみの集計（繰越なし）。当月残(balance)は、対象期間の開始(DayFrom)
/// より前の全行を SUM(TotalShiire - TotalOut) で積んだ PreviousBalance に当期間の Balance を
/// 加えて求める（`Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 2.3）。
/// </summary>
public partial class ShiharaiLedgerReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "支払台帳（発行控え）";
	protected override string FormFileName => "ShiharaiLedgerReport.qfm";

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
		// （設計書 2.3）。行ごとに DayFrom が異なりうるため仕入先＋DayFrom で相関させる。
		var activeOnly = IsActiveOnly ? "AND ((pb.PreviousBalance + k.Balance) != 0 OR k.TotalOut != 0)" : "";

		// SELECT の列順は ShiharaiLedgerReport.qfm の item1..item9 と一致させる。
		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("k.DenDay")}  AS payDayLabel,
    s.Code AS shiireCode,
    s.Name AS shiireName,
    {TranMeisaiSql.DateLabel("k.DayFrom")} || '～' || {TranMeisaiSql.DateLabel("k.DayTo")} AS termLabel,
    k.TotalShiire AS totalShiire,
    (k.Tax1+k.Tax2+k.Tax3) AS tax,
    k.TotalOut    AS totalOut,
    ifnull(pb.PreviousBalance,0) + k.Balance AS balance,
    {TranMeisaiSql.DateLabel("k.ShiharaiYoteiDay")} AS shiharaiYoteiLabel
FROM SummaryKaiShi k
JOIN MasterShiire s ON s.Id = k.Id_Shiire
LEFT JOIN (
    SELECT pb.Id_Shiire, pb.DayFrom, SUM(prior.TotalShiire - prior.TotalOut) AS PreviousBalance
    FROM (SELECT DISTINCT Id_Shiire, DayFrom FROM SummaryKaiShi WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}) pb
    JOIN SummaryKaiShi prior ON prior.Id_Shiire = pb.Id_Shiire AND prior.DayTo < pb.DayFrom
    GROUP BY pb.Id_Shiire, pb.DayFrom
) pb ON pb.Id_Shiire = k.Id_Shiire AND pb.DayFrom = k.DayFrom
WHERE k.DenDay >= {dayFrom} AND k.DenDay <= {dayTo}
  {activeOnly}{shiireWhere}
ORDER BY k.DenDay, s.Code";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
