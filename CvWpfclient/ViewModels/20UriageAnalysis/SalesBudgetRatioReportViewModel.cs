using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 売上予算構成比。店舗×ブランドで「予算の構成比」と「実績の構成比」を並べ、
/// 予算配分と実売の食い違いを見る。差異と達成率も併記する。
///
/// 予算は MasterYosanBrand（店舗×ブランド×日）を指定年月で合計する。
/// 実績のブランドは売上明細の商品Idから MasterShohin.Id_Brand を引いて判定する。
/// 構成比は「店舗内での比率」。店舗計行は出さず、店舗ごとの合計列を各行に持たせている。
/// </summary>
public partial class SalesBudgetRatioReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "売上予算構成比";
	protected override string FormFileName => "SalesBudgetRatioReport.qfm";

	[ObservableProperty]
	public partial string TargetYearMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=予算または実績があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShopCodeFrom() => ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;

	[RelayCommand]
	void SelectShopCodeTo() => ShopCodeTo = SelectShopCode() ?? ShopCodeTo;

	[RelayCommand]
	void SelectBrandCodeFrom() => BrandCodeFrom = SelectBrandCode() ?? BrandCodeFrom;

	[RelayCommand]
	void SelectBrandCodeTo() => BrandCodeTo = SelectBrandCode() ?? BrandCodeTo;

	string? SelectBrandCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunBrand}'", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(TargetYearMonth, out var target)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var (dateFrom, dateTo) = GetMonthRange(target);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, dateFrom);
		var dayTo = AddSqlParameter(parameters, dateTo);
		var shopWhere = BuildCodeRangeWhere(parameters, "sh.Code", ShopCodeFrom, ShopCodeTo);
		var brandWhere = BuildCodeRangeWhere(parameters, "br.Code", BrandCodeFrom, BrandCodeTo);

		var having = IsActiveOnly ? "WHERE yosan != 0 OR jisseki != 0" : "";

		var sql = $@"
WITH shops AS (
    SELECT sh.Id, sh.Code, sh.Name FROM MasterTokui sh
    WHERE sh.TenType = 6 {shopWhere}
),
brands AS (
    SELECT br.Id, br.Code, br.Name FROM MasterMeisho br
    WHERE br.Kubun = '{MasterMeisho.KubunBrand}' {brandWhere}
),
budget AS (
    SELECT Id_Tenpo AS idTenpo, Id_Brand AS idBrand, SUM(UriYosan) AS yosan
    FROM MasterYosanBrand
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY Id_Tenpo, Id_Brand
),
actual AS (
    SELECT
        h.Id_Tenpo AS idTenpo,
        (SELECT s.Id_Brand FROM MasterShohin s
         WHERE s.Id = {TranMeisaiSql.Num("Id_Shohin")}) AS idBrand,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
),
actual_agg AS (
    SELECT idTenpo, idBrand, SUM(kingaku) AS jisseki
    FROM actual
    GROUP BY idTenpo, idBrand
),
joined AS (
    SELECT
        s.Code AS shopCode, s.Name AS shopName,
        b.Code AS brandCode, b.Name AS brandName,
        ifnull(bg.yosan, 0)  AS yosan,
        ifnull(ac.jisseki, 0) AS jisseki
    FROM shops s
    CROSS JOIN brands b
    LEFT JOIN budget bg     ON bg.idTenpo = s.Id AND bg.idBrand = b.Id
    LEFT JOIN actual_agg ac ON ac.idTenpo = s.Id AND ac.idBrand = b.Id
),
filtered AS (
    SELECT * FROM joined
    {having}
)
SELECT
    shopCode, shopName,
    brandCode, brandName,
    yosan,
    CASE WHEN SUM(yosan) OVER (PARTITION BY shopCode) != 0
         THEN ROUND(CAST(yosan AS REAL) / SUM(yosan) OVER (PARTITION BY shopCode) * 100, 1)
         ELSE 0 END AS yosanShare,
    jisseki,
    CASE WHEN SUM(jisseki) OVER (PARTITION BY shopCode) != 0
         THEN ROUND(CAST(jisseki AS REAL) / SUM(jisseki) OVER (PARTITION BY shopCode) * 100, 1)
         ELSE 0 END AS jissekiShare,
    jisseki - yosan AS diff,
    CASE WHEN yosan != 0 THEN ROUND(CAST(jisseki AS REAL) / yosan * 100, 1) ELSE 0 END AS tasseiRatio
FROM filtered
ORDER BY shopCode, brandCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
