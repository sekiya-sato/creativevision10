using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 仕入未受リスト。発注済み(Tran13Hachu)だが入荷(Tran03Shiire)が済んでいない分を SKU 別に列挙する。
/// 発注数と入荷数を突き合わせ、未入荷数（発注数−入荷数）が残るものを検出する。現場での入荷待ち確認用。
///
/// 発注残管理表が仕入先／伝票単位で残高と経過日数を管理するのに対し、こちらは SKU 明細を並べる。
///
/// 【前提】仕入伝票と発注伝票の紐付けは Tran03Shiire.RelateNo1（関連伝票NO）に
/// 発注伝票の Id が入っている前提で実装している。移動未受リストと同じ紐付け規約。
/// 仕入入力(ShiireInputView)は実装済みだが発注からの引き当てUIは未整備なので、
/// 発注引き当てを実装する際はこの前提（RelateNo1 に発注伝票Idを入れる）を満たすこと。
/// </summary>
public partial class PendingShiireListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "仕入未受リスト";
	protected override string FormFileName => "PendingShiireList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddDays(-60).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=未入荷が残るもののみ / false=入荷済みも含む。</summary>
	[ObservableProperty]
	public partial bool IsPendingOnly { get; set; } = true;

	/// <summary>集計単位。true=SKU別明細 / false=伝票単位の合計。</summary>
	[ObservableProperty]
	public partial bool IsBySku { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("発注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo)
			+ " AND h.Kubun = 10";   // 発注のみ（返品･値引は入荷対象ではない）

		var skuKey = IsBySku ? TranMeisaiSql.Num("Id_Shohin") : "0";
		var colKey = IsBySku ? TranMeisaiSql.Num("Id_Col") : "0";
		var sizKey = IsBySku ? TranMeisaiSql.Num("Id_Siz") : "0";
		var shohinCode = IsBySku ? "MAX(shohinCode)" : "'(伝票計)'";
		var shohinName = IsBySku ? "MAX(shohinName)" : "''";
		var colName = IsBySku ? "MAX(colName)" : "''";
		var sizName = IsBySku ? "MAX(sizName)" : "''";
		var having = IsPendingOnly ? "WHERE pendingSu != 0" : "";

		var sql = $@"
WITH hachu AS (
    SELECT
        h.Id                                  AS denNo,
        h.DenDay                              AS denDay,
        {TranMeisaiSql.HeaderCode("VShiire")} AS shiireCode,
        {TranMeisaiSql.HeaderName("VShiire")} AS shiireName,
        {skuKey} AS idShohin, {colKey} AS idCol, {sizKey} AS idSiz,
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {TranMeisaiSql.Str("Mei_Col")}     AS colName,
        {TranMeisaiSql.Str("Mei_Siz")}     AS sizName,
        {TranMeisaiSql.Num("Su")}          AS su,
        {TranMeisaiSql.Num("Kingaku")}     AS kingaku
    FROM Tran13Hachu h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
-- 入荷は RelateNo1 に発注伝票Idが入っている前提で紐付ける
nyuka AS (
    SELECT
        h.RelateNo1 AS denNo,
        {skuKey} AS idShohin, {colKey} AS idCol, {sizKey} AS idSiz,
        {TranMeisaiSql.Num("Su")} AS su
    FROM Tran03Shiire h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND h.RelateNo1 > 0
),
hachu_agg AS (
    SELECT
        denNo, MAX(denDay) AS denDay,
        MAX(shiireCode) AS shiireCode, MAX(shiireName) AS shiireName,
        idShohin, idCol, idSiz,
        {shohinCode} AS shohinCode, {shohinName} AS shohinName,
        {colName} AS colName, {sizName} AS sizName,
        SUM(su) AS hachuSu, SUM(kingaku) AS kingaku
    FROM hachu
    GROUP BY denNo, idShohin, idCol, idSiz
),
nyuka_agg AS (
    SELECT denNo, idShohin, idCol, idSiz, SUM(su) AS nyukaSu
    FROM nyuka
    GROUP BY denNo, idShohin, idCol, idSiz
),
joined AS (
    SELECT
        a.*,
        ifnull(n.nyukaSu, 0)            AS nyukaSu,
        a.hachuSu - ifnull(n.nyukaSu, 0) AS pendingSu
    FROM hachu_agg a
    LEFT JOIN nyuka_agg n
           ON n.denNo = a.denNo AND n.idShohin = a.idShohin
          AND n.idCol = a.idCol AND n.idSiz = a.idSiz
)
SELECT
    {TranMeisaiSql.DateLabel("denDay")} AS denDayLabel,
    CAST(denNo AS TEXT) AS denNoText,
    shiireCode, shiireName,
    shohinCode, shohinName,
    colName, sizName,
    hachuSu, nyukaSu, pendingSu,
    kingaku
FROM joined
{having}
ORDER BY denDay, denNo, shohinCode, colName, sizName";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
