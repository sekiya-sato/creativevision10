using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 店舗別売上日計表。店別売上日報が伝票の合計値を並べるのに対し、こちらは
/// 取引区分ごとに「純売上・返品・値引」へ分解して日計を出す。締めの突合に使う。
///
/// 純売上 = 売上(Kubun 10,11) − 返品(Kubun 20,21)。値引は Nebiki00Total の合計。
/// 取引区分は EnumUri01（10=売上 11=売上SALE 20=返品 21=返品SALE 99=その他）。
/// </summary>
public partial class ShopSalesDailySummaryViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "店舗別売上日計表";
	protected override string FormFileName => "ShopSalesDailySummary.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=店舗別 / false=全店合計。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

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
		var uriKubun = $"{(int)EnumUri01.Uriage},{(int)EnumUri01.UriSale}";
		var henKubun = $"{(int)EnumUri01.Henpin},{(int)EnumUri01.HenSale}";

		var sql = $@"
WITH agg AS (
    SELECT
        h.DenDay AS denDaySort,
        {PeriodSql.Label("h.DenDay", PeriodUnit.Day)} AS denDayLabel,
        {PeriodSql.Youbi("h.DenDay")}                 AS youbi,
        {shopCode} AS shopCode,
        {shopName} AS shopName,
        COUNT(*) AS denCount,
        SUM(CASE WHEN h.Kubun IN ({uriKubun}) THEN h.KingakuTotal ELSE 0 END) AS uriKingaku,
        SUM(CASE WHEN h.Kubun IN ({henKubun}) THEN h.KingakuTotal ELSE 0 END) AS henKingaku,
        SUM(h.Nebiki00Total) AS nebiki,
        SUM(h.Tax)           AS tax,
        SUM(h.KingakuTotal)  AS netKingaku
    FROM Tran01Tenuri h
    WHERE {where}
    GROUP BY h.DenDay, {shopCode}, {shopName}
)
SELECT
    denDayLabel, youbi,
    shopCode, shopName,
    denCount,
    uriKingaku,
    henKingaku,
    nebiki,
    tax,
    netKingaku,
    SUM(netKingaku) OVER (PARTITION BY shopCode ORDER BY denDaySort
                          ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumNetKingaku
FROM agg
ORDER BY shopCode, denDaySort";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
