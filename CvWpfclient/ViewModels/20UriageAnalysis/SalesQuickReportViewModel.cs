using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 売上速報。指定日の全店売上を、当日・月初からの累計・予算・予算比・前年同日比で1枚にまとめる。
/// 朝礼や日次報告のために「今どうなっているか」を素早く見るための帳票。
///
/// 累計は「指定日の月初から指定日まで」。前年同日は指定日の1年前の同月同日（日付対比）。
/// 曜日対比が必要な場合は店舗予算表(02Yosan)の前年比切替を使う。
/// </summary>
public partial class SalesQuickReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "売上速報";
	protected override string FormFileName => "SalesQuickReport.qfm";

	/// <summary>
	/// 粗利に関わる列を出すか。店舗向けの「原価無」派生(40Shop)が false で上書きする。
	/// 粗利は伝票ヘッダの「明細金額合計 − 下代合計」で求める（明細を展開しないので速い）。
	/// 列数が変わるため派生側は専用の qfm を持つ。
	/// </summary>
	protected virtual bool ShowCost => true;

	[ObservableProperty]
	public partial string TargetDay { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=当日または累計に売上がある店舗のみ / false=直営店全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShopCodeFrom() => ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;

	[RelayCommand]
	void SelectShopCodeTo() => ShopCodeTo = SelectShopCode() ?? ShopCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(TargetDay, out var day)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var monthStart = new DateTime(day.Year, day.Month, 1);
		var prevDay = day.AddYears(-1);
		var prevMonthStart = new DateTime(prevDay.Year, prevDay.Month, 1);

		List<string> parameters = [];
		var target = AddSqlParameter(parameters, ToDenDay(day));
		var mStart = AddSqlParameter(parameters, ToDenDay(monthStart));
		var prevTarget = AddSqlParameter(parameters, ToDenDay(prevDay));
		var prevMStart = AddSqlParameter(parameters, ToDenDay(prevMonthStart));
		var shopWhere = BuildCodeRangeWhere(parameters, "t.Code", ShopCodeFrom, ShopCodeTo);

		var having = IsActiveOnly ? "WHERE dayKingaku != 0 OR cumKingaku != 0" : "";
		var arariCols = ShowCost ? @"
    dayArari,
    CASE WHEN dayKingaku != 0 THEN ROUND(CAST(dayArari AS REAL) / dayKingaku * 100, 1) ELSE 0 END AS dayArariRatio," : "";

		var sql = $@"
WITH shops AS (
    SELECT t.Id, t.Code, t.Name FROM MasterTokui t
    WHERE t.TenType = 6 {shopWhere}
),
-- 当日 / 月累計 / 前年同日 / 前年同月累計 を1パスで集計する
sales AS (
    SELECT
        h.Id_Tenpo AS idTenpo,
        SUM(CASE WHEN h.DenDay = {target} THEN 1 ELSE 0 END)                          AS dayCount,
        SUM(CASE WHEN h.DenDay = {target} THEN h.SuTotal ELSE 0 END)                  AS daySu,
        SUM(CASE WHEN h.DenDay = {target} THEN h.KingakuTotal ELSE 0 END)             AS dayKingaku,
        SUM(CASE WHEN h.DenDay = {target} THEN h.KingakuTotal - h.GedaiTotal ELSE 0 END) AS dayArari,
        SUM(CASE WHEN h.DenDay BETWEEN {mStart} AND {target} THEN h.KingakuTotal ELSE 0 END)          AS cumKingaku,
        SUM(CASE WHEN h.DenDay = {prevTarget} THEN h.KingakuTotal ELSE 0 END)         AS prevDayKingaku,
        SUM(CASE WHEN h.DenDay BETWEEN {prevMStart} AND {prevTarget} THEN h.KingakuTotal ELSE 0 END)  AS prevCumKingaku
    FROM Tran01Tenuri h
    GROUP BY h.Id_Tenpo
),
budget AS (
    SELECT Id_Tenpo AS idTenpo,
        SUM(CASE WHEN DenDay = {target} THEN UriYosan ELSE 0 END)                     AS dayYosan,
        SUM(CASE WHEN DenDay BETWEEN {mStart} AND {target} THEN UriYosan ELSE 0 END)   AS cumYosan
    FROM MasterYosanBrand
    GROUP BY Id_Tenpo
),
joined AS (
    SELECT
        s.Code AS shopCode, s.Name AS shopName,
        ifnull(sa.dayCount, 0)       AS dayCount,
        ifnull(sa.daySu, 0)          AS daySu,
        ifnull(sa.dayKingaku, 0)     AS dayKingaku,
        ifnull(sa.dayArari, 0)       AS dayArari,
        ifnull(b.dayYosan, 0)        AS dayYosan,
        ifnull(sa.cumKingaku, 0)     AS cumKingaku,
        ifnull(b.cumYosan, 0)        AS cumYosan,
        ifnull(sa.prevDayKingaku, 0) AS prevDayKingaku,
        ifnull(sa.prevCumKingaku, 0) AS prevCumKingaku
    FROM shops s
    LEFT JOIN sales sa ON sa.idTenpo = s.Id
    LEFT JOIN budget b ON b.idTenpo = s.Id
)
SELECT
    shopCode, shopName,
    dayCount, daySu, dayKingaku,{arariCols} dayYosan,
    CASE WHEN dayYosan != 0 THEN ROUND(CAST(dayKingaku AS REAL) / dayYosan * 100, 1) ELSE 0 END AS dayYosanRatio,
    cumKingaku, cumYosan,
    CASE WHEN cumYosan != 0 THEN ROUND(CAST(cumKingaku AS REAL) / cumYosan * 100, 1) ELSE 0 END AS cumYosanRatio,
    CASE WHEN prevCumKingaku != 0 THEN ROUND(CAST(cumKingaku AS REAL) / prevCumKingaku * 100, 1) ELSE 0 END AS prevRatio
FROM joined
{having}
ORDER BY shopCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
