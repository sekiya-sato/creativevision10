using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 納品書未発行チェックリスト。納品書を発行していない卸売上伝票を一覧で印字し、発行漏れを検出する。
///
/// 判定は Tran00Uriage.IsPrint（0=未発行 / 1=発行済）。このフラグは納品書印刷画面の
/// 「発行済みにする」で立てる。列自体は Phase 3f で追加した（UpdateDb 26_07_31_02）。
/// 伝票単位の一覧なので明細は展開せず、伝票ヘッダの合計値を出す。
/// </summary>
public partial class NouhinBookPendingCheckListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "納品書未発行チェックリスト";
	protected override string FormFileName => "NouhinBookPendingCheckList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddDays(-30).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>true=未発行のみ / false=発行済みも含めて発行状況を確認する。</summary>
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
			MessageEx.ShowWarningDialog("売上日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo);
		if (IsPendingOnly) {
			where += " AND ifnull(h.IsPrint,0) = 0";
		}

		const string Kingaku = "CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END";
		var kubunLabel = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));

		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("h.DenDay")}  AS denDayLabel,
    CAST(h.Id AS TEXT)                     AS denNoText,
    {TranMeisaiSql.HeaderCode("VTokui")}   AS tokuiCode,
    {TranMeisaiSql.HeaderName("VTokui")}   AS tokuiName,
    {kubunLabel}                           AS kubunText,
    h.SuTotal                              AS suTotal,
    h.KingakuTotal                         AS kingakuTotal,
    h.Tax                                  AS tax,
    {Kingaku}                              AS total,
    CASE WHEN ifnull(h.IsPrint,0) = 0 THEN '未発行' ELSE '発行済' END AS printState,
    ifnull(h.ManualNo,'')                  AS manualNo,
    {TranMeisaiSql.HeaderName("VShain")}   AS shainName
FROM Tran00Uriage h
WHERE {where}
ORDER BY h.DenDay, h.Id";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
