using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 仕入先元帳。仕入先ごとに「繰越残高 → 期間内の仕入・支払の明細 → 差引残高」を日付順に印字する。
///
/// 残高は集計テーブル(SummaryKaiKake)ではなく伝票から直接計算する。
/// 集計テーブルは支払計算(月次更新処理)が作るもので、締め処理を回していない期間では空になるため、
/// 元帳としては伝票を積み上げた値の方が常に正しい。
/// 繰越残高 = 期間開始日より前の (仕入 - 支払) の累計。
/// </summary>
public partial class ShiireLedgerViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "仕入先元帳";
	protected override string FormFileName => "ShiireLedger.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=期間内に動きがある仕入先のみ / false=繰越残高だけの仕入先も出す。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

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
		var shiireWhere = BuildCodeRangeWhere(parameters, "Code", ShiireCodeFrom, ShiireCodeTo);

		// 仕入の買掛計上額は総合計(Total)。未計算の伝票が混ざる場合は 明細金額+消費税 で代替する。
		const string ShiireKingaku = "CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END";
		var kubunLabel = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumShiire.Shiire, "仕入"), ((int)EnumShiire.Henpin, "仕入返品"),
			((int)EnumShiire.Nebiki, "値引"), ((int)EnumShiire.Other, "その他"));

		var activeOnly = IsActiveOnly ? "WHERE moveCount > 0" : "";

		var sql = $@"
WITH shiire AS (
    SELECT Id, Code, Name FROM MasterShiire
    WHERE 1=1 {shiireWhere}
),
-- 期間開始前の累計＝繰越残高
carry AS (
    SELECT idTori, SUM(kari) - SUM(kashi) AS balance
    FROM (
        SELECT h.Id_Shiire AS idTori, {ShiireKingaku} AS kari, 0 AS kashi
        FROM Tran03Shiire h WHERE h.DenDay < {dayFrom}
        UNION ALL
        SELECT h.Id_Torisaki AS idTori, 0 AS kari, h.KingakuTotal AS kashi
        FROM Tran07Shiharai h WHERE h.DenDay < {dayFrom}
    )
    GROUP BY idTori
),
-- 期間内の動き
moves AS (
    SELECT
        h.Id_Shiire AS idTori, h.DenDay AS denDay, h.Id AS denNo,
        1 AS srcOrder, {kubunLabel} AS kubunText,
        h.SuTotal AS su, {ShiireKingaku} AS kari, 0 AS kashi,
        ifnull(h.ManualNo,'') AS manualNo, ifnull(h.Memo,'') AS memo
    FROM Tran03Shiire h
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    UNION ALL
    SELECT
        h.Id_Torisaki AS idTori, h.DenDay AS denDay, h.Id AS denNo,
        2 AS srcOrder, '支払' AS kubunText,
        0 AS su, 0 AS kari, h.KingakuTotal AS kashi,
        ifnull(h.ManualNo,'') AS manualNo, ifnull(h.Memo,'') AS memo
    FROM Tran07Shiharai h
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
),
targets AS (
    SELECT
        s.Id, s.Code, s.Name,
        ifnull(c.balance, 0) AS carryBalance,
        (SELECT COUNT(*) FROM moves m WHERE m.idTori = s.Id) AS moveCount
    FROM shiire s
    LEFT JOIN carry c ON c.idTori = s.Id
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
