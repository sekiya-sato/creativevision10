using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 品番別販売動向表。店舗売上の明細を品番×期間で集計し、日別／週別／月別の動向と累計を印字する。
/// 販売動向表が店舗軸なのに対し、こちらは商品軸。売れ筋の立ち上がりと落ち方を追うのに使う。
/// </summary>
public partial class HinbanSalesTrendReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "品番別販売動向表";
	protected override string FormFileName => "HinbanSalesTrendReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsDaily { get; set; }

	[ObservableProperty]
	public partial bool IsWeekly { get; set; } = true;

	[ObservableProperty]
	public partial bool IsMonthly { get; set; }

	/// <summary>集計単位。true=色サイズ別 / false=商品計。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; }

	PeriodUnit Unit => IsMonthly ? PeriodUnit.Month : IsDaily ? PeriodUnit.Day : PeriodUnit.Week;

	[RelayCommand]
	void SelectShopCodeFrom() => ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;

	[RelayCommand]
	void SelectShopCodeTo() => ShopCodeTo = SelectShopCode() ?? ShopCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var unit = Unit;
		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTenpo"), ShopCodeFrom, ShopCodeTo);
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);

		var colName = IsByColorSize ? TranMeisaiSql.Str("Mei_Col") : "''";
		var sizName = IsByColorSize ? TranMeisaiSql.Str("Mei_Siz") : "''";
		var colCode = IsByColorSize ? TranMeisaiSql.Str("Code_Col") : "''";
		var sizCode = IsByColorSize ? TranMeisaiSql.Str("Code_Siz") : "''";

		var sql = $@"
WITH agg AS (
    SELECT
        {PeriodSql.Key("h.DenDay", unit)}   AS periodKey,
        {PeriodSql.Label("h.DenDay", unit)} AS periodLabel,
        {TranMeisaiSql.Str("Code_Shohin")}  AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}   AS shohinName,
        {colCode} AS colCode, {colName} AS colName,
        {sizCode} AS sizCode, {sizName} AS sizName,
        SUM({TranMeisaiSql.Num("Su")})      AS su,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS kingaku,
        SUM({TranMeisaiSql.Num("Su")} * {TranMeisaiSql.Num("Jodai")}) AS jodaiTotal
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
    GROUP BY periodKey, periodLabel, shohinCode, shohinName, colCode, colName, sizCode, sizName
)
SELECT
    periodLabel,
    shohinCode, shohinName,
    colName, sizName,
    su, kingaku, jodaiTotal,
    SUM(su) OVER (PARTITION BY shohinCode, colCode, sizCode ORDER BY periodKey
                  ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumSu,
    SUM(kingaku) OVER (PARTITION BY shohinCode, colCode, sizCode ORDER BY periodKey
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumKingaku
FROM agg
ORDER BY shohinCode, colCode, sizCode, periodKey";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
