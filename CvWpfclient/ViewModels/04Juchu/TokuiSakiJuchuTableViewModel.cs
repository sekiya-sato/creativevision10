using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 得意先別受注表。指定期間の受注(Tran12Jyuchu)を得意先別に集計し、
/// 件数・数量・金額・消費税・総額・上代金額・掛率・最終受注日を印字する。仕入先別発注表の受注側版。
/// </summary>
public partial class TokuiSakiJuchuTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "得意先別受注表";
	protected override string FormFileName => "TokuiSakiJuchuTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>true=返品･値引も含める / false=受注(Kubun=10)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("受注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo);
		if (!IncludeHenpin) where += " AND h.Kubun = 10";

		var sql = $@"
SELECT
    {TranMeisaiSql.HeaderCode("VTokui")} AS tokuiCode,
    {TranMeisaiSql.HeaderName("VTokui")} AS tokuiName,
    COUNT(*)                             AS denCount,
    SUM(h.SuTotal)                       AS su,
    SUM(h.KingakuTotal)                  AS kingaku,
    SUM(h.Tax)                           AS tax,
    SUM(CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END) AS total,
    SUM(h.JodaiTotal)                    AS jodaiTotal,
    CASE WHEN SUM(h.JodaiTotal) != 0
         THEN ROUND(CAST(SUM(h.KingakuTotal) AS REAL) / SUM(h.JodaiTotal) * 100, 1)
         ELSE 0 END                      AS kakeRatio,
    {TranMeisaiSql.DateLabel("MAX(h.DenDay)")} AS lastDay
FROM Tran12Jyuchu h
WHERE {where}
GROUP BY tokuiCode, tokuiName
ORDER BY tokuiCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
