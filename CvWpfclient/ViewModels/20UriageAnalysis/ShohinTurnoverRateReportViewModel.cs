using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 商品消化率表。商品単位に売上数と在庫数から消化率を出し、値入率と併せて印字する。
///
/// 消化率の定義は現場によって「売上÷投入」と「売上÷(売上+在庫)」の2通りがある。
/// 投入実績（仕入）が期間外にある商品では前者が100%を超えたり0になったりするため、
/// 本帳票は既定で後者（売上÷(売上+在庫)）を使い、投入基準も選べるようにしている。
/// 投入基準を選んだ場合の投入数は投入売上在庫表と同じ「期間内の仕入数量」。
///
/// 値入率 = (上代金額 − 原価金額) ÷ 上代金額。原価は商品マスタの現在原価。
/// </summary>
public partial class ShohinTurnoverRateReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "商品消化率表";
	protected override string FormFileName => "ShohinTurnoverRateReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddMonths(-3).ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	/// <summary>消化率の分母。true=売上+在庫 / false=期間内の投入(仕入)数。</summary>
	[ObservableProperty]
	public partial bool IsRateByStock { get; set; } = true;

	/// <summary>出力対象。true=売上または在庫があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	[RelayCommand]
	void SelectBrandCodeFrom() => BrandCodeFrom = SelectBrandCode() ?? BrandCodeFrom;

	[RelayCommand]
	void SelectBrandCodeTo() => BrandCodeTo = SelectBrandCode() ?? BrandCodeTo;

	string? SelectBrandCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code")?.Code;

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
		var uriDay = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		var uriShohin = BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		var shiireDay = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		var shiireShohin = BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		var brandWhere = BuildCodeRangeWhere(parameters, "ifnull(br.Code,'')", BrandCodeFrom, BrandCodeTo);

		// 分母の切替。0除算はCASEで回避する
		var denominator = IsRateByStock ? "(saleSu + stockSu)" : "inSu";
		var having = IsActiveOnly ? "WHERE saleSu != 0 OR stockSu != 0" : "";

		var sql = $@"
WITH uri AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        SUM({TranMeisaiSql.Num("Su")})      AS saleSu,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS saleKingaku,
        SUM({TranMeisaiSql.Num("Su")} * {TranMeisaiSql.Num("Jodai")}) AS saleJodai,
        MIN(h.DenDay) AS firstDay
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard} AND {uriDay}{uriShohin}
    GROUP BY idShohin
),
nyu AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        SUM({TranMeisaiSql.Num("Su")}) AS inSu
    FROM Tran03Shiire h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard} AND {shiireDay}{shiireShohin}
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
joined AS (
    SELECT
        ifnull(sh.Code,'') AS shohinCode,
        ifnull(sh.Name,'') AS shohinName,
        ifnull(br.Code,'') AS brandCode,
        ifnull(br.Name,'') AS brandName,
        ifnull(n.inSu, 0)          AS inSu,
        ifnull(u.saleSu, 0)        AS saleSu,
        ifnull(u.saleKingaku, 0)   AS saleKingaku,
        ifnull(u.saleJodai, 0)     AS saleJodai,
        ifnull(z.stockSu, 0)       AS stockSu,
        ifnull(u.saleSu, 0) * ifnull(sh.TankaGenka, 0) AS saleGenka,
        ifnull(u.firstDay, '')     AS firstDay
    FROM keys k
    LEFT JOIN MasterShohin sh ON sh.Id = k.idShohin
    LEFT JOIN MasterMeisho br ON br.Id = sh.Id_Brand AND br.Kubun = 'BRD'
    LEFT JOIN uri u ON u.idShohin = k.idShohin
    LEFT JOIN nyu n ON n.idShohin = k.idShohin
    LEFT JOIN zai z ON z.idShohin = k.idShohin
    WHERE 1=1 {brandWhere}
)
SELECT
    shohinCode, shohinName,
    brandName,
    inSu, saleSu, stockSu,
    CASE WHEN {denominator} != 0
         THEN ROUND(CAST(saleSu AS REAL) / {denominator} * 100, 1)
         ELSE 0 END AS shokaRatio,
    saleKingaku,
    saleJodai,
    CASE WHEN saleJodai != 0
         THEN ROUND(CAST(saleJodai - saleGenka AS REAL) / saleJodai * 100, 1)
         ELSE 0 END AS neireRatio,
    {TranMeisaiSql.DateLabel("firstDay")} AS firstDayLabel
FROM joined
{having}
ORDER BY shohinCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
