using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 店舗売上ランキング表。指定期間の店舗売上を金額順に順位付けし、客単価・予算・予算比・構成比を印字する。
/// 予算は MasterYosanBrand（店舗×ブランド×日）を期間内でブランド横断合計して求める。
/// </summary>
public partial class ShopSalesRankingReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "店舗売上ランキング表";
	protected override string FormFileName => "ShopSalesRankingReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>順位付けの基準。true=金額順 / false=数量順。</summary>
	[ObservableProperty]
	public partial bool IsByKingaku { get; set; } = true;

	/// <summary>出力対象。true=売上がある店舗のみ / false=直営店全て。</summary>
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
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var shopWhere = BuildCodeRangeWhere(parameters, "t.Code", ShopCodeFrom, ShopCodeTo);

		var orderKey = IsByKingaku ? "kingaku" : "su";
		var join = IsActiveOnly ? "JOIN" : "LEFT JOIN";

		var sql = $@"
WITH shops AS (
    SELECT t.Id, t.Code, t.Name FROM MasterTokui t
    WHERE t.TenType = 6 {shopWhere}
),
sales AS (
    SELECT
        h.Id_Tenpo AS idTenpo,
        COUNT(*)            AS denCount,
        SUM(h.SuTotal)      AS su,
        SUM(h.KingakuTotal) AS kingaku
    FROM Tran01Tenuri h
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY h.Id_Tenpo
),
budget AS (
    SELECT Id_Tenpo AS idTenpo, SUM(UriYosan) AS yosan
    FROM MasterYosanBrand
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY Id_Tenpo
),
agg AS (
    SELECT
        s.Code AS shopCode, s.Name AS shopName,
        ifnull(sa.denCount, 0) AS denCount,
        ifnull(sa.su, 0)       AS su,
        ifnull(sa.kingaku, 0)  AS kingaku,
        ifnull(b.yosan, 0)     AS yosan
    FROM shops s
    {join} sales sa ON sa.idTenpo = s.Id
    LEFT JOIN budget b ON b.idTenpo = s.Id
),
ranked AS (
    SELECT
        a.*,
        ROW_NUMBER() OVER (ORDER BY {orderKey} DESC) AS rank,
        SUM(kingaku) OVER ()                         AS grandTotal
    FROM agg a
)
SELECT
    rank,
    shopCode, shopName,
    denCount, su, kingaku,
    CASE WHEN denCount != 0
         THEN CAST(ROUND(CAST(kingaku AS REAL) / denCount) AS INTEGER)
         ELSE 0 END AS kyakuTanka,
    yosan,
    CASE WHEN yosan != 0 THEN ROUND(CAST(kingaku AS REAL) / yosan * 100, 1) ELSE 0 END AS yosanRatio,
    CASE WHEN grandTotal != 0 THEN ROUND(CAST(kingaku AS REAL) / grandTotal * 100, 1) ELSE 0 END AS shareRatio
FROM ranked
ORDER BY rank";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
