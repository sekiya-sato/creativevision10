using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 分類別売上消化率表。商品消化率表を分類（ブランド／アイテム／シーズン）で括った版。
/// 分類ごとの投入・売上・在庫・消化率・値入率・売上構成比を印字する。
///
/// 消化率の分母は商品消化率表と同じ考え方で「売上+在庫」を既定とし、投入基準も選べる。
/// 分類は商品マスタから引く（伝票側に分類列は無い）。
/// </summary>
public partial class CategorySalesConsumptionRateViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "分類別売上消化率表";
	protected override string FormFileName => "CategorySalesConsumptionRate.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddMonths(-3).ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	/// <summary>分類軸: ブランド(BRD)</summary>
	[ObservableProperty]
	public partial bool IsByBrand { get; set; } = true;

	/// <summary>分類軸: アイテム(ITM)</summary>
	[ObservableProperty]
	public partial bool IsByItem { get; set; }

	/// <summary>分類軸: シーズン(SZN)</summary>
	[ObservableProperty]
	public partial bool IsBySeason { get; set; }

	/// <summary>消化率の分母。true=売上+在庫 / false=期間内の投入(仕入)数。</summary>
	[ObservableProperty]
	public partial bool IsRateByStock { get; set; } = true;

	(string Kubun, string IdColumn) Category =>
		IsByItem ? ("ITM", "sh.Id_Item")
		: IsBySeason ? ("SZN", "sh.Id_Season")
		: ("BRD", "sh.Id_Brand");

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
		var uriDay = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		var shiireDay = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";

		var denominator = IsRateByStock ? "(saleSu + stockSu)" : "inSu";

		var sql = $@"
WITH uri AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        SUM({TranMeisaiSql.Num("Su")})      AS saleSu,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS saleKingaku,
        SUM({TranMeisaiSql.Num("Su")} * {TranMeisaiSql.Num("Jodai")}) AS saleJodai
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard} AND {uriDay}
    GROUP BY idShohin
),
nyu AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        SUM({TranMeisaiSql.Num("Su")}) AS inSu
    FROM Tran03Shiire h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard} AND {shiireDay}
    GROUP BY idShohin
),
zai AS (
    SELECT Id_Shohin AS idShohin, SUM(Su) AS stockSu
    FROM SummaryRealStock
    GROUP BY Id_Shohin
),
keys AS (
    SELECT idShohin FROM uri UNION SELECT idShohin FROM nyu UNION SELECT idShohin FROM zai
),
per_cat AS (
    SELECT
        ifnull(cat.Code,'(未設定)') AS catCode,
        ifnull(cat.Name,'(未設定)') AS catName,
        SUM(ifnull(n.inSu, 0))        AS inSu,
        SUM(ifnull(u.saleSu, 0))      AS saleSu,
        SUM(ifnull(z.stockSu, 0))     AS stockSu,
        SUM(ifnull(u.saleKingaku, 0)) AS saleKingaku,
        SUM(ifnull(u.saleJodai, 0))   AS saleJodai,
        SUM(ifnull(u.saleSu, 0) * ifnull(sh.TankaGenka, 0)) AS saleGenka,
        COUNT(*)                      AS shohinCount
    FROM keys k
    LEFT JOIN MasterShohin sh ON sh.Id = k.idShohin
    LEFT JOIN MasterMeisho cat ON cat.Id = {idColumn} AND cat.Kubun = '{kubun}'
    LEFT JOIN uri u ON u.idShohin = k.idShohin
    LEFT JOIN nyu n ON n.idShohin = k.idShohin
    LEFT JOIN zai z ON z.idShohin = k.idShohin
    GROUP BY catCode, catName
)
SELECT
    catCode, catName,
    shohinCount,
    inSu, saleSu, stockSu,
    CASE WHEN {denominator} != 0
         THEN ROUND(CAST(saleSu AS REAL) / {denominator} * 100, 1)
         ELSE 0 END AS shokaRatio,
    saleKingaku,
    CASE WHEN saleJodai != 0
         THEN ROUND(CAST(saleJodai - saleGenka AS REAL) / saleJodai * 100, 1)
         ELSE 0 END AS neireRatio,
    CASE WHEN SUM(saleKingaku) OVER () != 0
         THEN ROUND(CAST(saleKingaku AS REAL) / SUM(saleKingaku) OVER () * 100, 1)
         ELSE 0 END AS shareRatio
FROM per_cat
ORDER BY catCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
