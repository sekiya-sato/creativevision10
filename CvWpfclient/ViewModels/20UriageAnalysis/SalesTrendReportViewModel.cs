using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 販売動向表。店舗売上(Tran01Tenuri)を店舗×期間で集計し、日別／週別／月別に切り替えて動向を見る。
/// 客単価は「売上金額÷伝票数」。伝票1枚を1客として扱う。
/// 週は「その週の月曜日」をキーにする（年をまたぐ週で並びが崩れないため。詳細は PeriodSql）。
/// </summary>
public partial class SalesTrendReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "販売動向表";
	protected override string FormFileName => "SalesTrendReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>日別で集計する</summary>
	[ObservableProperty]
	public partial bool IsDaily { get; set; } = true;

	/// <summary>週別で集計する</summary>
	[ObservableProperty]
	public partial bool IsWeekly { get; set; }

	/// <summary>月別で集計する</summary>
	[ObservableProperty]
	public partial bool IsMonthly { get; set; }

	/// <summary>集計単位。true=店舗別 / false=全店合計。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	PeriodUnit Unit => IsWeekly ? PeriodUnit.Week : IsMonthly ? PeriodUnit.Month : PeriodUnit.Day;

	[RelayCommand]
	void SelectShopCodeFrom() => ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;

	[RelayCommand]
	void SelectShopCodeTo() => ShopCodeTo = SelectShopCode() ?? ShopCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var unit = Unit;
		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTenpo"), ShopCodeFrom, ShopCodeTo);

		var shopCode = IsByShop ? TranMeisaiSql.HeaderCode("VTenpo") : "''";
		var shopName = IsByShop ? TranMeisaiSql.HeaderName("VTenpo") : "'全店'";

		var sql = $@"
WITH agg AS (
    SELECT
        {PeriodSql.Key("h.DenDay", unit)}   AS periodKey,
        {PeriodSql.Label("h.DenDay", unit)} AS periodLabel,
        {shopCode} AS shopCode,
        {shopName} AS shopName,
        COUNT(*)              AS denCount,
        SUM(h.SuTotal)        AS su,
        SUM(h.KingakuTotal)   AS kingaku,
        SUM(h.JodaiTotal)     AS jodaiTotal,
        SUM(h.Nebiki00Total)  AS nebiki
    FROM Tran01Tenuri h
    WHERE {where}
    GROUP BY periodKey, periodLabel, shopCode, shopName
)
SELECT
    periodLabel,
    shopCode, shopName,
    denCount, su, kingaku, jodaiTotal, nebiki,
    CASE WHEN denCount != 0
         THEN CAST(ROUND(CAST(kingaku AS REAL) / denCount) AS INTEGER)
         ELSE 0 END AS kyakuTanka,
    SUM(kingaku) OVER (PARTITION BY shopCode ORDER BY periodKey
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumKingaku
FROM agg
ORDER BY shopCode, periodKey";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
