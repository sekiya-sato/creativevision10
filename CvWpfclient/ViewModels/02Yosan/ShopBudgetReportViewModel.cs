using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

public partial class ShopBudgetReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "店舗予算表";
	protected override string FormFileName => "ShopBudgetReport.qfm";

	[ObservableProperty]
	public partial DateTime SelectedYearMonth { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial string SelectedYearMonthString { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	[ObservableProperty]
	public partial bool IsDateComparison { get; set; } = true;

	/// <summary>
	/// 出力対象。false=全て / true=当年売上あり（指定年月に売上がある店舗のみ）。
	/// </summary>
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
		if (!TryApplySelectedYearMonth()) return Task.FromResult<QueryListSqlParam?>(null);
		ct.ThrowIfCancellationRequested();

		var (dateFrom, dateTo) = GetMonthRange(SelectedYearMonth);
		var daysInMonth = DateTime.DaysInMonth(SelectedYearMonth.Year, SelectedYearMonth.Month);
		var yearMonthLabel = SelectedYearMonth.ToString("yy年MM月", CultureInfo.InvariantCulture);
		var year = SelectedYearMonth.Year;
		var month = SelectedYearMonth.Month;
		var isDateComparisonStr = IsDateComparison ? "1" : "0";

		List<string> parameters = [];
		var shopWhere = BuildCodeRangeWhere(parameters, "Code", ShopCodeFrom, ShopCodeTo);

		// 「当年売上あり」選択時は、指定年月に売上（月間合計金額 <> 0）がある店舗のみへ絞り込む。
		// dateFrom/dateTo は SelectedYearMonth 由来の yyyyMMdd 文字列でユーザ入力を含まないため直接埋め込む。
		var salesOnlyWhere = "";
		if (IsSalesOnly) {
			salesOnlyWhere = $@"
        AND Id IN (
            SELECT Id_Tenpo FROM Tran01Tenuri
            WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
            GROUP BY Id_Tenpo
            HAVING SUM(KingakuTotal) <> 0
        )";
		}

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
prev_calendar AS (
    SELECT
        denDay,
        strftime('%Y%m%d',
            CASE
                WHEN {isDateComparisonStr} = '1' THEN date(denDay_fmt, '-1 year')
                ELSE date(
                    date(denDay_fmt, '-1 year'),
                    (strftime('%w', denDay_fmt) - strftime('%w', date(denDay_fmt, '-1 year'))) || ' days'
                )
            END
        ) AS prevDenDay
    FROM (
        SELECT
            denDay,
            substr(denDay, 1, 4) || '-' || substr(denDay, 5, 2) || '-' || substr(denDay, 7, 2) AS denDay_fmt
        FROM calendar
    )
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
prev_sales AS (
    -- 前年突き合わせ日は prev_calendar が日別に算出する（曜日対比では前年同月の範囲外へ最大±6日ずれる）。
    -- 固定の月範囲で絞ると月初・月末の前年比が欠落するため、実際に必要な prevDenDay 集合で厳密に絞る。
    SELECT Id_Tenpo, DenDay, SUM(KingakuTotal) AS kingakuTotal
    FROM Tran01Tenuri
    WHERE DenDay IN (SELECT prevDenDay FROM prev_calendar)
    GROUP BY Id_Tenpo, DenDay
),
daily_by_shop AS (
    SELECT
        s.Code, s.Name, '{yearMonthLabel}' AS yearMonth,
        c.day, c.youbi, pc.prevDenDay,
        COALESCE(b.UriYosan, 0) AS uriYosan,
        COALESCE(sa.KingakuTotal, 0) AS kingakuTotal,
        COALESCE(ps.KingakuTotal, 0) AS prevKingakuTotal
    FROM shops s
    CROSS JOIN calendar c
    LEFT JOIN prev_calendar pc ON pc.denDay = c.denDay
    LEFT JOIN budget b ON b.Id_Tenpo = s.Id AND b.DenDay = c.denDay
    LEFT JOIN sales sa ON sa.Id_Tenpo = s.Id AND sa.DenDay = c.denDay
    LEFT JOIN prev_sales ps ON ps.Id_Tenpo = s.Id AND ps.DenDay = pc.prevDenDay
)";

		if (IsByShop) {
			sql += @"
SELECT
    Code, Name, yearMonth,
    printf('%02d', day) AS day, youbi,
    CAST(uriYosan / 1000 AS INTEGER) AS budgetK,
    CAST(SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) / 1000 AS INTEGER) AS cumBudgetK,
    kingakuTotal AS sales,
    SUM(kingakuTotal) OVER (PARTITION BY Code ORDER BY day) AS cumSales,
    prevKingakuTotal AS prevSales,
    SUM(prevKingakuTotal) OVER (PARTITION BY Code ORDER BY day) AS cumPrevSales,
    CASE WHEN prevKingakuTotal != 0
         THEN ROUND(CAST(kingakuTotal AS REAL) / prevKingakuTotal * 100, 1)
         ELSE 0 END AS prevRatio,
    kingakuTotal - uriYosan AS budgetDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(kingakuTotal AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS budgetRatio,
    0 AS kyakusu
FROM daily_by_shop
ORDER BY Code, day";
		}
		else {
			sql += @"
,daily_total AS (
    SELECT
        '' AS Code, '全店' AS Name, yearMonth,
        day, youbi,
        SUM(uriYosan) AS uriYosan,
        SUM(kingakuTotal) AS kingakuTotal,
        SUM(prevKingakuTotal) AS prevKingakuTotal
    FROM daily_by_shop
    GROUP BY yearMonth, day, youbi
)
SELECT
    Code, Name, yearMonth,
    printf('%02d', day) AS day, youbi,
    CAST(uriYosan / 1000 AS INTEGER) AS budgetK,
    CAST(SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) / 1000 AS INTEGER) AS cumBudgetK,
    kingakuTotal AS sales,
    SUM(kingakuTotal) OVER (PARTITION BY Code ORDER BY day) AS cumSales,
    prevKingakuTotal AS prevSales,
    SUM(prevKingakuTotal) OVER (PARTITION BY Code ORDER BY day) AS cumPrevSales,
    CASE WHEN prevKingakuTotal != 0
         THEN ROUND(CAST(kingakuTotal AS REAL) / prevKingakuTotal * 100, 1)
         ELSE 0 END AS prevRatio,
    kingakuTotal - uriYosan AS budgetDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(kingakuTotal AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS budgetRatio,
    0 AS kyakusu
FROM daily_total
ORDER BY day";
		}

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}

	bool TryApplySelectedYearMonth() {
		if (!TryParseYearMonth(SelectedYearMonthString, out var yearMonth)) return false;
		SelectedYearMonth = yearMonth;
		return true;
	}
}
