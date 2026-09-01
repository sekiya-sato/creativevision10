using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 請求台帳（発行控え）。請求計算（月次更新処理・31Monthly）が SummaryUriSei へ保存した
/// 請求書番号(SeikyuNo)・再発行世代(Renban)・確定済み入金予定日(NyukinYoteiDay) を含む
/// 確定結果を、得意先・請求日単位で一覧印字する。
///
/// 「請求一覧表」が金額中心の一覧で発行情報(番号・世代・予定日)を持たないのに対し、こちらは
/// 請求計算結果の突合・発行控え用。得意先1件ごとの請求書は「請求書印刷」を使う。
/// 締め処理を回していない請求日は行が無く空になる。
///
/// SummaryUriSei は対象期間のみの集計（繰越なし）。当月残(balance)は、対象期間の開始(DayFrom)
/// より前の全行を SUM(TotalSales - TotalIn) で積んだ PreviousBalance に当期間の Balance を
/// 加えて求める（`Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 2.3）。
/// </summary>
public partial class SeikyuLedgerReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "請求台帳（発行控え）";
	protected override string FormFileName => "SeikyuLedgerReport.qfm";

	[ObservableProperty]
	public partial string SeikyuDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string SeikyuDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=請求額または残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(SeikyuDayFrom, out var from) || !TryParseDate(SeikyuDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("請求日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "t.Code", TokuiCodeFrom, TokuiCodeTo);

		// PreviousBalance は対象期間の開始(DayFrom)より前の全行を SUM(TotalSales - TotalIn) で積む
		// （設計書 2.3）。行ごとに DayFrom が異なりうるため得意先＋DayFrom で相関させる。
		var activeOnly = IsActiveOnly ? "AND ((pb.PreviousBalance + u.Balance) != 0 OR u.TotalSales != 0)" : "";

		// SELECT の列順は SeikyuLedgerReport.qfm の item1..item10 と一致させる。
		var sql = $@"
SELECT
    u.SeikyuNo AS seikyuNo,
    {TranMeisaiSql.DateLabel("u.DenDay")}  AS seikyuDayLabel,
    t.Code AS tokuiCode,
    t.Name AS tokuiName,
    {TranMeisaiSql.DateLabel("u.DayFrom")} || '～' || {TranMeisaiSql.DateLabel("u.DayTo")} AS termLabel,
    u.TotalSales AS totalSales,
    (u.Tax1+u.Tax2+u.Tax3) AS tax,
    ifnull(pb.PreviousBalance,0) + u.Balance AS balance,
    {TranMeisaiSql.DateLabel("u.NyukinYoteiDay")} AS nyukinYoteiLabel,
    u.Renban     AS renban
FROM SummaryUriSei u
JOIN MasterTokui t ON t.Id = u.Id_Tokui
LEFT JOIN (
    SELECT pb.Id_Tokui, pb.DayFrom, SUM(prior.TotalSales - prior.TotalIn) AS PreviousBalance
    FROM (SELECT DISTINCT Id_Tokui, DayFrom FROM SummaryUriSei WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}) pb
    JOIN SummaryUriSei prior ON prior.Id_Tokui = pb.Id_Tokui AND prior.DayTo < pb.DayFrom
    GROUP BY pb.Id_Tokui, pb.DayFrom
) pb ON pb.Id_Tokui = u.Id_Tokui AND pb.DayFrom = u.DayFrom
WHERE u.DenDay >= {dayFrom} AND u.DenDay <= {dayTo}
  {activeOnly}{tokuiWhere}
ORDER BY u.DenDay, t.Code, u.SeikyuNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
