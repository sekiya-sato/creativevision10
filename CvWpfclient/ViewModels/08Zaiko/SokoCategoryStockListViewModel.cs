using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 倉庫分類別棚卸表。倉庫×分類（ブランド／アイテム）ごとに SKU 明細を並べた棚卸用のリスト。
/// 倉庫別在庫集計表が金額の内訳を見るのに対し、こちらは実棚で数える順に SKU を列挙する。
///
/// 実棚記入欄を紙に設けるため、理論在庫は出すが差異欄は空にしている（手書き用）。
/// 記入後の差異確認は棚卸明細表が担当する。
/// </summary>
public partial class SokoCategoryStockListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "倉庫分類別棚卸表";
	protected override string FormFileName => "SokoCategoryStockList.qfm";

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CategoryCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CategoryCodeTo { get; set; } = string.Empty;

	/// <summary>分類軸。true=ブランド別(BRD) / false=アイテム別(ITM)。</summary>
	[ObservableProperty]
	public partial bool IsByBrand { get; set; } = true;

	/// <summary>出力対象。true=在庫数0を除く / false=全て（0在庫も数えたい場合）。</summary>
	[ObservableProperty]
	public partial bool ExcludeZero { get; set; } = true;

	/// <summary>true=理論在庫数を印字 / false=空欄にする（先入観なく数えたい場合）。</summary>
	[ObservableProperty]
	public partial bool ShowTheoretical { get; set; } = true;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectCategoryCodeFrom() => CategoryCodeFrom = SelectCategoryCode() ?? CategoryCodeFrom;

	[RelayCommand]
	void SelectCategoryCodeTo() => CategoryCodeTo = SelectCategoryCode() ?? CategoryCodeTo;

	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	string? SelectCategoryCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{(IsByBrand ? MasterMeisho.KubunBrand : MasterMeisho.KubunItem)}'", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = "1=1";
		where += BuildCodeRangeWhere(parameters, StockSql.SokoCode(), SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, "ifnull(cat.Code,'')", CategoryCodeFrom, CategoryCodeTo);
		if (ExcludeZero) {
			where += " AND s.Su != 0";
		}

		var (kubun, idColumn) = IsByBrand ? (MasterMeisho.KubunBrand, "sh.Id_Brand") : (MasterMeisho.KubunItem, "sh.Id_Item");
		// 実棚を手書きする運用では理論在庫を出さないことがある
		var theoretical = ShowTheoretical ? "s.Su" : "''";

		var sql = $@"
SELECT
    {StockSql.SokoCode()}      AS sokoCode,
    {StockSql.SokoName()}      AS sokoName,
    ifnull(cat.Code,'(未設定)') AS catCode,
    ifnull(cat.Name,'(未設定)') AS catName,
    {StockSql.ShohinCode()}    AS shohinCode,
    {StockSql.ShohinName()}    AS shohinName,
    {StockSql.ColName()}       AS colName,
    {StockSql.SizName()}       AS sizName,
    {theoretical}              AS theoreticalSu,
    ''                         AS actualSu,
    {StockSql.TankaGenka()}    AS genkaTanka,
    s.Su * {StockSql.TankaGenka()} AS genkaKingaku
FROM SummaryRealStock s
{StockSql.JoinSku()}
{StockSql.JoinSoko()}
    LEFT JOIN MasterMeisho cat ON cat.Id = {idColumn} AND cat.Kubun = '{kubun}'
WHERE {where}
ORDER BY sokoCode, catCode, shohinCode, {StockSql.ColCode()}, {StockSql.SizCode()}";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
