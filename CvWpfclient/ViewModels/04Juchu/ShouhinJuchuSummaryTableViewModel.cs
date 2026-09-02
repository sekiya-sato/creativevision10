using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 商品別受注集計表。受注明細を分類（ブランド／アイテム）で括った集計と分類内構成比を印字する。
/// 商品別発注集計表の受注側版。分類は明細の商品Idから商品マスタを引いて判定する。
/// </summary>
public partial class ShouhinJuchuSummaryTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "商品別受注集計表";
	protected override string FormFileName => "ShouhinJuchuSummaryTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>分類軸。true=ブランド別(BRD) / false=アイテム別(ITM)。</summary>
	[ObservableProperty]
	public partial bool IsByBrand { get; set; } = true;

	/// <summary>集計単位。true=分類×品番 / false=分類計のみ。</summary>
	[ObservableProperty]
	public partial bool IsByShohin { get; set; } = true;

	/// <summary>true=返品･値引も含める / false=受注(Kubun=10)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("受注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo);
		if (!IncludeHenpin) where += " AND h.Kubun = 10";

		var (kubun, idColumn) = IsByBrand ? (MasterMeisho.KubunBrand, "s.Id_Brand") : (MasterMeisho.KubunItem, "s.Id_Item");
		var shohinCode = IsByShohin ? "shohinCode" : "''";
		var shohinName = IsByShohin ? "shohinName" : "'(分類計)'";

		var sql = $@"
WITH meisai AS (
    SELECT
        (SELECT {idColumn} FROM MasterShohin s
         WHERE s.Id = {TranMeisaiSql.Num("Id_Shohin")}) AS idCat,
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        h.Id AS denNo
    FROM Tran12Jyuchu h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
agg AS (
    SELECT
        ifnull(cat.Code,'(未設定)') AS catCode,
        ifnull(cat.Name,'(未設定)') AS catName,
        {shohinCode} AS shohinCode,
        {shohinName} AS shohinName,
        SUM(m.su)             AS su,
        SUM(m.kingaku)        AS kingaku,
        SUM(m.su * m.jodai)   AS jodaiTotal,
        COUNT(DISTINCT m.denNo) AS denCount
    FROM meisai m
    LEFT JOIN MasterMeisho cat ON cat.Id = m.idCat AND cat.Kubun = '{kubun}'
    GROUP BY catCode, catName, {shohinCode}, {shohinName}
)
SELECT
    catCode, catName,
    shohinCode, shohinName,
    su, kingaku, jodaiTotal, denCount,
    SUM(kingaku) OVER (PARTITION BY catCode) AS catTotal,
    CASE WHEN SUM(kingaku) OVER (PARTITION BY catCode) != 0
         THEN ROUND(CAST(kingaku AS REAL) / SUM(kingaku) OVER (PARTITION BY catCode) * 100, 1)
         ELSE 0 END AS shareRatio
FROM agg
ORDER BY catCode, shohinCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
