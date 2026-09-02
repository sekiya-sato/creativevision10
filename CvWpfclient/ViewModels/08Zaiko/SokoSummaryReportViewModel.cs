using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 倉庫別在庫集計表。現在庫(SummaryRealStock)を倉庫×分類で集計し、
/// SKU数・在庫数・原価金額・上代金額・倉庫内構成比を印字する。在庫資産の内訳確認用。
///
/// 分類の軸はブランド／アイテムを選べる（どちらも MasterMeisho の区分違い）。
/// 単価は商品マスタの現在単価を使うため、金額は「現在の単価 × 在庫数」。
/// </summary>
public partial class SokoSummaryReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "倉庫別在庫集計表";
	protected override string FormFileName => "SokoSummaryReport.qfm";

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	/// <summary>分類軸。true=ブランド別(BRD) / false=アイテム別(ITM)。</summary>
	[ObservableProperty]
	public partial bool IsByBrand { get; set; } = true;

	/// <summary>出力対象。true=在庫数0を除く / false=全て。</summary>
	[ObservableProperty]
	public partial bool ExcludeZero { get; set; } = true;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = "1=1";
		where += BuildCodeRangeWhere(parameters, StockSql.SokoCode(), SokoCodeFrom, SokoCodeTo);
		if (ExcludeZero) {
			where += " AND s.Su != 0";
		}

		// ブランドとアイテムはどちらも MasterMeisho。区分と参照する商品マスタ列だけが違う。
		var (kubun, idColumn) = IsByBrand ? (MasterMeisho.KubunBrand, "sh.Id_Brand") : (MasterMeisho.KubunItem, "sh.Id_Item");

		var sql = $@"
WITH agg AS (
    SELECT
        {StockSql.SokoCode()}      AS sokoCode,
        {StockSql.SokoName()}      AS sokoName,
        ifnull(cat.Code,'(未設定)') AS catCode,
        ifnull(cat.Name,'(未設定)') AS catName,
        COUNT(*)                                    AS skuCount,
        SUM(s.Su)                                   AS su,
        SUM(s.Su * {StockSql.TankaGenka()})         AS genkaKingaku,
        SUM(s.Su * {StockSql.TankaJodai()})         AS jodaiKingaku
    FROM SummaryRealStock s
{StockSql.JoinSku()}
{StockSql.JoinSoko()}
    LEFT JOIN MasterMeisho cat ON cat.Id = {idColumn} AND cat.Kubun = '{kubun}'
    WHERE {where}
    GROUP BY sokoCode, sokoName, catCode, catName
)
SELECT
    sokoCode, sokoName,
    catCode, catName,
    skuCount, su,
    genkaKingaku,
    jodaiKingaku,
    CASE WHEN jodaiKingaku != 0
         THEN ROUND(CAST(genkaKingaku AS REAL) / jodaiKingaku * 100, 1)
         ELSE 0 END AS genkaRatio,
    SUM(genkaKingaku) OVER (PARTITION BY sokoCode) AS sokoTotal,
    CASE WHEN SUM(genkaKingaku) OVER (PARTITION BY sokoCode) != 0
         THEN ROUND(CAST(genkaKingaku AS REAL) / SUM(genkaKingaku) OVER (PARTITION BY sokoCode) * 100, 1)
         ELSE 0 END AS shareRatio
FROM agg
ORDER BY sokoCode, catCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
