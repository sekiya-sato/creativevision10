using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 支払残高明細書。仕入先へ渡す支払明細書を、支払ヘッダ（前回残高・当月仕入・当月支払・当月残高）と
/// 対象期間の仕入／支払明細で構成して印字する。請求書印刷の仕入側の対応帳票。
///
/// 支払ヘッダは集計テーブル SummaryKaiShi（支払計算＝月次更新処理の成果物）を読む。
/// 対象期間は同テーブルの DayFrom〜DayTo。締め処理を回していない支払日は行が無く空になる。
/// 前回残高は当月残高から当月増減を戻して算出する（Balance - TotalShiire + TotalOut）。
///
/// 明細1行=CSV1行で、ヘッダ項目は各行に同じ値を繰り返す。qfm 側でヘッダ領域と明細領域に
/// 振り分ける前提。
/// </summary>
public partial class ShiharaiBalanceDetailViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "支払残高明細書";
	protected override string FormFileName => "ShiharaiBalanceDetail.qfm";

	[ObservableProperty]
	public partial string PayDay { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>true=支払額または残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	/// <summary>true=支払明細も印字 / false=仕入明細のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeShiharai { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(PayDay, out var day)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var payDay = AddSqlParameter(parameters, ToDenDay(day));
		var shiireWhere = BuildCodeRangeWhere(parameters, "s.Code", ShiireCodeFrom, ShiireCodeTo);

		const string ShiireKingaku = "CASE WHEN v.Total != 0 THEN v.Total ELSE v.KingakuTotal + v.Tax END";
		var activeOnly = IsActiveOnly ? "AND (k.TotalShiire != 0 OR k.Balance != 0)" : "";
		var kubunLabel = TranMeisaiSql.KubunLabel("v.Kubun",
			((int)EnumShiire.Shiire, "仕入"), ((int)EnumShiire.Henpin, "仕入返品"),
			((int)EnumShiire.Nebiki, "値引"), ((int)EnumShiire.Other, "その他"));

		var shiharaiPart = IncludeShiharai ? @"
    UNION ALL
    SELECT
        h.Id_Shiire AS idShiire, p.KakeDay AS denDay, 2 AS srcOrder, p.Id AS denNo,
        '支払' AS kubunText, 0 AS su, -p.KingakuTotal AS kingaku
    FROM headers h
    JOIN Tran07Shiharai p
      ON p.Id_Torisaki = h.Id_Shiire
     AND p.KakeDay >= h.dayFrom AND p.KakeDay <= h.dayTo" : "";

		var sql = $@"
WITH headers AS (
    SELECT
        k.Id_Shiire AS Id_Shiire,
        s.Code AS shiireCode, s.Name AS shiireName,
        k.DenDay AS payDay, k.DayFrom AS dayFrom, k.DayTo AS dayTo,
        k.Balance - k.TotalShiire + k.TotalOut AS prevBalance,
        k.TotalShiire AS totalShiire,
        k.TotalOut    AS totalOut,
        k.Tax         AS tax,
        k.Balance     AS balance
    FROM SummaryKaiShi k
    JOIN MasterShiire s ON s.Id = k.Id_Shiire
    WHERE k.DenDay = {payDay}
      {activeOnly}{shiireWhere}
),
details AS (
    SELECT
        h.Id_Shiire AS idShiire, v.KakeDay AS denDay, 1 AS srcOrder, v.Id AS denNo,
        {kubunLabel} AS kubunText, v.SuTotal AS su, {ShiireKingaku} AS kingaku
    FROM headers h
    JOIN Tran03Shiire v
      ON v.Id_Shiire = h.Id_Shiire
     AND v.KakeDay >= h.dayFrom AND v.KakeDay <= h.dayTo{shiharaiPart}
)
SELECT
    {TranMeisaiSql.DateLabel("h.payDay")} AS payDayLabel,
    h.shiireCode, h.shiireName,
    {TranMeisaiSql.DateLabel("h.dayFrom")} || '～' || {TranMeisaiSql.DateLabel("h.dayTo")} AS termLabel,
    h.prevBalance, h.totalShiire, h.totalOut, h.tax, h.balance,
    {TranMeisaiSql.DateLabel("d.denDay")} AS denDayLabel,
    CAST(d.denNo AS TEXT) AS denNoText,
    d.kubunText,
    d.su,
    d.kingaku
FROM headers h
LEFT JOIN details d ON d.idShiire = h.Id_Shiire
ORDER BY h.shiireCode, d.denDay, d.srcOrder, d.denNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
