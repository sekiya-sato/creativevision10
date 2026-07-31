using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 売上チェックリスト。指定期間の売上伝票を明細1行=1レコードで印字し、入力内容の目視突合に使う。
/// 品番別売上チェックリストが「品番へ集計した結果」を見るのに対し、こちらは伝票の生の明細を並べる。
/// 卸売上(Tran00Uriage)と店舗売上(Tran01Tenuri)は取引先列が異なる(VTokui / VTenpo)ため、
/// 取引先コード・名称として同じ位置へ寄せて UNION する。
/// </summary>
public partial class UriageCheckListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "売上チェックリスト";
	protected override string FormFileName => "UriageCheckList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>卸売上(Tran00Uriage)を対象にする</summary>
	[ObservableProperty]
	public partial bool IncludeOroshi { get; set; } = true;

	/// <summary>店舗売上(Tran01Tenuri)を対象にする</summary>
	[ObservableProperty]
	public partial bool IncludeShop { get; set; } = true;

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
		if (!IncludeOroshi && !IncludeShop) {
			MessageEx.ShowWarningDialog("卸売上・店舗売上のどちらかを選択してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];

		// 卸は取引区分に値引(30)があり、店舗には無い。テーブルごとにラベル対応表を分ける。
		var oroshiKubun = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));
		var shopKubun = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumUri01.Uriage, "売上"), ((int)EnumUri01.UriSale, "売上SALE"),
			((int)EnumUri01.Henpin, "返品"), ((int)EnumUri01.HenSale, "返品SALE"),
			((int)EnumUri01.Other, "その他"));

		// @n プレースホルダは出現順の採番なので、SQL片を組む時点で毎回採番し直す。
		string Source(string table, string vTokui, string sourceLabel, string kubunLabel) {
			var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
				+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
				+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode(vTokui), TokuiCodeFrom, TokuiCodeTo);
			return $@"
    SELECT
        {TranMeisaiSql.DateLabel("h.DenDay")} AS denDayLabel,
        h.DenDay AS denDaySort,
        h.Id     AS denNo,
        '{sourceLabel}' AS source,
        {TranMeisaiSql.HeaderCode(vTokui)} AS tokuiCode,
        {TranMeisaiSql.HeaderName(vTokui)} AS tokuiName,
        {TranMeisaiSql.Str("Code_Shohin")}  AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}   AS shohinName,
        {TranMeisaiSql.Str("Mei_Col")}      AS colName,
        {TranMeisaiSql.Str("Mei_Siz")}      AS sizName,
        {TranMeisaiSql.Num("Su")}           AS su,
        {TranMeisaiSql.Num("Tanka")}        AS tanka,
        {TranMeisaiSql.Num("Kingaku")}      AS kingaku,
        {kubunLabel} AS kubunText,
        {TranMeisaiSql.Num("No")} AS meisaiNo
    FROM {table} h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}";
		}

		List<string> sources = [];
		if (IncludeOroshi) sources.Add(Source("Tran00Uriage", "VTokui", "卸", oroshiKubun));
		if (IncludeShop) sources.Add(Source("Tran01Tenuri", "VTenpo", "店", shopKubun));

		var sql = $@"
WITH meisai AS (
{string.Join("\n    UNION ALL\n", sources)}
)
SELECT
    denDayLabel,
    source || '-' || denNo AS denNo,
    tokuiCode, tokuiName,
    shohinCode, shohinName,
    colName, sizName,
    su, tanka, kingaku,
    kubunText
FROM meisai
ORDER BY denDaySort, source, denNo, meisaiNo";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
