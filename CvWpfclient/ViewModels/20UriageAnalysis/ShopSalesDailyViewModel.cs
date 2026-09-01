using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 店別売上日報。店舗×日で売上件数・数量・金額・消費税・総額・値引・客単価を印字する。
/// 予算との対比は日別店別予算表(02Yosan)が担当し、こちらは売上そのものの内訳に寄せている。
/// </summary>
public partial class ShopSalesDailyViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "店別売上日報";
	protected override string FormFileName => "ShopSalesDaily.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=店舗別 / false=全店合計。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	/// <summary>出力対象。true=売上がある日のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShopCodeFrom() => ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;

	[RelayCommand]
	void SelectShopCodeTo() => ShopCodeTo = SelectShopCode() ?? ShopCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTenpo"), ShopCodeFrom, ShopCodeTo);

		var shopCode = IsByShop ? TranMeisaiSql.HeaderCode("VTenpo") : "''";
		var shopName = IsByShop ? TranMeisaiSql.HeaderName("VTenpo") : "'全店'";
		var having = IsActiveOnly ? "HAVING SUM(h.KingakuTotal) != 0 OR SUM(h.SuTotal) != 0" : "";

		var sql = $@"
SELECT
    {PeriodSql.Label("h.DenDay", PeriodUnit.Day)} AS denDayLabel,
    {PeriodSql.Youbi("h.DenDay")}                 AS youbi,
    {shopCode} AS shopCode,
    {shopName} AS shopName,
    COUNT(*)             AS denCount,
    SUM(h.SuTotal)       AS su,
    SUM(h.KingakuTotal)  AS kingaku,
    SUM(h.Tax1+h.Tax2+h.Tax3)           AS tax,
    SUM(CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + (h.Tax1+h.Tax2+h.Tax3) END) AS total,
    SUM(h.Nebiki00Total) AS nebiki,
    CASE WHEN COUNT(*) != 0
         THEN CAST(ROUND(CAST(SUM(h.KingakuTotal) AS REAL) / COUNT(*)) AS INTEGER)
         ELSE 0 END      AS kyakuTanka
FROM Tran01Tenuri h
WHERE {where}
GROUP BY h.DenDay, {shopCode}, {shopName}
{having}
ORDER BY h.DenDay, shopCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
