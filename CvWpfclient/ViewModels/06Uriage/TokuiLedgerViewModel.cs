using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 得意先元帳。得意先ごとに「繰越残高 → 期間内の売上・入金の明細 → 差引残高」を日付順に印字する。
///
/// 残高は集計テーブル(SummaryUriKake)ではなく伝票から直接計算する。
/// 集計テーブルは請求計算(月次更新処理)が作るもので、締め処理を回していない期間では空になるため、
/// 元帳としては伝票を積み上げた値の方が常に正しい。
/// 繰越残高 = 期間開始日より前の (売上 - 入金) の累計。
///
/// 対象は卸売上(Tran00Uriage)。店舗売上(Tran01Tenuri)は掛売ではなく店頭現金売上なので含めない。
///
/// 期間は売上・入金とも掛計上日(KakeDay)で切る。売掛集計(SummaryUriKake)と同じ軸にするためで、
/// 2026-08-16 に売上側を DenDay から、入金側を改名前の DenDay から切り替えた。
/// 画面の条件プロパティ名 DenDayFrom / DenDayTo は互換のため据え置いており、指す値は掛計上日である。
///
/// 消込済(EndFlag=1)の伝票はメモ欄の先頭へ `*` を出す。帳票定義(TokuiLedger.qfm)は変更していない。
/// </summary>
public partial class TokuiLedgerViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "得意先元帳";
	protected override string FormFileName => "TokuiLedger.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=期間内に動きがある得意先のみ / false=繰越残高だけの得意先も出す。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "Code", TokuiCodeFrom, TokuiCodeTo);

		// 売上の売掛計上額は総合計(Total)。未計算の伝票が混ざる場合は 明細金額+消費税 で代替する。
		const string UriageKingaku = "CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END";
		var kubunLabel = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));

		// 消込済(EndFlag=1)の売上伝票はメモ欄の先頭へ `*` を出す。qfm には列を追加しない。
		var memoWithMark = TranMeisaiSql.MemoWithKesikomiMark("h.EndFlag", "h.Memo");

		var activeOnly = IsActiveOnly ? "WHERE moveCount > 0" : "";

		var sql = $@"
WITH tokui AS (
    SELECT Id, Code, Name FROM MasterTokui
    WHERE TenType = 1 {tokuiWhere}
),
-- 期間開始前の累計＝繰越残高
carry AS (
    SELECT idTori, SUM(kari) - SUM(kashi) AS balance
    FROM (
        SELECT h.Id_Tokui AS idTori, {UriageKingaku} AS kari, 0 AS kashi
        FROM Tran00Uriage h WHERE h.KakeDay < {dayFrom}
        UNION ALL
        SELECT h.Id_Torisaki AS idTori, 0 AS kari, h.KingakuTotal AS kashi
        FROM Tran06Nyukin h WHERE h.KakeDay < {dayFrom}
    )
    GROUP BY idTori
),
-- 期間内の動き
moves AS (
    SELECT
        h.Id_Tokui AS idTori, h.KakeDay AS denDay, h.Id AS denNo,
        1 AS srcOrder, {kubunLabel} AS kubunText,
        h.SuTotal AS su, {UriageKingaku} AS kari, 0 AS kashi,
        ifnull(h.ManualNo,'') AS manualNo, {memoWithMark} AS memo
    FROM Tran00Uriage h
    WHERE h.KakeDay >= {dayFrom} AND h.KakeDay <= {dayTo}
    UNION ALL
    SELECT
        h.Id_Torisaki AS idTori, h.KakeDay AS denDay, h.Id AS denNo,
        2 AS srcOrder, '入金' AS kubunText,
        0 AS su, 0 AS kari, h.KingakuTotal AS kashi,
        ifnull(h.ManualNo,'') AS manualNo, ifnull(h.Memo,'') AS memo
    FROM Tran06Nyukin h
    WHERE h.KakeDay >= {dayFrom} AND h.KakeDay <= {dayTo}
),
targets AS (
    SELECT
        t.Id, t.Code, t.Name,
        ifnull(c.balance, 0) AS carryBalance,
        (SELECT COUNT(*) FROM moves m WHERE m.idTori = t.Id) AS moveCount
    FROM tokui t
    LEFT JOIN carry c ON c.idTori = t.Id
    {activeOnly}
),
-- 繰越行(rowOrder=0)＋明細行(rowOrder=1)を1本にまとめ、残高を累積する
rows_all AS (
    SELECT
        t.Code AS toriCode, t.Name AS toriName,
        0 AS rowOrder, '' AS denDaySort, 0 AS srcOrder, 0 AS denNoSort,
        '' AS denDayLabel, '' AS denNoText, '繰越' AS kubunText,
        0 AS su, 0 AS kari, 0 AS kashi,
        t.carryBalance AS delta,
        '' AS manualNo, '' AS memo
    FROM targets t
    UNION ALL
    SELECT
        t.Code, t.Name,
        1 AS rowOrder, m.denDay AS denDaySort, m.srcOrder, m.denNo AS denNoSort,
        {TranMeisaiSql.DateLabel("m.denDay")} AS denDayLabel,
        CAST(m.denNo AS TEXT) AS denNoText, m.kubunText,
        m.su, m.kari, m.kashi,
        m.kari - m.kashi AS delta,
        m.manualNo, m.memo
    FROM targets t
    JOIN moves m ON m.idTori = t.Id
)
SELECT
    toriCode, toriName,
    denDayLabel, denNoText, kubunText,
    su, kari, kashi,
    SUM(delta) OVER (PARTITION BY toriCode ORDER BY rowOrder, denDaySort, srcOrder, denNoSort
                     ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance,
    manualNo, memo
FROM rows_all
ORDER BY toriCode, rowOrder, denDaySort, srcOrder, denNoSort";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
