using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

/// <summary>
/// 日別店別予算表。指定年月の「日付→店舗」順で予算・売上・差異・達成率と累計を印字する。
/// 同じ元データを扱う ShopBudgetReport(店舗予算表) は「店舗→日付」順で前年比を主眼にするのに対し、
/// こちらは日単位で全店を並べて当日の進捗を見るための帳票。
/// 予算は MasterYosanBrand(店舗×ブランド×日)をブランド横断で合計し、実績は Tran01Tenuri のヘッダ合計を使う。
/// </summary>
public partial class DailyShopBudgetReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "日別店別予算表";
	protected override string FormFileName => "DailyShopBudgetReport.qfm";

	[ObservableProperty]
	public partial DateTime SelectedYearMonth { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial string SelectedYearMonthString { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>出力区分。true=店舗別明細 / false=日計(全店合計)。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	/// <summary>出力対象。false=全て / true=当年売上あり（指定年月に売上がある店舗のみ）。</summary>
	[ObservableProperty]
	public partial bool IsSalesOnly { get; set; }

	partial void OnSelectedYearMonthChanged(DateTime value) {
		SelectedYearMonthString = value.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	}

	[RelayCommand]
	void SelectShopCodeFrom() {
		ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;
	}

	[RelayCommand]
	void SelectShopCodeTo() {
		ShopCodeTo = SelectShopCode() ?? ShopCodeTo;
	}

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(SelectedYearMonthString, out var yearMonth)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		SelectedYearMonth = yearMonth;
		ct.ThrowIfCancellationRequested();

		var (dateFrom, dateTo) = GetMonthRange(SelectedYearMonth);
		var daysInMonth = DateTime.DaysInMonth(SelectedYearMonth.Year, SelectedYearMonth.Month);
		var yearMonthLabel = SelectedYearMonth.ToString("yy年MM月", CultureInfo.InvariantCulture);
		var year = SelectedYearMonth.Year;
		var month = SelectedYearMonth.Month;

		List<string> parameters = [];
		var shopWhere = BuildCodeRangeWhere(parameters, "Code", ShopCodeFrom, ShopCodeTo);

		// 「当年売上あり」選択時は、指定年月に売上（月間合計金額 <> 0）がある店舗のみへ絞り込む。
		// dateFrom/dateTo は SelectedYearMonth 由来の yyyyMMdd 文字列でユーザ入力を含まないため直接埋め込む。
		var salesOnlyWhere = IsSalesOnly ? $@"
        AND Id IN (
            SELECT Id_Tenpo FROM Tran01Tenuri
            WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
            GROUP BY Id_Tenpo
            HAVING SUM(KingakuTotal) <> 0
        )" : "";

		var sql = $@"
WITH RECURSIVE days(day) AS (
    SELECT 1 UNION ALL SELECT day+1 FROM days WHERE day < {daysInMonth}
),
shops AS (
    SELECT Id, Code, Name FROM MasterTokui
    WHERE TenType = 6 {shopWhere}{salesOnlyWhere}
),
calendar AS (
    SELECT
        printf('%04d%02d%02d', {year}, {month}, day) AS denDay,
        day,
        CASE strftime('%w', printf('%04d-%02d-%02d', {year}, {month}, day))
            WHEN '0' THEN '日' WHEN '1' THEN '月' WHEN '2' THEN '火'
            WHEN '3' THEN '水' WHEN '4' THEN '木' WHEN '5' THEN '金' WHEN '6' THEN '土'
        END AS youbi
    FROM days
),
budget AS (
    SELECT Id_Tenpo, DenDay, SUM(UriYosan) AS uriYosan
    FROM MasterYosanBrand
    WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
    GROUP BY Id_Tenpo, DenDay
),
sales AS (
    SELECT Id_Tenpo, DenDay, SUM(KingakuTotal) AS kingakuTotal
    FROM Tran01Tenuri
    WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
    GROUP BY Id_Tenpo, DenDay
),
daily_by_shop AS (
    SELECT
        s.Code, s.Name,
        c.day, c.youbi,
        COALESCE(b.uriYosan, 0) AS uriYosan,
        COALESCE(sa.kingakuTotal, 0) AS kingakuTotal
    FROM shops s
    CROSS JOIN calendar c
    LEFT JOIN budget b ON b.Id_Tenpo = s.Id AND b.DenDay = c.denDay
    LEFT JOIN sales sa ON sa.Id_Tenpo = s.Id AND sa.DenDay = c.denDay
)";

		// 累計の集計単位が「店舗別=店舗内の日付順」「日計=全店合計の日付順」で変わるため PARTITION を出し分ける。
		if (IsByShop) {
			sql += $@"
SELECT
    '{yearMonthLabel}' AS yearMonth,
    printf('%02d', day) AS day,
    youbi,
    Code, Name,
    uriYosan AS budget,
    kingakuTotal AS sales,
    kingakuTotal - uriYosan AS budgetDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(kingakuTotal AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS budgetRatio,
    SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) AS cumBudget,
    SUM(kingakuTotal) OVER (PARTITION BY Code ORDER BY day) AS cumSales,
    CASE WHEN SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) != 0
         THEN ROUND(CAST(SUM(kingakuTotal) OVER (PARTITION BY Code ORDER BY day) AS REAL)
                    / SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) * 100, 1)
         ELSE 0 END AS cumRatio
FROM daily_by_shop
ORDER BY day, Code";
		}
		else {
			sql += $@"
,daily_total AS (
    SELECT
        day, youbi,
        SUM(uriYosan) AS uriYosan,
        SUM(kingakuTotal) AS kingakuTotal
    FROM daily_by_shop
    GROUP BY day, youbi
)
SELECT
    '{yearMonthLabel}' AS yearMonth,
    printf('%02d', day) AS day,
    youbi,
    '' AS Code, '全店' AS Name,
    uriYosan AS budget,
    kingakuTotal AS sales,
    kingakuTotal - uriYosan AS budgetDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(kingakuTotal AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS budgetRatio,
    SUM(uriYosan) OVER (ORDER BY day) AS cumBudget,
    SUM(kingakuTotal) OVER (ORDER BY day) AS cumSales,
    CASE WHEN SUM(uriYosan) OVER (ORDER BY day) != 0
         THEN ROUND(CAST(SUM(kingakuTotal) OVER (ORDER BY day) AS REAL)
                    / SUM(uriYosan) OVER (ORDER BY day) * 100, 1)
         ELSE 0 END AS cumRatio
FROM daily_total
ORDER BY day";
		}

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
