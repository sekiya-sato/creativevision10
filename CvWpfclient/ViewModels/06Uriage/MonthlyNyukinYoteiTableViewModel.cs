using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 月別入金予定表。締め済みの売掛残高に得意先ごとの回収条件を当てて、入金予定日別の入金予定額を印字する。
/// 月別支払予定表の売上側の対応帳票。
///
/// 予定日は MasterTokui（MasterTorihiki 派生）の回収条件から算出する。
/// - PayMonth = 締月から何ヶ月後に入金されるか（0=当月, 1=翌月, 2=翌々月）
/// - PayDay   = 入金日（1〜31。0以下/31以上/その月の日数超は月末へ丸める）
/// 予定日 = 請求年月に PayMonth を加算した月の PayDay 日。
///
/// 金額は SummaryUriSei（請求計算＝月次更新処理の成果物）の当月残高を使う。
/// 締め処理を回していない請求日は行が無く空になる。
/// </summary>
public partial class MonthlyNyukinYoteiTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "月別入金予定表";
	protected override string FormFileName => "MonthlyNyukinYoteiTable.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜24）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "6";

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=入金予定日×得意先の明細 / false=入金予定月の合計のみ。</summary>
	[ObservableProperty]
	public partial bool IsByTokui { get; set; } = true;

	/// <summary>出力対象。true=予定額が0以外のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 24) {
			MessageEx.ShowWarningDialog("出力月数は 1〜24 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		// 予定日が対象範囲に入る締めデータを拾うため、抽出元の請求日は3ヶ月前まで遡る。
		var end = start.AddMonths(months - 1);
		List<string> parameters = [];
		var dataFrom = AddSqlParameter(parameters, ToDenDay(start.AddMonths(-3)));
		var dataTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var rangeFrom = AddSqlParameter(parameters, start.ToString("yyyyMM", CultureInfo.InvariantCulture));
		var rangeTo = AddSqlParameter(parameters, end.ToString("yyyyMM", CultureInfo.InvariantCulture));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "t.Code", TokuiCodeFrom, TokuiCodeTo);

		// baseMonth（加算後の月の1日）は同じ SELECT 内で参照できないため式ごと展開する。
		const string BaseMonth =
			"date(substr(u.DenDay,1,4) || '-' || substr(u.DenDay,5,2) || '-01', '+' || ifnull(t.PayMonth,1) || ' months')";
		const string MonthEnd = $"date({BaseMonth}, '+1 month', '-1 day')";
		const string PayDate = $@"CASE
            WHEN ifnull(t.PayDay,0) <= 0 OR ifnull(t.PayDay,0) >= 31
                 OR CAST(strftime('%d', {MonthEnd}) AS INTEGER) < t.PayDay
              THEN {MonthEnd}
            ELSE date({BaseMonth}, '+' || (t.PayDay - 1) || ' days')
        END";

		var groupKeys = IsByTokui ? "yoteiYm, yoteiDay, tokuiCode, tokuiName" : "yoteiYm";
		var selectTokui = IsByTokui ? "tokuiCode, tokuiName" : "'' AS tokuiCode, '(月合計)' AS tokuiName";
		var selectDay = IsByTokui ? "MAX(yoteiDay)" : "''";
		var having = IsActiveOnly ? "HAVING SUM(balance) != 0" : "";

		var sql = $@"
WITH scheduled AS (
    SELECT
        t.Code AS tokuiCode, t.Name AS tokuiName,
        u.DenDay AS shimeDay,
        u.Balance    AS balance,
        u.TotalSales AS totalSales,
        u.TotalIn    AS totalIn,
        {PayDate} AS payDate
    FROM SummaryUriSei u
    JOIN MasterTokui t ON t.Id = u.Id_Tokui
    WHERE u.DenDay >= {dataFrom} AND u.DenDay <= {dataTo}
      {tokuiWhere}
),
filtered AS (
    SELECT
        strftime('%Y%m', payDate)   AS yoteiYm,
        strftime('%Y%m%d', payDate) AS yoteiDay,
        tokuiCode, tokuiName, shimeDay, balance, totalSales, totalIn
    FROM scheduled
    WHERE strftime('%Y%m', payDate) >= {rangeFrom}
      AND strftime('%Y%m', payDate) <= {rangeTo}
)
SELECT
    substr(yoteiYm,1,4) || '/' || substr(yoteiYm,5,2) AS yoteiYmLabel,
    {TranMeisaiSql.DateLabel(selectDay)} AS yoteiDayLabel,
    {selectTokui},
    SUM(totalSales) AS totalSales,
    SUM(totalIn)    AS totalIn,
    SUM(balance)    AS yoteiKingaku,
    COUNT(*)        AS shimeCount,
    {TranMeisaiSql.DateLabel("MAX(shimeDay)")} AS lastShimeDay
FROM filtered
GROUP BY {groupKeys}
{having}
ORDER BY yoteiYm, yoteiDayLabel, tokuiCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
