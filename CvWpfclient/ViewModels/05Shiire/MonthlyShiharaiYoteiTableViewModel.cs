using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 月別支払予定表。締め済みの買掛残高に仕入先ごとの支払条件を当てて、支払予定日別の支払予定額を印字する。
///
/// 予定日は支払計算が保存した SummaryKaiShi.ShiharaiYoteiDay を使う（PayDay=0 は自社締日、3.4）。
/// 期首残高CSVで予定日が空の行だけ MasterShiire の支払条件から同じ規則で補う。
/// - PayMonth = 締月から何ヶ月後に支払うか（0=当月, 1=翌月, 2=翌々月）
/// - PayDay   = 支払日（0以下は自社締日。31以上/その月の日数超は月末へ丸める）
/// 予定日 = 締年月に PayMonth を加算した月の PayDay 日。
/// 複数締日で同じ予定日に複数の締めがある場合は、最後の締めの残高を予定額とする（累積残高のため合算しない）。
///
/// 金額は SummaryKaiShi（支払計算＝月次更新処理の成果物）の当月末残高を使う。
/// SummaryKaiShi は対象期間のみの集計（繰越なし）なので、当月末残高は対象期間の開始(DayFrom)
/// より前の全行を SUM(TotalShiire - TotalOut) で積んだ PreviousBalance に当期間の Balance を
/// 加えて求める。前残を前月1行だけから取ると、前月に行がない仕入先の繰越を失うため全過去行を積む。
/// 締め処理を回していない支払日は行が無く空になる。
/// </summary>
public partial class MonthlyShiharaiYoteiTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "月別支払予定表";
	protected override string FormFileName => "MonthlyShiharaiYoteiTable.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜24）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "6";

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=支払予定日×仕入先の明細 / false=支払予定月の合計のみ。</summary>
	[ObservableProperty]
	public partial bool IsByShiire { get; set; } = true;

	/// <summary>出力対象。true=予定額が0以外のみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 24) {
			MessageEx.ShowWarningDialog("出力月数は 1〜24 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		// 予定日が対象範囲に入る締めデータを拾うため、抽出元の支払日は最大2ヶ月前まで遡る（PayMonth 最大2想定）。
		var end = start.AddMonths(months - 1);
		List<string> parameters = [];
		var dataFrom = AddSqlParameter(parameters, ToDenDay(start.AddMonths(-3)));
		var dataTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var rangeFrom = AddSqlParameter(parameters, start.ToString("yyyyMM", CultureInfo.InvariantCulture));
		var rangeTo = AddSqlParameter(parameters, end.ToString("yyyyMM", CultureInfo.InvariantCulture));
		var shiireWhere = BuildCodeRangeWhere(parameters, "s.Code", ShiireCodeFrom, ShiireCodeTo);

		// 支払予定日 = 締年月に PayMonth ヶ月を加算した月の PayDay 日。
		// PayDay が 0 以下 / 31 以上 / その月の日数超 の場合は月末へ丸める。
		// baseMonth（加算後の月の1日）は同じ SELECT 内で参照できないため式ごと展開する。
		const string BaseMonth =
			"date(substr(k.DenDay,1,4) || '-' || substr(k.DenDay,5,2) || '-01', '+' || ifnull(s.PayMonth,1) || ' months')";
		const string MonthEnd = $"date({BaseMonth}, '+1 month', '-1 day')";
		// 支払日が未設定(0以下)なら自社締日を支払日とする(計算処理 SummaryDb.CalcSummaryKaiShi と同じ規則、3.4)。
		const string EffPayDay = $"(CASE WHEN ifnull(s.PayDay,0) <= 0 THEN {ClosingDaySet.OwnShimeSubquerySql} ELSE s.PayDay END)";
		const string PayDate = $@"CASE
            WHEN {EffPayDay} >= 31
                 OR CAST(strftime('%d', {MonthEnd}) AS INTEGER) < {EffPayDay}
              THEN {MonthEnd}
            ELSE date({BaseMonth}, '+' || ({EffPayDay} - 1) || ' days')
        END";
		// 予定日は計算処理が保存した値(ShiharaiYoteiDay)を正とする。期首残高CSVで予定日を空のまま取り込んだ行だけ
		// 上の式で補う。
		const string DueDate = $@"CASE
            WHEN length(ifnull(k.ShiharaiYoteiDay,'')) = 8
              THEN substr(k.ShiharaiYoteiDay,1,4) || '-' || substr(k.ShiharaiYoteiDay,5,2) || '-' || substr(k.ShiharaiYoteiDay,7,2)
            ELSE {PayDate}
        END";

		var groupKeys = IsByShiire ? "yoteiYm, yoteiDay, shiireCode, shiireName" : "yoteiYm";
		var selectShiire = IsByShiire ? "shiireCode, shiireName" : "'' AS shiireCode, '(月合計)' AS shiireName";
		var selectDay = IsByShiire ? "MAX(yoteiDay)" : "''";
		var having = IsActiveOnly ? "HAVING SUM(CASE WHEN rn = 1 THEN balance ELSE 0 END) != 0" : "";

		// 予定金額は当月末残高（PreviousBalance + Balance）。PreviousBalance は対象期間の開始(DayFrom)
		// より前の全行を SUM(TotalShiire - TotalOut) で積む。行ごとに DayFrom が異なるため
		// 仕入先＋DayFrom の相関スカラサブクエリにする。
		var sql = $@"
WITH scheduled AS (
    SELECT
        s.Code AS shiireCode, s.Name AS shiireName,
        k.DenDay AS shimeDay,
        (SELECT ifnull(SUM(pb.TotalShiire - pb.TotalOut),0) FROM SummaryKaiShi pb
          WHERE pb.Id_Shiire = k.Id_Shiire AND pb.DayTo < k.DayFrom) + k.Balance AS balance,
        k.TotalShiire AS totalShiire,
        k.TotalOut    AS totalOut,
        {DueDate} AS payDate
    FROM SummaryKaiShi k
    JOIN MasterShiire s ON s.Id = k.Id_Shiire
    WHERE k.DenDay >= {dataFrom} AND k.DenDay <= {dataTo}
      {shiireWhere}
),
filtered AS (
    SELECT
        strftime('%Y%m', payDate)   AS yoteiYm,
        strftime('%Y%m%d', payDate) AS yoteiDay,
        shiireCode, shiireName, shimeDay, balance,
        -- 同じ取引先・同じ予定日に複数の締め(複数締日)がある場合、balance は締めごとの累積残高なので
        -- 合算すると前残を重複して数える。予定額は最後の締めの累積残高だけを採る。
        ROW_NUMBER() OVER (PARTITION BY shiireCode, strftime('%Y%m%d', payDate) ORDER BY shimeDay DESC) AS rn,
        totalShiire, totalOut
    FROM scheduled
    WHERE strftime('%Y%m', payDate) >= {rangeFrom}
      AND strftime('%Y%m', payDate) <= {rangeTo}
)
SELECT
    substr(yoteiYm,1,4) || '/' || substr(yoteiYm,5,2) AS yoteiYmLabel,
    {TranMeisaiSql.DateLabel(selectDay)} AS yoteiDayLabel,
    {selectShiire},
    SUM(totalShiire) AS totalShiire,
    SUM(totalOut)    AS totalOut,
    SUM(CASE WHEN rn = 1 THEN balance ELSE 0 END) AS yoteiKingaku,
    COUNT(*)         AS shimeCount,
    {TranMeisaiSql.DateLabel("MAX(shimeDay)")} AS lastShimeDay
FROM filtered
GROUP BY {groupKeys}
{having}
ORDER BY yoteiYm, yoteiDayLabel, shiireCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
