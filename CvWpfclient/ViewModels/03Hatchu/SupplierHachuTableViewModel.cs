using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 仕入先別発注表。指定期間の発注(Tran13Hachu)を仕入先別に集計し、
/// 件数・数量・金額・消費税・総額・上代金額・原価率・最終発注日を印字する。
/// 発注の取引区分は仕入と同じ体系（10=発注 / 20=返品 / 30=値引 / 99=その他）。
/// </summary>
public partial class SupplierHachuTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "仕入先別発注表";
	protected override string FormFileName => "SupplierHachuTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>true=返品･値引も含める / false=発注(Kubun=10)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("発注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);
		if (!IncludeHenpin) where += " AND h.Kubun = 10";

		var sql = $@"
SELECT
    {TranMeisaiSql.HeaderCode("VShiire")} AS shiireCode,
    {TranMeisaiSql.HeaderName("VShiire")} AS shiireName,
    COUNT(*)                              AS denCount,
    SUM(h.SuTotal)                        AS su,
    SUM(h.KingakuTotal)                   AS kingaku,
    SUM(h.Tax)                            AS tax,
    SUM(CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END) AS total,
    SUM(h.JodaiTotal)                     AS jodaiTotal,
    CASE WHEN SUM(h.JodaiTotal) != 0
         THEN ROUND(CAST(SUM(h.KingakuTotal) AS REAL) / SUM(h.JodaiTotal) * 100, 1)
         ELSE 0 END                       AS genkaRatio,
    {TranMeisaiSql.DateLabel("MAX(h.DenDay)")} AS lastDay
FROM Tran13Hachu h
WHERE {where}
GROUP BY shiireCode, shiireName
ORDER BY shiireCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
