using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/// <summary>
/// 売上週報･月報。店舗×期間（週別／月別）で売上・予算・予算比・前年同期・前年比・累計を印字する。
/// 販売動向表が売上そのものの推移を見るのに対し、こちらは予算と前年に対する達成度を見る。
///
/// 前年同期は「同じ期間キーの1年前」で突き合わせる。
/// 週別のとき、1年前の同じ週（月曜起点）は暦のずれで日付が一致しないため、
/// 「当該週の月曜日の1年前を含む週」を前年同期として扱う。
/// </summary>
public partial class UriageShuhouGeppouViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "売上週報･月報";
	protected override string FormFileName => "UriageShuhouGeppou.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddMonths(-2).ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>true=週報（週別） / false=月報（月別）。</summary>
	[ObservableProperty]
	public partial bool IsWeekly { get; set; } = true;

	/// <summary>集計単位。true=店舗別 / false=全店合計。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	PeriodUnit Unit => IsWeekly ? PeriodUnit.Week : PeriodUnit.Month;

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
		// 前年同期のため、抽出範囲は1年前まで広げる
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from.AddYears(-1)));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var rangeFrom = AddSqlParameter(parameters, ToDenDay(from));
		var rangeTo = AddSqlParameter(parameters, ToDenDay(to));
		var shopWhere = BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTenpo"), ShopCodeFrom, ShopCodeTo);

		var shopCode = IsByShop ? TranMeisaiSql.HeaderCode("VTenpo") : "''";
		var shopName = IsByShop ? TranMeisaiSql.HeaderName("VTenpo") : "'全店'";

		// 前年同期のキー: 月別なら年月を1年戻す。週別なら月曜日を1年戻してその週の月曜日に丸める。
		// withPrev は agg(a) と budget(b) を結合し両方が periodKey を持つため、必ず a. で修飾する。
		var prevKey = unit == PeriodUnit.Month
			? "strftime('%Y%m', date(substr(a.periodKey,1,4) || '-' || substr(a.periodKey,5,2) || '-01', '-1 year'))"
			: "date(date(a.periodKey, '-1 year'), '-' || ((strftime('%w', date(a.periodKey, '-1 year')) + 6) % 7) || ' days')";

		// 補間ホールの中に " を書くと verbatim 文字列が途中で終わるため、式は必ずローカルへ切り出す
		var budgetShopCode = IsByShop ? "ifnull(t.Code,'')" : "''";
		var periodLabel = unit == PeriodUnit.Month
			? "(substr(periodKey,1,4) || '/' || substr(periodKey,5,2))"
			: "(replace(periodKey,'-','/') || '週')";

		var sql = $@"
WITH sales AS (
    SELECT
        {PeriodSql.Key("h.DenDay", unit)} AS periodKey,
        {shopCode} AS shopCode,
        {shopName} AS shopName,
        h.DenDay   AS denDay,
        h.SuTotal  AS su,
        h.KingakuTotal AS kingaku
    FROM Tran01Tenuri h
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
      {shopWhere}
),
budget AS (
    SELECT
        {PeriodSql.Key("y.DenDay", unit)} AS periodKey,
        {budgetShopCode} AS shopCode,
        SUM(y.UriYosan) AS yosan
    FROM MasterYosanBrand y
    LEFT JOIN MasterTokui t ON t.Id = y.Id_Tenpo
    WHERE y.DenDay >= {dayFrom} AND y.DenDay <= {dayTo}
    GROUP BY periodKey, shopCode
),
agg AS (
    SELECT
        periodKey, shopCode, shopName,
        MIN(denDay)   AS minDay,
        MAX(denDay)   AS maxDay,
        COUNT(*)      AS denCount,
        SUM(su)       AS su,
        SUM(kingaku)  AS kingaku
    FROM sales
    GROUP BY periodKey, shopCode, shopName
),
withPrev AS (
    SELECT
        a.*,
        {prevKey} AS prevPeriodKey,
        ifnull(b.yosan, 0) AS yosan
    FROM agg a
    LEFT JOIN budget b ON b.periodKey = a.periodKey AND b.shopCode = a.shopCode
),
joined AS (
    SELECT
        w.*,
        ifnull(p.kingaku, 0) AS prevKingaku
    FROM withPrev w
    LEFT JOIN agg p ON p.periodKey = w.prevPeriodKey AND p.shopCode = w.shopCode
)
SELECT
    {periodLabel} AS periodLabel,
    shopCode, shopName,
    su, kingaku, yosan,
    CASE WHEN yosan != 0 THEN ROUND(CAST(kingaku AS REAL) / yosan * 100, 1) ELSE 0 END AS yosanRatio,
    prevKingaku,
    CASE WHEN prevKingaku != 0 THEN ROUND(CAST(kingaku AS REAL) / prevKingaku * 100, 1) ELSE 0 END AS prevRatio,
    SUM(kingaku) OVER (PARTITION BY shopCode ORDER BY periodKey
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumKingaku
FROM joined
WHERE maxDay >= {rangeFrom} AND minDay <= {rangeTo}
ORDER BY shopCode, periodKey";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
