using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 担当得意先別予算実績対比表。営業担当×得意先で実績を並べ、担当単位の予算と達成率を対比する。
///
/// 【スキーマ上の制約】cv10 には**得意先別予算のテーブルが存在しない**。
/// 予算テーブルは MasterYosanBrand（店舗×ブランド×日）と MasterYosanHanbai（社員×日）の2本だけで、
/// 得意先ごとに予算を持つ構造がない。
/// そのため本帳票は次の形にしている:
///   - 実績は 担当×得意先 の明細で出す
///   - 予算は MasterYosanHanbai の担当単位の値を使い、**担当計に対する達成率**として出す
///   - 得意先行の予算欄は空（割り振る根拠がないため按分もしない）
/// 得意先別に予算を持たせたい場合は、得意先別予算テーブルの追加が先に必要。
/// </summary>
public partial class TantoTokuiBudgetActualReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "担当得意先別予算実績対比表";
	protected override string FormFileName => "TantoTokuiBudgetActualReport.qfm";

	[ObservableProperty]
	public partial string TargetYearMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜12）。複数月を指定すると期間合計で対比する。</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "1";

	[ObservableProperty]
	public partial string ShainCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=実績または予算があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShainCodeFrom() => ShainCodeFrom = SelectShainCode() ?? ShainCodeFrom;

	[RelayCommand]
	void SelectShainCodeTo() => ShainCodeTo = SelectShainCode() ?? ShainCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(TargetYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 12) {
			MessageEx.ShowWarningDialog("出力月数は 1〜12 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var end = start.AddMonths(months - 1);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var shainWhere = BuildCodeRangeWhere(parameters, "sn.Code", ShainCodeFrom, ShainCodeTo);

		var termLabel = months == 1 ? $"{start:yyyy/MM}" : $"{start:yyyy/MM}～{end:yyyy/MM}";
		var having = IsActiveOnly ? "WHERE jisseki != 0 OR shainYosan != 0" : "";

		var sql = $@"
WITH shains AS (
    SELECT sn.Id, sn.Code, sn.Name FROM MasterShain sn
    WHERE 1=1 {shainWhere}
),
-- 担当単位の予算（得意先別予算はスキーマに存在しないため担当止まり）
budget AS (
    SELECT Id_Shain AS idShain, SUM(UriYosan) AS yosan
    FROM MasterYosanHanbai
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY Id_Shain
),
-- 実績は得意先の営業担当で担当へ紐付ける
actual AS (
    SELECT
        t.Id_Shain AS idShain,
        t.Code     AS tokuiCode,
        t.Name     AS tokuiName,
        COUNT(*)            AS denCount,
        SUM(h.SuTotal)      AS su,
        SUM(h.KingakuTotal) AS jisseki
    FROM Tran00Uriage h
    JOIN MasterTokui t ON t.Id = h.Id_Tokui AND t.TenType = 1
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY t.Id_Shain, t.Code, t.Name
),
joined AS (
    SELECT
        s.Code AS shainCode, s.Name AS shainName,
        ifnull(a.tokuiCode,'') AS tokuiCode,
        ifnull(a.tokuiName,'') AS tokuiName,
        ifnull(a.denCount, 0)  AS denCount,
        ifnull(a.su, 0)        AS su,
        ifnull(a.jisseki, 0)   AS jisseki,
        ifnull(b.yosan, 0)     AS shainYosan
    FROM shains s
    LEFT JOIN actual a ON a.idShain = s.Id
    LEFT JOIN budget b ON b.idShain = s.Id
)
SELECT
    '{termLabel}' AS termLabel,
    shainCode, shainName,
    tokuiCode, tokuiName,
    denCount, su, jisseki,
    -- 担当計の実績と予算。得意先行の予算欄は割り振り根拠がないので出さない
    SUM(jisseki) OVER (PARTITION BY shainCode) AS shainJisseki,
    shainYosan,
    CASE WHEN shainYosan != 0
         THEN ROUND(CAST(SUM(jisseki) OVER (PARTITION BY shainCode) AS REAL) / shainYosan * 100, 1)
         ELSE 0 END AS shainTasseiRatio,
    CASE WHEN SUM(jisseki) OVER (PARTITION BY shainCode) != 0
         THEN ROUND(CAST(jisseki AS REAL) / SUM(jisseki) OVER (PARTITION BY shainCode) * 100, 1)
         ELSE 0 END AS shareRatio
FROM joined
{having}
ORDER BY shainCode, tokuiCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
