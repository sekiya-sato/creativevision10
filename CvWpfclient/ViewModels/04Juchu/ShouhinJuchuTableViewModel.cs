using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 商品別受注表。指定期間の受注明細を品番(商品×色×サイズ)別に集計し、
/// 数量・金額・上代金額・伝票数・最終受注日を印字する。商品別発注表の受注側版。
/// </summary>
public partial class ShouhinJuchuTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "商品別受注表";
	protected override string FormFileName => "ShouhinJuchuTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=色サイズ別 / false=商品計。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; } = true;

	/// <summary>true=返品･値引も含める / false=受注(Kubun=10)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

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
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		if (!IncludeHenpin) where += " AND h.Kubun = 10";

		var colCode = IsByColorSize ? TranMeisaiSql.Str("Code_Col") : "''";
		var colName = IsByColorSize ? TranMeisaiSql.Str("Mei_Col") : "''";
		var sizCode = IsByColorSize ? TranMeisaiSql.Str("Code_Siz") : "''";
		var sizName = IsByColorSize ? TranMeisaiSql.Str("Mei_Siz") : "''";

		var sql = $@"
WITH meisai AS (
    SELECT
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {colCode} AS colCode, {colName} AS colName,
        {sizCode} AS sizCode, {sizName} AS sizName,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        h.Id AS denNo, h.DenDay AS denDay
    FROM Tran12Jyuchu h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
)
SELECT
    shohinCode, shohinName,
    colCode, colName, sizCode, sizName,
    SUM(su)                                  AS su,
    SUM(kingaku)                             AS kingaku,
    SUM(su * jodai)                          AS jodaiTotal,
    COUNT(DISTINCT denNo)                    AS denCount,
    {TranMeisaiSql.DateLabel("MAX(denDay)")} AS lastDay
FROM meisai
GROUP BY shohinCode, shohinName, colCode, colName, sizCode, sizName
ORDER BY shohinCode, colCode, sizCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
