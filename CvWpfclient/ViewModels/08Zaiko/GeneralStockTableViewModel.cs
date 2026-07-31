using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 汎用在庫表。現在庫(SummaryRealStock)を倉庫×SKU で出力し、原価金額・上代金額を付ける。
/// 集計単位を SKU別 / 商品別 / 倉庫別 で切り替えられるので、棚卸準備から在庫金額確認まで兼用できる。
///
/// 単価は商品マスタの原価単価・上代単価を使う（在庫集計テーブルは単価を持たない）。
/// したがって金額は「現在の単価 × 在庫数」であり、取得時点の単価ではない。
/// </summary>
public partial class GeneralStockTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "汎用在庫表";
	protected override string FormFileName => "GeneralStockTable.qfm";

	/// <summary>集計単位</summary>
	public enum StockLevel { Sku, Shohin, Soko }

	/// <summary>
	/// 原価に関わる列を出すか。店舗向けの「原価無」派生(40Shop)が false で上書きする。
	/// false のときは原価単価・原価金額を SELECT から列ごと外す（上代金額は売価なので残す）。
	/// 列数が変わるため派生側は専用の qfm を持つ。
	/// </summary>
	protected virtual bool ShowCost => true;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	/// <summary>SKU別（商品×色×サイズ×倉庫）で出力する</summary>
	[ObservableProperty]
	public partial bool IsBySku { get; set; } = true;

	/// <summary>商品別（色サイズを合計）で出力する</summary>
	[ObservableProperty]
	public partial bool IsByShohin { get; set; }

	/// <summary>倉庫別（全商品を合計）で出力する</summary>
	[ObservableProperty]
	public partial bool IsBySoko { get; set; }

	/// <summary>出力対象。true=在庫数0を除く / false=全て。</summary>
	[ObservableProperty]
	public partial bool ExcludeZero { get; set; } = true;

	StockLevel Level => IsByShohin ? StockLevel.Shohin : IsBySoko ? StockLevel.Soko : StockLevel.Sku;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	[RelayCommand]
	void SelectBrandCodeFrom() => BrandCodeFrom = SelectBrandCode() ?? BrandCodeFrom;

	[RelayCommand]
	void SelectBrandCodeTo() => BrandCodeTo = SelectBrandCode() ?? BrandCodeTo;

	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	string? SelectBrandCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		var level = Level;
		List<string> parameters = [];
		var where = "1=1";
		where += BuildCodeRangeWhere(parameters, StockSql.SokoCode(), SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, StockSql.ShohinCode(), ShohinCodeFrom, ShohinCodeTo);
		where += BuildCodeRangeWhere(parameters, "ifnull(br.Code,'')", BrandCodeFrom, BrandCodeTo);
		if (ExcludeZero) {
			where += " AND s.Su != 0";
		}

		var costCols = ShowCost ? @"
    CASE WHEN su != 0 THEN CAST(ROUND(CAST(genkaKingaku AS REAL) / su) AS INTEGER) ELSE 0 END AS genkaTanka,
    genkaKingaku," : "";

		// 集計単位に応じてキーを潰す。潰した列は空文字を返して GROUP BY でまとめる。
		var (shohinCode, shohinName, colCode, colName, sizCode, sizName) = level switch {
			StockLevel.Soko => ("''", "'(倉庫計)'", "''", "''", "''", "''"),
			StockLevel.Shohin => (StockSql.ShohinCode(), StockSql.ShohinName(), "''", "''", "''", "''"),
			_ => (StockSql.ShohinCode(), StockSql.ShohinName(),
				  StockSql.ColCode(), StockSql.ColName(), StockSql.SizCode(), StockSql.SizName()),
		};

		var sql = $@"
WITH agg AS (
    SELECT
        {StockSql.SokoCode()}   AS sokoCode,
        {StockSql.SokoName()}   AS sokoName,
        {shohinCode}            AS shohinCode,
        {shohinName}            AS shohinName,
        {colCode}               AS colCode,
        {colName}               AS colName,
        {sizCode}               AS sizCode,
        {sizName}               AS sizName,
        SUM(s.Su)                                       AS su,
        SUM(s.Su * {StockSql.TankaGenka()})             AS genkaKingaku,
        SUM(s.Su * {StockSql.TankaJodai()})             AS jodaiKingaku,
        COUNT(*)                                        AS skuCount
    FROM SummaryRealStock s
{StockSql.JoinSku()}
{StockSql.JoinSoko()}
    LEFT JOIN MasterMeisho br ON br.Id = sh.Id_Brand AND br.Kubun = 'BRD'
    WHERE {where}
    GROUP BY sokoCode, sokoName, shohinCode, shohinName, colCode, colName, sizCode, sizName
)
SELECT
    sokoCode, sokoName,
    shohinCode, shohinName,
    colCode, colName, sizCode, sizName,
    su,{costCols}
    jodaiKingaku,
    skuCount
FROM agg
ORDER BY sokoCode, shohinCode, colCode, sizCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
