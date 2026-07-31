using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 受注残管理表。受注伝票単位に「受注数・売上済数・残数・残金額・受注からの経過日数」を出して、
/// 長期化している受注残を管理する。発注残管理表の受注側版。
///
/// 経過日数の基準日はクライアントの日付を埋め込む（サーバとのタイムゾーン差で日数がずれないため）。
///
/// 【前提】売上の紐付けは Tran00Uriage.RelateNo1（関連伝票NO）に受注伝票の Id が入っている前提。
/// 発注残管理表・移動未受リストと同じ紐付け規約。
/// 受注からの売上引き当てUIは未整備なので、実装する際はこの前提を満たすこと。
/// </summary>
public partial class JuchuZanKanriTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "受注残管理表";
	protected override string FormFileName => "JuchuZanKanriTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddDays(-180).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>滞留日数の下限（この日数以上経過した受注残のみ出す）。空欄なら絞らない。</summary>
	[ObservableProperty]
	public partial string MinElapsedDaysText { get; set; } = string.Empty;

	/// <summary>出力対象。true=残がある伝票のみ / false=完了分も含む。</summary>
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
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo)
			+ " AND h.Kubun = 10";

		var today = DateTime.Today.ToString("yyyy-MM-dd");

		var conditions = new List<string>();
		if (IsPendingOnly) conditions.Add("zanSu != 0");
		if (minDays > 0) conditions.Add($"elapsedDays >= {minDays}");
		var having = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

		var sql = $@"
WITH juchu AS (
    SELECT
        h.Id     AS denNo,
        h.DenDay AS denDay,
        {TranMeisaiSql.HeaderCode("VTokui")} AS tokuiCode,
        {TranMeisaiSql.HeaderName("VTokui")} AS tokuiName,
        h.SuTotal      AS juchuSu,
        h.KingakuTotal AS juchuKingaku
    FROM Tran12Jyuchu h
    WHERE {where}
),
uriage AS (
    SELECT RelateNo1 AS denNo, SUM(SuTotal) AS uriageSu, MAX(DenDay) AS lastUriageDay
    FROM Tran00Uriage
    WHERE RelateNo1 > 0
    GROUP BY RelateNo1
),
joined AS (
    SELECT
        a.denNo, a.denDay, a.tokuiCode, a.tokuiName,
        a.juchuSu, a.juchuKingaku,
        ifnull(u.uriageSu, 0)             AS uriageSu,
        a.juchuSu - ifnull(u.uriageSu, 0) AS zanSu,
        u.lastUriageDay                   AS lastUriageDay,
        CASE WHEN a.juchuSu != 0
             THEN CAST(ROUND(CAST(a.juchuKingaku AS REAL) * (a.juchuSu - ifnull(u.uriageSu,0)) / a.juchuSu) AS INTEGER)
             ELSE 0 END                   AS zanKingaku,
        CAST(julianday('{today}')
             - julianday(substr(a.denDay,1,4) || '-' || substr(a.denDay,5,2) || '-' || substr(a.denDay,7,2))
             AS INTEGER)                  AS elapsedDays
    FROM juchu a
    LEFT JOIN uriage u ON u.denNo = a.denNo
)
SELECT
    tokuiCode, tokuiName,
    {TranMeisaiSql.DateLabel("denDay")} AS denDayLabel,
    CAST(denNo AS TEXT) AS denNoText,
    juchuSu, uriageSu, zanSu, zanKingaku,
    elapsedDays,
    CASE
        WHEN elapsedDays >= 90 THEN '90日以上'
        WHEN elapsedDays >= 60 THEN '60日以上'
        WHEN elapsedDays >= 30 THEN '30日以上'
        ELSE '30日未満'
    END AS elapsedLabel,
    {TranMeisaiSql.DateLabel("ifnull(lastUriageDay,'')")} AS lastUriageDayLabel
FROM joined
{having}
ORDER BY tokuiCode, denDay, denNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
