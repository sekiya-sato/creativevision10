using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 分類別店別売上報告。店舗×分類（ブランド／アイテム／シーズン）で売上を集計し、
/// 店舗内の構成比と値入率を印字する。店舗ごとの品揃えバランスを見るのに使う。
///
/// 分類は明細の商品から商品マスタを引いて判定する（伝票側に分類列は無い）。
/// 値入率 = (上代金額 − 原価金額) ÷ 上代金額。原価は商品マスタの現在原価。
/// </summary>
public partial class CategoryShopSalesReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "分類別店別売上報告";
	protected override string FormFileName => "CategoryShopSalesReport.qfm";

	/// <summary>
	/// 原価に関わる列を出すか。店舗向けの「原価無」派生(40Shop)が false で上書きする。
	/// false のときは値入率を SELECT から列ごと外す（上代金額は売価なので残す）。
	/// 列数が変わるため派生側は専用の qfm を持つ。
	/// </summary>
	protected virtual bool ShowCost => true;

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>分類軸: ブランド(BRD)</summary>
	[ObservableProperty]
	public partial bool IsByBrand { get; set; } = true;

	/// <summary>分類軸: アイテム(ITM)</summary>
	[ObservableProperty]
	public partial bool IsByItem { get; set; }

	/// <summary>分類軸: シーズン(SZN)</summary>
	[ObservableProperty]
	public partial bool IsBySeason { get; set; }

	/// <summary>集計単位。true=店舗別 / false=全店合計。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	(string Kubun, string IdColumn) Category =>
		IsByItem ? (MasterMeisho.KubunItem, "sh.Id_Item")
		: IsBySeason ? ("SZN", "sh.Id_Season")
		: (MasterMeisho.KubunBrand, "sh.Id_Brand");

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

		var (kubun, idColumn) = Category;
		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTenpo"), ShopCodeFrom, ShopCodeTo);

		var shopCode = IsByShop ? TranMeisaiSql.HeaderCode("VTenpo") : "''";
		var shopName = IsByShop ? TranMeisaiSql.HeaderName("VTenpo") : "'全店'";
		var neireCol = ShowCost ? @",
    CASE WHEN jodaiTotal != 0
         THEN ROUND(CAST(jodaiTotal - genkaTotal AS REAL) / jodaiTotal * 100, 1)
         ELSE 0 END AS neireRatio" : "";

		var sql = $@"
WITH meisai AS (
    SELECT
        {shopCode} AS shopCode,
        {shopName} AS shopName,
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
agg AS (
    SELECT
        m.shopCode, m.shopName,
        ifnull(cat.Code,'(未設定)') AS catCode,
        ifnull(cat.Name,'(未設定)') AS catName,
        SUM(m.su)               AS su,
        SUM(m.kingaku)          AS kingaku,
        SUM(m.su * m.jodai)     AS jodaiTotal,
        SUM(m.su * ifnull(sh.TankaGenka, 0)) AS genkaTotal
    FROM meisai m
    LEFT JOIN MasterShohin sh  ON sh.Id = m.idShohin
    LEFT JOIN MasterMeisho cat ON cat.Id = {idColumn} AND cat.Kubun = '{kubun}'
    GROUP BY m.shopCode, m.shopName, catCode, catName
)
SELECT
    shopCode, shopName,
    catCode, catName,
    su, kingaku, jodaiTotal,
    SUM(kingaku) OVER (PARTITION BY shopCode) AS shopTotal,
    CASE WHEN SUM(kingaku) OVER (PARTITION BY shopCode) != 0
         THEN ROUND(CAST(kingaku AS REAL) / SUM(kingaku) OVER (PARTITION BY shopCode) * 100, 1)
         ELSE 0 END AS shareRatio{neireCol}
FROM agg
ORDER BY shopCode, catCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
