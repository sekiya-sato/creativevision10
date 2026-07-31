using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 得意先別売上予定表。得意先別に「受注済みだが売上計上されていない残」を売上予定額として集計する。
///
/// 【注意】cv10 の受注テーブル(Tran12Jyuchu)には納品予定日の列が無いため、
/// 予定を「日付軸」で並べることはできない。したがって本帳票は
/// 「受注日の期間で絞った受注残の得意先別合計」として実装している。
/// 納品予定日ベースの予定表が必要になった場合は、受注に納品予定日を持たせる設計変更が先に必要。
///
/// 【前提】売上の紐付けは Tran00Uriage.RelateNo1 に受注伝票Id が入っている前提（受注残管理表と同じ規約）。
/// </summary>
public partial class TokuiSakiUriageYoteiTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "得意先別売上予定表";
	protected override string FormFileName => "TokuiSakiUriageYoteiTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddDays(-180).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=予定額が残る得意先のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsPendingOnly { get; set; } = true;

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
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo)
			+ " AND h.Kubun = 10";

		var having = IsPendingOnly ? "HAVING SUM(zanKingaku) != 0" : "";

		var sql = $@"
WITH juchu AS (
    SELECT
        h.Id AS denNo,
        {TranMeisaiSql.HeaderCode("VTokui")} AS tokuiCode,
        {TranMeisaiSql.HeaderName("VTokui")} AS tokuiName,
        h.DenDay       AS denDay,
        h.SuTotal      AS juchuSu,
        h.KingakuTotal AS juchuKingaku
    FROM Tran12Jyuchu h
    WHERE {where}
),
uriage AS (
    SELECT RelateNo1 AS denNo, SUM(SuTotal) AS uriageSu, SUM(KingakuTotal) AS uriageKingaku
    FROM Tran00Uriage
    WHERE RelateNo1 > 0
    GROUP BY RelateNo1
),
per_den AS (
    SELECT
        j.tokuiCode, j.tokuiName, j.denNo, j.denDay,
        j.juchuSu, j.juchuKingaku,
        ifnull(u.uriageSu, 0)       AS uriageSu,
        ifnull(u.uriageKingaku, 0)  AS uriageKingaku,
        j.juchuSu - ifnull(u.uriageSu, 0) AS zanSu,
        CASE WHEN j.juchuSu != 0
             THEN CAST(ROUND(CAST(j.juchuKingaku AS REAL) * (j.juchuSu - ifnull(u.uriageSu,0)) / j.juchuSu) AS INTEGER)
             ELSE 0 END AS zanKingaku
    FROM juchu j
    LEFT JOIN uriage u ON u.denNo = j.denNo
)
SELECT
    tokuiCode, tokuiName,
    COUNT(*)             AS denCount,
    SUM(juchuSu)         AS juchuSu,
    SUM(juchuKingaku)    AS juchuKingaku,
    SUM(uriageSu)        AS uriageSu,
    SUM(uriageKingaku)   AS uriageKingaku,
    SUM(zanSu)           AS zanSu,
    SUM(zanKingaku)      AS yoteiKingaku,
    {TranMeisaiSql.DateLabel("MAX(denDay)")} AS lastJuchuDay
FROM per_den
GROUP BY tokuiCode, tokuiName
{having}
ORDER BY tokuiCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
