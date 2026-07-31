using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 投入売上在庫表。品番ごとに「投入（仕入で入れた数）・売上・在庫」を並べて消化状況を見る。
///
/// 投入 = 期間内の仕入(Tran03Shiire)明細の数量。売上 = 期間内の店舗売上(Tran01Tenuri)明細の数量。
/// 在庫 = 現在庫(SummaryRealStock)。**在庫は期間末時点ではなく現在時点**である点に注意。
/// 期間末の在庫が必要な場合は年月別在庫集計を使う商品別受払表(08Zaiko)を参照する。
///
/// 消化率 = 売上数 ÷ 投入数。投入0で売上がある（期間前に投入済み）場合は 0 を返す。
/// </summary>
public partial class InputSalesStockReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "投入売上在庫表";
	protected override string FormFileName => "InputSalesStockReport.qfm";

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

	/// <summary>集計単位。true=色サイズ別 / false=商品計。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; }

	/// <summary>出力対象。true=投入･売上･在庫のいずれかがあるもののみ / false=全て。</summary>
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

		var byCs = IsByColorSize;
		// 商品計のときは色サイズIdを潰して1本にまとめる
		string ColKey(bool meisai) => byCs ? (meisai ? TranMeisaiSql.Num("Id_Col") : "s.Id_Col") : "0";
		string SizKey(bool meisai) => byCs ? (meisai ? TranMeisaiSql.Num("Id_Siz") : "s.Id_Siz") : "0";

		List<string> parameters = [];
		var shiireDay = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		var shiireShohin = BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		var uriDay = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		var uriShohin = BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		var brandWhere = BuildCodeRangeWhere(parameters, "ifnull(br.Code,'')", BrandCodeFrom, BrandCodeTo);

		var having = IsActiveOnly ? "WHERE inSu != 0 OR saleSu != 0 OR stockSu != 0" : "";

		var sql = $@"
WITH nyu AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        {ColKey(true)} AS idCol, {SizKey(true)} AS idSiz,
        SUM({TranMeisaiSql.Num("Su")})      AS inSu,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS inKingaku
    FROM Tran03Shiire h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard} AND {shiireDay}{shiireShohin}
    GROUP BY idShohin, idCol, idSiz
),
uri AS (
    SELECT
        {TranMeisaiSql.Num("Id_Shohin")} AS idShohin,
        {ColKey(true)} AS idCol, {SizKey(true)} AS idSiz,
        SUM({TranMeisaiSql.Num("Su")})      AS saleSu,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS saleKingaku
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard} AND {uriDay}{uriShohin}
    GROUP BY idShohin, idCol, idSiz
),
zai AS (
    SELECT
        s.Id_Shohin AS idShohin,
        {ColKey(false)} AS idCol, {SizKey(false)} AS idSiz,
        SUM(s.Su) AS stockSu
    FROM SummaryRealStock s
    GROUP BY idShohin, idCol, idSiz
),
keys AS (
    SELECT idShohin, idCol, idSiz FROM nyu
    UNION SELECT idShohin, idCol, idSiz FROM uri
    UNION SELECT idShohin, idCol, idSiz FROM zai
),
joined AS (
    SELECT
        ifnull(sh.Code,'') AS shohinCode,
        ifnull(sh.Name,'') AS shohinName,
        ifnull(cs.Mei_Col,'') AS colName,
        ifnull(cs.Mei_Siz,'') AS sizName,
        ifnull(cs.Code_Col,'') AS colCode,
        ifnull(cs.Code_Siz,'') AS sizCode,
        ifnull(n.inSu, 0)        AS inSu,
        ifnull(n.inKingaku, 0)   AS inKingaku,
        ifnull(u.saleSu, 0)      AS saleSu,
        ifnull(u.saleKingaku, 0) AS saleKingaku,
        ifnull(z.stockSu, 0)     AS stockSu,
        ifnull(sh.TankaGenka, 0) AS genkaTanka
    FROM keys k
    LEFT JOIN MasterShohin sh ON sh.Id = k.idShohin
    LEFT JOIN DerivedShohinColSiz cs
           ON cs.Id_Shohin = k.idShohin AND cs.Id_Col = k.idCol AND cs.Id_Siz = k.idSiz
    LEFT JOIN MasterMeisho br ON br.Id = sh.Id_Brand AND br.Kubun = 'BRD'
    LEFT JOIN nyu n ON n.idShohin = k.idShohin AND n.idCol = k.idCol AND n.idSiz = k.idSiz
    LEFT JOIN uri u ON u.idShohin = k.idShohin AND u.idCol = k.idCol AND u.idSiz = k.idSiz
    LEFT JOIN zai z ON z.idShohin = k.idShohin AND z.idCol = k.idCol AND z.idSiz = k.idSiz
    WHERE 1=1 {brandWhere}
)
SELECT
    shohinCode, shohinName,
    colName, sizName,
    inSu, inKingaku,
    saleSu, saleKingaku,
    stockSu,
    CASE WHEN inSu != 0 THEN ROUND(CAST(saleSu AS REAL) / inSu * 100, 1) ELSE 0 END AS shokaRatio,
    stockSu * genkaTanka AS stockKingaku
FROM joined
{having}
ORDER BY shohinCode, colCode, sizCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
