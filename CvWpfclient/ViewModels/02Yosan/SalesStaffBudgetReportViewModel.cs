using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

/// <summary>
/// 販売員予算表。指定年月の販売員別・日別に予算・売上・差異・達成率と累計を印字する。
/// 予算は MasterYosanHanbai(社員×日)。実績は Tran01Tenuri の明細(Jmeisai)を展開し、
/// 明細担当社員(Id_Shain)を優先、未設定(0)の明細は伝票ヘッダの入力社員へ寄せて集計する。
/// </summary>
public partial class SalesStaffBudgetReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "販売員予算表";
	protected override string FormFileName => "SalesStaffBudgetReport.qfm";

	/// <summary>明細JSON(Jmeisai)からの値取り出し。ShiireSlipPrint と同じ抽出規則。</summary>
	const string M = "json_extract(m.value,";

	[ObservableProperty]
	public partial DateTime SelectedYearMonth { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial string SelectedYearMonthString { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShainCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainCodeTo { get; set; } = string.Empty;

	/// <summary>出力区分。true=販売員別明細 / false=販売員合計(日別合計)。</summary>
	[ObservableProperty]
	public partial bool IsByStaff { get; set; } = true;

	/// <summary>出力対象。false=全て / true=予算または売上がある社員のみ。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	partial void OnSelectedYearMonthChanged(DateTime value) {
		SelectedYearMonthString = value.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	}

	[RelayCommand]
	void SelectShainCodeFrom() {
		ShainCodeFrom = SelectShainCode() ?? ShainCodeFrom;
	}

	[RelayCommand]
	void SelectShainCodeTo() {
		ShainCodeTo = SelectShainCode() ?? ShainCodeTo;
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
		var shainWhere = BuildCodeRangeWhere(parameters, "Code", ShainCodeFrom, ShainCodeTo);

		// dateFrom/dateTo は SelectedYearMonth 由来の yyyyMMdd 文字列でユーザ入力を含まないため直接埋め込む。
		var activeOnlyWhere = IsActiveOnly ? $@"
        AND (
            Id IN (
                SELECT Id_Shain FROM MasterYosanHanbai
                WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}' AND UriYosan <> 0
            )
            OR Id IN (SELECT idShain FROM staff_sales)
        )" : "";

		var sql = $@"
WITH RECURSIVE days(day) AS (
    SELECT 1 UNION ALL SELECT day+1 FROM days WHERE day < {daysInMonth}
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
staff_sales AS (
    -- 明細担当社員が未設定(0/NULL)の行は伝票ヘッダの入力社員へ寄せる。
    -- json_valid で不正JSONをガードする（空文字などに json_extract を当てると例外になる）。
    SELECT
        COALESCE(NULLIF(CAST({M}'$.Id_Shain') AS INTEGER), 0), h.Id_Shain) AS idShain,
        h.DenDay AS denDay,
        SUM(CAST(COALESCE({M}'$.Kingaku'), 0) AS INTEGER)) AS kingaku
    FROM Tran01Tenuri h, json_each(h.Jmeisai) m
    WHERE h.DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
      AND json_valid(h.Jmeisai)
    GROUP BY idShain, denDay
),
staff AS (
    SELECT Id, Code, Name FROM MasterShain
    WHERE 1=1 {shainWhere}{activeOnlyWhere}
),
budget AS (
    SELECT Id_Shain, DenDay, SUM(UriYosan) AS uriYosan
    FROM MasterYosanHanbai
    WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
    GROUP BY Id_Shain, DenDay
),
daily_by_staff AS (
    SELECT
        st.Code, st.Name,
        c.day, c.youbi,
        COALESCE(b.uriYosan, 0) AS uriYosan,
        COALESCE(sa.kingaku, 0) AS kingaku
    FROM staff st
    CROSS JOIN calendar c
    LEFT JOIN budget b ON b.Id_Shain = st.Id AND b.DenDay = c.denDay
    LEFT JOIN staff_sales sa ON sa.idShain = st.Id AND sa.denDay = c.denDay
)";

		// 累計の集計単位が「販売員別=社員内の日付順」「合計=全社員合計の日付順」で変わるため PARTITION を出し分ける。
		if (IsByStaff) {
			sql += $@"
SELECT
    '{yearMonthLabel}' AS yearMonth,
    printf('%02d', day) AS day,
    youbi,
    Code, Name,
    uriYosan AS budget,
    kingaku AS sales,
    kingaku - uriYosan AS budgetDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(kingaku AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS budgetRatio,
    SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) AS cumBudget,
    SUM(kingaku) OVER (PARTITION BY Code ORDER BY day) AS cumSales,
    CASE WHEN SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) != 0
         THEN ROUND(CAST(SUM(kingaku) OVER (PARTITION BY Code ORDER BY day) AS REAL)
                    / SUM(uriYosan) OVER (PARTITION BY Code ORDER BY day) * 100, 1)
         ELSE 0 END AS cumRatio
FROM daily_by_staff
ORDER BY Code, day";
		}
		else {
			sql += $@"
,daily_total AS (
    SELECT
        day, youbi,
        SUM(uriYosan) AS uriYosan,
        SUM(kingaku) AS kingaku
    FROM daily_by_staff
    GROUP BY day, youbi
)
SELECT
    '{yearMonthLabel}' AS yearMonth,
    printf('%02d', day) AS day,
    youbi,
    '' AS Code, '全販売員' AS Name,
    uriYosan AS budget,
    kingaku AS sales,
    kingaku - uriYosan AS budgetDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(kingaku AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS budgetRatio,
    SUM(uriYosan) OVER (ORDER BY day) AS cumBudget,
    SUM(kingaku) OVER (ORDER BY day) AS cumSales,
    CASE WHEN SUM(uriYosan) OVER (ORDER BY day) != 0
         THEN ROUND(CAST(SUM(kingaku) OVER (ORDER BY day) AS REAL)
                    / SUM(uriYosan) OVER (ORDER BY day) * 100, 1)
         ELSE 0 END AS cumRatio
FROM daily_total
ORDER BY day";
		}

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
