using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 発注残管理表。発注伝票単位に「発注数・入荷数・残数・残金額・発注からの経過日数」を出して、
/// 長期化している発注残を管理する。仕入未受リストが SKU 明細を並べるのに対し、こちらは伝票単位。
///
/// 経過日数は「今日 − 発注日」。滞留の目安として区分ラベル（30日未満/30日以上/60日以上/90日以上）も出す。
/// 基準日はSQL側の date('now') ではなくクライアントの日付を埋め込む
/// （サーバとクライアントのタイムゾーン差で日数がずれないようにするため）。
///
/// 【前提】入荷の紐付けは Tran03Shiire.RelateNo1 に発注伝票Id が入っている前提。
/// 仕入未受リストと同じ規約。詳細は PendingShiireListViewModel のコメント参照。
/// </summary>
public partial class HachuZanKanriTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "発注残管理表";
	protected override string FormFileName => "HachuZanKanriTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddDays(-180).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>滞留日数の下限（この日数以上経過した発注残のみ出す）。空欄なら絞らない。</summary>
	[ObservableProperty]
	public partial string MinElapsedDaysText { get; set; } = string.Empty;

	/// <summary>出力対象。true=残がある伝票のみ / false=完了分も含む。</summary>
	[ObservableProperty]
	public partial bool IsPendingOnly { get; set; } = true;

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
		var minDays = 0;
		if (MinElapsedDaysText.Trim().Length > 0
			&& (!int.TryParse(MinElapsedDaysText.Trim(), out minDays) || minDays < 0)) {
			MessageEx.ShowWarningDialog("滞留日数は0以上の数値で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo)
			+ " AND h.Kubun = 10";

		// 基準日はクライアント日付を埋め込む（サーバのタイムゾーンに依存させない）
		var today = DateTime.Today.ToString("yyyy-MM-dd");

		var conditions = new List<string>();
		if (IsPendingOnly) conditions.Add("zanSu != 0");
		if (minDays > 0) conditions.Add($"elapsedDays >= {minDays}");
		var having = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

		var sql = $@"
WITH hachu AS (
    SELECT
        h.Id     AS denNo,
        h.DenDay AS denDay,
        {TranMeisaiSql.HeaderCode("VShiire")} AS shiireCode,
        {TranMeisaiSql.HeaderName("VShiire")} AS shiireName,
        h.SuTotal      AS hachuSu,
        h.KingakuTotal AS hachuKingaku,
        h.NouhinDay    AS nouhinDay,
        h.EndFlag      AS endFlag
    FROM Tran13Hachu h
    WHERE {where}
),
nyuka AS (
    SELECT RelateNo1 AS denNo, SUM(SuTotal) AS nyukaSu, MAX(DenDay) AS lastNyukaDay
    FROM Tran03Shiire
    WHERE RelateNo1 > 0
    GROUP BY RelateNo1
),
joined AS (
    SELECT
        a.denNo, a.denDay, a.shiireCode, a.shiireName,
        a.hachuSu, a.hachuKingaku,
        ifnull(n.nyukaSu, 0)             AS nyukaSu,
        a.hachuSu - ifnull(n.nyukaSu, 0) AS zanSu,
        n.lastNyukaDay                   AS lastNyukaDay,
        -- 発注数が0の伝票で0除算しないようガードする
        CASE WHEN a.hachuSu != 0
             THEN CAST(ROUND(CAST(a.hachuKingaku AS REAL) * (a.hachuSu - ifnull(n.nyukaSu,0)) / a.hachuSu) AS INTEGER)
             ELSE 0 END                  AS zanKingaku,
        CAST(julianday('{today}')
             - julianday(substr(a.denDay,1,4) || '-' || substr(a.denDay,5,2) || '-' || substr(a.denDay,7,2))
             AS INTEGER)                 AS elapsedDays,
        a.nouhinDay                      AS nouhinDay,
        -- 納期遅れ日数: 納品予定日が非空・未完了・予定日を過ぎている場合のみ (today - 納品予定日)
        CASE WHEN a.nouhinDay != '' AND a.endFlag = 0
             AND julianday('{today}') > julianday(substr(a.nouhinDay,1,4) || '-' || substr(a.nouhinDay,5,2) || '-' || substr(a.nouhinDay,7,2))
             THEN CAST(julianday('{today}')
                  - julianday(substr(a.nouhinDay,1,4) || '-' || substr(a.nouhinDay,5,2) || '-' || substr(a.nouhinDay,7,2))
                  AS INTEGER)
             ELSE 0 END                  AS delayDays
    FROM hachu a
    LEFT JOIN nyuka n ON n.denNo = a.denNo
)
SELECT
    shiireCode, shiireName,
    {TranMeisaiSql.DateLabel("denDay")} AS denDayLabel,
    CAST(denNo AS TEXT) AS denNoText,
    hachuSu, nyukaSu, zanSu, zanKingaku,
    elapsedDays,
    CASE
        WHEN elapsedDays >= 90 THEN '90日以上'
        WHEN elapsedDays >= 60 THEN '60日以上'
        WHEN elapsedDays >= 30 THEN '30日以上'
        ELSE '30日未満'
    END AS elapsedLabel,
    {TranMeisaiSql.DateLabel("ifnull(lastNyukaDay,'')")} AS lastNyukaDayLabel,
    CASE WHEN nouhinDay = '' THEN '' ELSE {TranMeisaiSql.DateLabel("nouhinDay")} END AS nouhinDayLabel,
    CASE WHEN delayDays > 0 THEN CAST(delayDays AS TEXT) || '日' ELSE '' END AS delayLabel
FROM joined
{having}
ORDER BY shiireCode, denDay, denNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
