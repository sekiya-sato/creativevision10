using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 請求一覧表。指定請求日の得意先別 請求額（対象期間・売上・返品・値引・消費税・入金・残高）を一覧で印字する。
///
/// 集計テーブル SummaryUriSei を読む。これは請求計算（月次更新処理・31Monthly）が
/// 請求日単位で作る成果物であり、締め処理を回していない請求日は行が無く空になる。
/// 請求書印刷が得意先1件ごとの請求書を出すのに対し、こちらは請求日単位の一覧（発行控え）。
/// </summary>
public partial class SeikyuListReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "請求一覧表";
	protected override string FormFileName => "SeikyuListReport.qfm";

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

		var activeOnly = IsActiveOnly ? "AND (u.TotalSales != 0 OR u.Balance != 0)" : "";

		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("u.DenDay")}  AS seikyuDayLabel,
    t.Code AS tokuiCode,
    t.Name AS tokuiName,
    {TranMeisaiSql.DateLabel("u.DayFrom")} || '～' || {TranMeisaiSql.DateLabel("u.DayTo")} AS termLabel,
    u.TotalSales AS totalSales,
    u.Henpin     AS henpin,
    u.Nebiki     AS nebiki,
    u.Tax        AS tax,
    u.TotalIn    AS totalIn,
    u.Balance    AS balance
FROM SummaryUriSei u
JOIN MasterTokui t ON t.Id = u.Id_Tokui
WHERE u.DenDay >= {dayFrom} AND u.DenDay <= {dayTo}
  {activeOnly}{tokuiWhere}
ORDER BY u.DenDay, t.Code";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
