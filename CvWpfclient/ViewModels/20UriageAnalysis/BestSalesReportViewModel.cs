using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// ベスト表。店舗売上を品番別に集計して金額または数量の降順に順位付けし、構成比と累計構成比を印字する。
/// 受注ベスト表(04Juchu)の売上版。累計構成比はABC分析の目安に使える。
/// 順位は同値でも連番になる（ROW_NUMBER）。
/// </summary>
public partial class BestSalesReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "ベスト表";
	protected override string FormFileName => "BestSalesReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	/// <summary>出力件数（1〜999）</summary>
	[ObservableProperty]
	public partial string TopCountText { get; set; } = "50";

	/// <summary>順位付けの基準。true=金額順 / false=数量順。</summary>
	[ObservableProperty]
	public partial bool IsByKingaku { get; set; } = true;

	/// <summary>集計単位。true=色サイズ別 / false=商品計。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; }

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
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(TopCountText.Trim(), out var topCount) || topCount < 1 || topCount > 999) {
			MessageEx.ShowWarningDialog("出力件数は 1〜999 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTenpo"), ShopCodeFrom, ShopCodeTo);
		var brandWhere = BuildCodeRangeWhere(parameters, "ifnull(br.Code,'')", BrandCodeFrom, BrandCodeTo);

		var colName = IsByColorSize ? TranMeisaiSql.Str("Mei_Col") : "''";
		var sizName = IsByColorSize ? TranMeisaiSql.Str("Mei_Siz") : "''";
		var colCode = IsByColorSize ? TranMeisaiSql.Str("Code_Col") : "''";
		var sizCode = IsByColorSize ? TranMeisaiSql.Str("Code_Siz") : "''";
		var orderKey = IsByKingaku ? "kingaku" : "su";

		var sql = $@"
WITH meisai AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")}   AS idShohin,
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {colCode} AS colCode, {colName} AS colName,
        {sizCode} AS sizCode, {sizName} AS sizName,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        h.Id_Tenpo AS idTenpo
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
filtered AS (
    SELECT m.*
    FROM meisai m
    LEFT JOIN MasterShohin sh ON sh.Id = m.idShohin
    LEFT JOIN MasterMeisho br ON br.Id = sh.Id_Brand AND br.Kubun = '{MasterMeisho.KubunBrand}'
    WHERE 1=1 {brandWhere}
),
agg AS (
    SELECT
        shohinCode, shohinName, colCode, colName, sizCode, sizName,
        SUM(su)                 AS su,
        SUM(kingaku)            AS kingaku,
        SUM(su * jodai)         AS jodaiTotal,
        COUNT(DISTINCT idTenpo) AS shopCount
    FROM filtered
    GROUP BY shohinCode, shohinName, colCode, colName, sizCode, sizName
),
ranked AS (
    SELECT
        a.*,
        ROW_NUMBER() OVER (ORDER BY {orderKey} DESC) AS rank,
        SUM({orderKey}) OVER ()                      AS grandTotal,
        SUM({orderKey}) OVER (ORDER BY {orderKey} DESC
                              ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumValue
    FROM agg a
)
SELECT
    rank,
    shohinCode, shohinName,
    colName, sizName,
    su, kingaku, jodaiTotal, shopCount,
    CASE WHEN grandTotal != 0 THEN ROUND(CAST({orderKey} AS REAL) / grandTotal * 100, 1) ELSE 0 END AS shareRatio,
    CASE WHEN grandTotal != 0 THEN ROUND(CAST(cumValue AS REAL) / grandTotal * 100, 1) ELSE 0 END AS cumShareRatio
FROM ranked
WHERE rank <= {topCount}
ORDER BY rank";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
