using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 移動未受リスト。出庫済み(Tran10IdoOut)だが入庫(Tran11IdoIn)が済んでいない移動を SKU 別に列挙する。
/// 出庫数と受入数を突き合わせ、未受数（出庫数−受入数）が残るものを検出する。
///
/// 【前提】入庫伝票と出庫伝票の紐付けは Tran11IdoIn.RelateNo1（関連伝票NO）に
/// 出庫伝票の Id が入っている前提で実装している。
/// 移動受入力(IdoInputUke)は Phase 10 で実装予定の未実装画面であり、紐付けの実装がまだ存在しない。
/// 移動受入力を実装する際に、この前提（RelateNo1 に出庫伝票Idを入れる）を必ず満たすこと。
/// 別方式（手入力NO照合など）を採る場合は、このSQLの結合条件も合わせて直す必要がある。
/// </summary>
public partial class IdoUnreceivedListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "移動未受リスト";
	protected override string FormFileName => "IdoUnreceivedList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddDays(-30).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=未受が残るもののみ / false=受入済みも含めて全件。</summary>
	[ObservableProperty]
	public partial bool IsUnreceivedOnly { get; set; } = true;

	/// <summary>集計単位。true=SKU別明細 / false=伝票単位の合計。</summary>
	[ObservableProperty]
	public partial bool IsBySku { get; set; } = true;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("出庫日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo);

		// SKU を潰す場合は伝票単位の合計になる
		var skuKey = IsBySku ? TranMeisaiSql.Num("Id_Shohin") : "0";
		var colKey = IsBySku ? TranMeisaiSql.Num("Id_Col") : "0";
		var sizKey = IsBySku ? TranMeisaiSql.Num("Id_Siz") : "0";
		var shohinCode = IsBySku ? "MAX(shohinCode)" : "'(伝票計)'";
		var shohinName = IsBySku ? "MAX(shohinName)" : "''";
		var colName = IsBySku ? "MAX(colName)" : "''";
		var sizName = IsBySku ? "MAX(sizName)" : "''";
		var having = IsUnreceivedOnly ? "WHERE unreceivedSu != 0" : "";

		var sql = $@"
WITH outs AS (
    SELECT
        h.Id                                 AS denNo,
        h.DenDay                             AS denDay,
        {TranMeisaiSql.HeaderCode("VSoko")}  AS sokoCode,
        {TranMeisaiSql.HeaderName("VSoko")}  AS sokoName,
        {TranMeisaiSql.HeaderName("VIdo")}   AS idoName,
        {skuKey}                             AS idShohin,
        {colKey}                             AS idCol,
        {sizKey}                             AS idSiz,
        {TranMeisaiSql.Str("Code_Shohin")}   AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}    AS shohinName,
        {TranMeisaiSql.Str("Mei_Col")}       AS colName,
        {TranMeisaiSql.Str("Mei_Siz")}       AS sizName,
        {TranMeisaiSql.Num("Su")}            AS su
    FROM Tran10IdoOut h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
-- 入庫は RelateNo1 に出庫伝票Idが入っている前提で紐付ける
ins AS (
    SELECT
        h.RelateNo1                      AS denNo,
        {skuKey}                         AS idShohin,
        {colKey}                         AS idCol,
        {sizKey}                         AS idSiz,
        {TranMeisaiSql.Num("Su")}        AS su
    FROM Tran11IdoIn h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND h.RelateNo1 > 0
),
out_agg AS (
    SELECT
        denNo, MAX(denDay) AS denDay,
        MAX(sokoCode) AS sokoCode, MAX(sokoName) AS sokoName, MAX(idoName) AS idoName,
        idShohin, idCol, idSiz,
        {shohinCode} AS shohinCode, {shohinName} AS shohinName,
        {colName} AS colName, {sizName} AS sizName,
        SUM(su) AS outSu
    FROM outs
    GROUP BY denNo, idShohin, idCol, idSiz
),
in_agg AS (
    SELECT denNo, idShohin, idCol, idSiz, SUM(su) AS inSu
    FROM ins
    GROUP BY denNo, idShohin, idCol, idSiz
),
joined AS (
    SELECT
        o.*,
        ifnull(i.inSu, 0)            AS inSu,
        o.outSu - ifnull(i.inSu, 0)  AS unreceivedSu
    FROM out_agg o
    LEFT JOIN in_agg i
           ON i.denNo = o.denNo AND i.idShohin = o.idShohin
          AND i.idCol = o.idCol AND i.idSiz = o.idSiz
)
SELECT
    {TranMeisaiSql.DateLabel("denDay")} AS denDayLabel,
    CAST(denNo AS TEXT) AS denNoText,
    sokoCode, sokoName, idoName,
    shohinCode, shohinName,
    colName, sizName,
    outSu, inSu, unreceivedSu
FROM joined
{having}
ORDER BY denDay, denNo, shohinCode, colName, sizName";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
