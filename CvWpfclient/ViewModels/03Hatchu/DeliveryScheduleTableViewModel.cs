using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 納品予定表（発注・PDF帳票）。発注(<see cref="Tran13Hachu"/>)を納品予定日(<see cref="Tran13Hachu.NouhinDay"/>)→
/// 仕入先の順に並べ、発注明細（商品・色・サイズ・単価）と仕入先計・納品日計を印刷する。
/// 画面照会版は「納品予定照会」(<see cref="DeliveryScheduleInquiryViewModel"/>)。
/// <para>
/// 納品予定日は 2026-08-18 に伝票ヘッダへ追加した（決定 6.2 / H1）。納期遅れ = 納品予定日を過ぎても未完了(<c>EndFlag=0</c>)。
/// 入荷数は仕入(<see cref="Tran03Shiire"/>) の <c>RelateNo1</c>=発注伝票Id 紐付け合計（発注残管理表と同じ規約）。
/// 入荷済・残数・納期遅れ・メモは発注伝票単位の値であり明細(色・サイズ)単位には分解できないため、
/// 各発注の先頭明細行にだけ表示する（qfm側の集計(sum)が明細行ごとに二重加算しないようにするため）。
/// 納品日・仕入先の見出し表示と仕入先計・納品日計の集計は qfm 側（group/suppress・calctype=sum）で行うため、
/// SQLは「ヘッダ値を明細行へ乗せたままの単純な平坦行」を返すだけでよい。
/// 仕様は `Doc/spec/archive/2026-08-18_H1-H4_納品予定日_詳細設計.md` の follow-up と
/// `Doc/spec/2026-08-20_納品予定表(発注)_qfmレイアウト変更_詳細設計.md` を参照する。
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
		// 納品予定日が入っている発注だけを対象にする。取引区分は発注/追加発注/自動発注をまとめて対象にする。
		var clauses = new List<string> {
			"ifnull(h.NouhinDay,'') <> ''",
			$"h.Kubun IN ({(int)EnumHachu.Hachu},{(int)EnumHachu.Tsuika},{(int)EnumHachu.Jido})",
		};
		if (from.HasValue) clauses.Add($"h.NouhinDay >= {AddSqlParameter(parameters, ToDenDay(from.Value))}");
		if (to.HasValue) clauses.Add($"h.NouhinDay <= {AddSqlParameter(parameters, ToDenDay(to.Value))}");
		if (IncompleteOnly || OverdueOnly) clauses.Add("h.EndFlag = 0");
		if (OverdueOnly) clauses.Add($"h.NouhinDay < {AddSqlParameter(parameters, todayYmd)}");
		var where = string.Join(" AND ", clauses)
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);

		var kubunLabel = "CAST(m.kubun AS TEXT) || ' ' || " + TranMeisaiSql.KubunLabel("m.kubun",
			((int)EnumHachu.Hachu, "発注"), ((int)EnumHachu.Tsuika, "追加発注"), ((int)EnumHachu.Jido, "自動発注"),
			((int)EnumHachu.Henpin, "発注返品"), ((int)EnumHachu.Nebiki, "値引"), ((int)EnumHachu.Other, "その他"));

		// SELECT の列順は DeliveryScheduleTable.qfm の item1..item16 と一致させる。
		// 納品日・仕入先は明細行にも毎回同じ値を乗せる（見出し表示の抑制・仕入先計/納品日計の集計は qfm 側で行う）。
		var sql = $@"
WITH hachu AS (
    SELECT
        h.Id        AS denNo,
        h.DenDay    AS denDay,
        h.NouhinDay AS nouhinDay,
        h.Kubun     AS kubun,
        h.EndFlag   AS endFlag,
        h.SuTotal   AS hachuSu,
        ifnull(h.Memo,'') AS memo,
        {TranMeisaiSql.HeaderCode("VShiire")}         AS shiireCode,
        {CodeNameDisplay.SqlFromVColumn("h.VShiire")} AS shiireDisplay,
        h.Jmeisai   AS jmeisai
    FROM Tran13Hachu h
    WHERE {where}
),
nyuka AS (
    SELECT RelateNo1 AS denNo, SUM(SuTotal) AS nyukaSu
    FROM Tran03Shiire
    WHERE RelateNo1 > 0
    GROUP BY RelateNo1
),
-- 発注1件(denNo)単位の入荷済・残数・納期遅れ・メモ。明細(色・サイズ)には分解しない値。
hachuAgg AS (
    SELECT
        h.denNo, h.memo,
        ifnull(n.nyukaSu, 0)             AS nyukaSu,
        h.hachuSu - ifnull(n.nyukaSu, 0) AS zanSu,
        CASE WHEN h.endFlag = 0
             AND julianday('{today}') > julianday(substr(h.nouhinDay,1,4)||'-'||substr(h.nouhinDay,5,2)||'-'||substr(h.nouhinDay,7,2))
             THEN CAST(julianday('{today}')
                  - julianday(substr(h.nouhinDay,1,4)||'-'||substr(h.nouhinDay,5,2)||'-'||substr(h.nouhinDay,7,2))
                  AS INTEGER)
             ELSE 0 END AS delayDays
    FROM hachu h
    LEFT JOIN nyuka n ON n.denNo = h.denNo
),
meisai AS (
    SELECT
        h.denNo, h.denDay, h.nouhinDay, h.kubun, h.shiireCode, h.shiireDisplay,
        cast(json_extract(m.value,'$.No') as int) AS meisaiNo,
        ROW_NUMBER() OVER (PARTITION BY h.denNo ORDER BY cast(json_extract(m.value,'$.No') as int)) AS lineSeq,
        ifnull(json_extract(m.value,'$.Code_Shohin'),'')    AS shohinCode,
        ifnull(json_extract(m.value,'$.Mei_Shohin'),'')     AS shohinName,
        ifnull(json_extract(m.value,'$.Mei_Col'),'')        AS colorName,
        ifnull(json_extract(m.value,'$.Mei_Siz'),'')        AS sizeName,
        cast(ifnull(json_extract(m.value,'$.Su'),0)      as int) AS su,
        cast(ifnull(json_extract(m.value,'$.Tanka'),0)   as int) AS tanka,
        cast(ifnull(json_extract(m.value,'$.Kingaku'),0) as int) AS kingaku
    FROM hachu h, json_each(h.jmeisai) m
    WHERE json_valid(h.jmeisai)
)
SELECT
    m.nouhinDay, m.shiireDisplay,
    m.denDay, CAST(m.denNo AS TEXT), {kubunLabel},
    m.shohinCode, m.shohinName, m.colorName, m.sizeName,
    m.su, m.tanka, m.kingaku,
    CASE WHEN m.lineSeq = 1 THEN a.nyukaSu ELSE NULL END,
    CASE WHEN m.lineSeq = 1 THEN a.zanSu ELSE NULL END,
    CASE WHEN m.lineSeq = 1 AND a.delayDays > 0 THEN CAST(a.delayDays AS TEXT) || '日超過' ELSE '' END,
    CASE WHEN m.lineSeq = 1 THEN a.memo ELSE '' END
FROM meisai m
JOIN hachuAgg a ON a.denNo = m.denNo
ORDER BY m.nouhinDay, m.shiireCode, m.denDay, m.denNo, m.meisaiNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
