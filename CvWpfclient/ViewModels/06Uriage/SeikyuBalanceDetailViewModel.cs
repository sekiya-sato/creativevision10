using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 請求書印刷。得意先へ渡す請求書を、請求ヘッダ（前回残高・当月売上・当月入金・当月請求額）と
/// 対象期間の売上／入金明細で構成して印字する。
///
/// 請求ヘッダは集計テーブル SummaryUriSei（請求計算＝月次更新処理の成果物）を読む。
/// 対象期間は同テーブルの DayFrom〜DayTo。締め処理を回していない請求日は行が無く空になる。
/// 前回残高は当月残高から当月増減を戻して算出する（Balance + TotalSales - TotalIn）。
/// SummaryUriSei の当月残高は Balance = 前回残高 + TotalIn - TotalSales で作られるため、
/// 逆算は TotalSales を足し TotalIn を引く。符号を逆にすると当月増減を2回効かせてしまう。
///
/// 明細1行=CSV1行で、ヘッダ項目は各行に同じ値を繰り返す。qfm 側でヘッダ領域と明細領域に
/// 振り分ける前提（CSV入力のフォームで単票を作る際の定石）。
/// </summary>
public partial class SeikyuBalanceDetailViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "請求書印刷";
	protected override string FormFileName => "SeikyuBalanceDetail.qfm";

	[ObservableProperty]
	public partial string SeikyuDay { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>true=請求額または残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	/// <summary>true=入金明細も印字 / false=売上明細のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeNyukin { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(SeikyuDay, out var day)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var seikyuDay = AddSqlParameter(parameters, ToDenDay(day));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "t.Code", TokuiCodeFrom, TokuiCodeTo);

		const string UriageKingaku = "CASE WHEN u.Total != 0 THEN u.Total ELSE u.KingakuTotal + u.Tax END";
		var activeOnly = IsActiveOnly ? "AND (s.TotalSales != 0 OR s.Balance != 0)" : "";
		var kubunLabel = TranMeisaiSql.KubunLabel("u.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));

		// 入金明細を含めない場合は売上側だけを UNION 対象にする
		var nyukinPart = IncludeNyukin ? $@"
    UNION ALL
    SELECT
        h.Id_Tokui AS idTokui, n.KakeDay AS denDay, 2 AS srcOrder, n.Id AS denNo,
        '入金' AS kubunText, 0 AS su, -n.KingakuTotal AS kingaku
    FROM headers h
    JOIN Tran06Nyukin n
      ON n.Id_Torisaki = h.Id_Tokui
     AND n.KakeDay >= h.dayFrom AND n.KakeDay <= h.dayTo" : "";

		var sql = $@"
WITH headers AS (
    SELECT
        s.Id_Tokui AS Id_Tokui,
        t.Code AS tokuiCode, t.Name AS tokuiName,
        s.DenDay AS seikyuDay, s.DayFrom AS dayFrom, s.DayTo AS dayTo,
        s.Balance + s.TotalSales - s.TotalIn AS prevBalance,
        s.TotalSales AS totalSales,
        s.TotalIn    AS totalIn,
        s.Tax        AS tax,
        s.Balance    AS balance,
        s.SeikyuNo   AS seikyuNo
    FROM SummaryUriSei s
    JOIN MasterTokui t ON t.Id = s.Id_Tokui
    WHERE s.DenDay = {seikyuDay}
      {activeOnly}{tokuiWhere}
),
details AS (
    SELECT
        h.Id_Tokui AS idTokui, u.KakeDay AS denDay, 1 AS srcOrder, u.Id AS denNo,
        {kubunLabel} AS kubunText, u.SuTotal AS su, {UriageKingaku} AS kingaku
    FROM headers h
    JOIN Tran00Uriage u
      ON u.Id_Tokui = h.Id_Tokui
     AND u.KakeDay >= h.dayFrom AND u.KakeDay <= h.dayTo{nyukinPart}
)
SELECT
    {TranMeisaiSql.DateLabel("h.seikyuDay")} AS seikyuDayLabel,
    h.tokuiCode, h.tokuiName,
    {TranMeisaiSql.DateLabel("h.dayFrom")} || '～' || {TranMeisaiSql.DateLabel("h.dayTo")} AS termLabel,
    h.prevBalance, h.totalSales, h.totalIn, h.tax, h.balance,
    {TranMeisaiSql.DateLabel("d.denDay")} AS denDayLabel,
    CAST(d.denNo AS TEXT) AS denNoText,
    d.kubunText,
    d.su,
    d.kingaku,
    h.seikyuNo
FROM headers h
LEFT JOIN details d ON d.idTokui = h.Id_Tokui
ORDER BY h.tokuiCode, d.denDay, d.srcOrder, d.denNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
