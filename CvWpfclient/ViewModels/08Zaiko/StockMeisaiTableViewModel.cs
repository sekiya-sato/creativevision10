using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 棚卸明細表。棚卸入力(Tran60Tana)の明細を SKU 別に集計し、理論在庫(SummaryRealStock)と突き合わせて
/// 差異数・差異金額を印字する。棚卸チェックリストが入力そのものの確認なのに対し、こちらは差異の確定用。
///
/// 棚卸数は同一SKUへ複数棚（棚番）から入力されることがあるため、SKU単位に合計してから比較する。
/// 理論在庫は現在庫(SummaryRealStock)を見るので、棚卸後に在庫が動くと差異がずれる。
/// 棚卸日直後に出力する運用を前提にしている。
/// </summary>
public partial class StockMeisaiTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "棚卸明細表";
	protected override string FormFileName => "StockMeisaiTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=差異があるSKUのみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsDiffOnly { get; set; }

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("棚卸日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);

		var having = IsDiffOnly ? "WHERE actualSu - theoreticalSu != 0" : "";

		var sql = $@"
WITH tana AS (
    -- 同一SKUへ複数棚から入力されることがあるので、SKU単位に合計する
    SELECT
        h.Id_Soko                            AS idSoko,
        {TranMeisaiSql.HeaderCode("VSoko")}  AS sokoCode,
        {TranMeisaiSql.HeaderName("VSoko")}  AS sokoName,
        {TranMeisaiSql.Num("Id_Shohin")}     AS idShohin,
        {TranMeisaiSql.Num("Id_Col")}        AS idCol,
        {TranMeisaiSql.Num("Id_Siz")}        AS idSiz,
        {TranMeisaiSql.Str("Code_Shohin")}   AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}    AS shohinName,
        {TranMeisaiSql.Str("Mei_Col")}       AS colName,
        {TranMeisaiSql.Str("Mei_Siz")}       AS sizName,
        {TranMeisaiSql.Num("Su")}            AS su,
        h.DenDay                             AS denDay
    FROM Tran60Tana h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
agg AS (
    SELECT
        idSoko, sokoCode, sokoName,
        idShohin, idCol, idSiz,
        MAX(shohinCode) AS shohinCode,
        MAX(shohinName) AS shohinName,
        MAX(colName)    AS colName,
        MAX(sizName)    AS sizName,
        SUM(su)         AS actualSu,
        MAX(denDay)     AS lastDenDay,
        COUNT(*)        AS tanaCount
    FROM tana
    GROUP BY idSoko, sokoCode, sokoName, idShohin, idCol, idSiz
),
joined AS (
    SELECT
        a.*,
        ifnull(rs.Su, 0)                 AS theoreticalSu,
        ifnull(sh.TankaGenka, 0)         AS genkaTanka
    FROM agg a
    LEFT JOIN SummaryRealStock rs
           ON rs.Id_Soko = a.idSoko AND rs.Id_Shohin = a.idShohin
          AND rs.Id_Col = a.idCol AND rs.Id_Siz = a.idSiz
    LEFT JOIN MasterShohin sh ON sh.Id = a.idShohin
)
SELECT
    {TranMeisaiSql.DateLabel("lastDenDay")} AS denDayLabel,
    sokoCode, sokoName,
    shohinCode, shohinName,
    colName, sizName,
    theoreticalSu,
    actualSu,
    actualSu - theoreticalSu AS diffSu,
    genkaTanka,
    (actualSu - theoreticalSu) * genkaTanka AS diffKingaku
FROM joined
{having}
ORDER BY sokoCode, shohinCode, colName, sizName";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
