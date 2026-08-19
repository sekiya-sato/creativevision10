using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 納品予定表（発注・PDF帳票）。発注(<see cref="Tran13Hachu"/>)を納品予定日(<see cref="Tran13Hachu.NouhinDay"/>)順に
/// 並べ、入荷予定と納期遅れを印刷する。画面照会版は「納品予定照会」(<see cref="DeliveryScheduleInquiryViewModel"/>)。
/// <para>
/// 納品予定日は 2026-08-18 に伝票ヘッダへ追加した（決定 6.2 / H1）。納期遅れ = 納品予定日を過ぎても未完了(<c>EndFlag=0</c>)。
/// 入荷数は仕入(<see cref="Tran03Shiire"/>) の <c>RelateNo1</c>=発注伝票Id 紐付け合計（発注残管理表と同じ規約）。
/// 仕様は `Doc/spec/2026-08-18_H1-H4_納品予定日_詳細設計.md` の follow-up を参照する。
/// </para>
/// </summary>
public partial class DeliveryScheduleTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "納品予定表（発注）";
	protected override string FormFileName => "DeliveryScheduleTable.qfm";

	[ObservableProperty]
	public partial string NouhinDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string NouhinDayTo { get; set; } = DateTime.Today.AddMonths(2).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>納期遅れ（予定日超過かつ未完了）だけに絞る。</summary>
	[ObservableProperty]
	public partial bool OverdueOnly { get; set; }

	/// <summary>未完了だけに絞る（既定 true）。</summary>
	[ObservableProperty]
	public partial bool IncompleteOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		DateTime? from = null, to = null;
		if (!string.IsNullOrWhiteSpace(NouhinDayFrom)) {
			if (!TryParseDate(NouhinDayFrom, out var f)) return Task.FromResult<QueryListSqlParam?>(null);
			from = f;
		}
		if (!string.IsNullOrWhiteSpace(NouhinDayTo)) {
			if (!TryParseDate(NouhinDayTo, out var t)) return Task.FromResult<QueryListSqlParam?>(null);
			to = t;
		}
		if (from.HasValue && to.HasValue && from > to) {
			MessageEx.ShowWarningDialog("納品予定日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var today = DateTime.Today.ToString("yyyy-MM-dd");
		var todayYmd = ToDenDay(DateTime.Today);

		List<string> parameters = [];
		// 納品予定日が入っている発注だけを対象にする
		var clauses = new List<string> { "ifnull(h.NouhinDay,'') <> ''", "h.Kubun = 10" };
		if (from.HasValue) clauses.Add($"h.NouhinDay >= {AddSqlParameter(parameters, ToDenDay(from.Value))}");
		if (to.HasValue) clauses.Add($"h.NouhinDay <= {AddSqlParameter(parameters, ToDenDay(to.Value))}");
		if (IncompleteOnly || OverdueOnly) clauses.Add("h.EndFlag = 0");
		if (OverdueOnly) clauses.Add($"h.NouhinDay < {AddSqlParameter(parameters, todayYmd)}");
		var where = string.Join(" AND ", clauses)
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);

		// SELECT の列順は DeliveryScheduleTable.qfm の item1..item9 と一致させる。
		var sql = $@"
WITH hachu AS (
    SELECT
        h.Id        AS denNo,
        h.DenDay    AS denDay,
        h.NouhinDay AS nouhinDay,
        h.EndFlag   AS endFlag,
        {TranMeisaiSql.HeaderCode("VShiire")} AS shiireCode,
        {TranMeisaiSql.HeaderName("VShiire")} AS shiireName,
        h.SuTotal   AS hachuSu
    FROM Tran13Hachu h
    WHERE {where}
),
nyuka AS (
    SELECT RelateNo1 AS denNo, SUM(SuTotal) AS nyukaSu
    FROM Tran03Shiire
    WHERE RelateNo1 > 0
    GROUP BY RelateNo1
),
joined AS (
    SELECT
        a.denNo, a.denDay, a.nouhinDay, a.shiireCode, a.shiireName, a.hachuSu,
        ifnull(n.nyukaSu, 0)             AS nyukaSu,
        a.hachuSu - ifnull(n.nyukaSu, 0) AS zanSu,
        CASE WHEN a.endFlag = 0
             AND julianday('{today}') > julianday(substr(a.nouhinDay,1,4) || '-' || substr(a.nouhinDay,5,2) || '-' || substr(a.nouhinDay,7,2))
             THEN CAST(julianday('{today}')
                  - julianday(substr(a.nouhinDay,1,4) || '-' || substr(a.nouhinDay,5,2) || '-' || substr(a.nouhinDay,7,2))
                  AS INTEGER)
             ELSE 0 END                  AS delayDays
    FROM hachu a
    LEFT JOIN nyuka n ON n.denNo = a.denNo
)
SELECT
    {TranMeisaiSql.DateLabel("nouhinDay")} AS nouhinDayLabel,
    shiireCode, shiireName,
    {TranMeisaiSql.DateLabel("denDay")} AS denDayLabel,
    CAST(denNo AS TEXT) AS denNoText,
    hachuSu, nyukaSu, zanSu,
    CASE WHEN delayDays > 0 THEN CAST(delayDays AS TEXT) || '日超過' ELSE '' END AS delayLabel
FROM joined
ORDER BY nouhinDay, denNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
