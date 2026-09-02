using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 請求一覧表。請求計算で保存した SummaryUriSei を、請求先単位（親のみ）または
/// 得意先明細＋請求先集計（子含む）として印字する。
///
/// SummaryUriSei は対象期間のみの集計（繰越なし）。前月残(prevBalance)・繰越金額(carryOver)は
/// 対象期間の開始(DayFrom)より前の全行を SUM(TotalSales - TotalIn) で積んで都度算出する
/// （PreviousBalance、`Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 2.3）。
/// 当月残(balance)は PreviousBalance + Balance。
/// </summary>
public partial class SeikyuListReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "請求一覧表";
	protected override string FormFileName => IsIncludeChildren
		? "SeikyuListReportPaysakiChild.qfm"
		: "SeikyuListReportPaysaki.qfm";

	[ObservableProperty]
	public partial string TargetMonth { get; set; } = DateTime.Today.ToString("yyyy/MM");

	/// <summary>1～28、99=末日。</summary>
	[ObservableProperty]
	public partial string ShimeDay { get; set; } = "99";

	/// <summary>false=請求集計月 / true=入金予定月。</summary>
	[ObservableProperty]
	public partial bool IsNyukinYoteiBasis { get; set; }

	[ObservableProperty]
	public partial string PaysakiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string PaysakiCodeTo { get; set; } = string.Empty;

	/// <summary>false=親のみ / true=子含む。</summary>
	[ObservableProperty]
	public partial bool IsIncludeChildren { get; set; }

	/// <summary>false=出力基準日順 / true=請求先コード順。</summary>
	[ObservableProperty]
	public partial bool IsPaysakiCodeOrder { get; set; } = true;

	[RelayCommand]
	void SelectPaysakiCodeFrom() => PaysakiCodeFrom = SelectTokuiCode() ?? PaysakiCodeFrom;

	[RelayCommand]
	void SelectPaysakiCodeTo() => PaysakiCodeTo = SelectTokuiCode() ?? PaysakiCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(TargetMonth, out var month)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(ShimeDay, out var shimeDay) || !((shimeDay >= 1 && shimeDay <= 28) || shimeDay == 99)) {
			MessageEx.ShowWarningDialog("締日は 1～28 または 99（末日）で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var (monthFrom, monthTo) = GetMonthRange(month);
		List<string> parameters = [];
		var dateFrom = AddSqlParameter(parameters, monthFrom);
		var dateTo = AddSqlParameter(parameters, monthTo);
		var shime = AddSqlParameter(parameters, shimeDay);
		var paysakiWhere = BuildCodeRangeWhere(parameters, "ifnull(p.Code,c.Code)", PaysakiCodeFrom, PaysakiCodeTo);
		var primaryDay = IsNyukinYoteiBasis ? "s.NyukinYoteiDay" : "s.DenDay";
		var secondaryDay = IsNyukinYoteiBasis ? "s.DenDay" : "s.NyukinYoteiDay";
		var primaryLabel = IsNyukinYoteiBasis ? "入金予定日" : "請求日";
		var secondaryLabel = IsNyukinYoteiBasis ? "請求日" : "入金予定日";
		var orderBy = IsPaysakiCodeOrder
			? (IsIncludeChildren ? "parentCode, primaryDay, secondaryDay, childCode" : "parentCode, primaryDay, secondaryDay")
			: (IsIncludeChildren ? "primaryDay, secondaryDay, parentCode, childCode" : "primaryDay, secondaryDay, parentCode");

		// PreviousBalance は対象期間の開始(DayFrom)より前の全期間を SUM(TotalSales - TotalIn) で
		// 積んだ値（設計書 2.3）。行ごとに DayFrom が異なりうるため得意先＋DayFrom で相関させる。
		// 締日欄は選択した締日をそのまま出す。複数締日(Shime1/2/3)では c.Shime1 が当該行の締日とは
		// 限らない(締日[10,20,99]の得意先で締日20を出力すると10が出てしまう)。本帳票は単一締日で
		// 絞り込むため、全行の締日は選択値に一致する。

		var source = $@"
raw AS (
    SELECT
        {primaryDay} AS primaryDay,
        {secondaryDay} AS secondaryDay,
        {shime} AS shimeDay,
        c.Id AS childId, c.Code AS childCode, c.Name AS childName,
        ifnull(p.Id,c.Id) AS parentId,
        ifnull(p.Code,c.Code) AS parentCode,
        ifnull(p.Name,c.Name) AS parentName,
        (SELECT ifnull(SUM(pb.TotalSales - pb.TotalIn),0) FROM SummaryUriSei pb
          WHERE pb.Id_Tokui = s.Id_Tokui AND pb.DayTo < s.DayFrom) AS prevBalance,
        s.Balance AS rawBalance,
        s.Cash, s.Fee, s.Densai, s.Offset, s.Other,
        s.TotalIn,
        s.Uriage, s.Henpin, s.Nebiki, s.Sonota,
        s.Tax1 + s.Tax2 + s.Tax3 AS tax,
        s.TotalSales,
        s.TotalSales - (s.Tax1 + s.Tax2 + s.Tax3) AS netSales
    FROM SummaryUriSei s
    JOIN MasterTokui c ON c.Id = s.Id_Tokui
    LEFT JOIN MasterTokui p ON p.Id = c.Id_Paysaki
    WHERE c.IsPay = 1
      AND {ClosingDaySet.ContainsShimeSql("c", shime, ClosingDaySet.OwnShimeSubquerySql)}
      AND {primaryDay} >= {dateFrom} AND {primaryDay} <= {dateTo}
      {paysakiWhere}
),
source AS (
    SELECT
        primaryDay, secondaryDay, shimeDay, childId, childCode, childName,
        parentId, parentCode, parentName,
        prevBalance,
        prevBalance + rawBalance AS balance,
        Cash, Fee, Densai, Offset, Other, TotalIn,
        Uriage, Henpin, Nebiki, Sonota, tax, TotalSales,
        prevBalance - TotalIn AS carryOver,
        netSales
    FROM raw
    WHERE (prevBalance + rawBalance) != 0 OR TotalIn != 0 OR TotalSales != 0
)";

		var sql = IsIncludeChildren
			? BuildIncludeChildrenSql(source, primaryLabel, secondaryLabel, orderBy)
			: BuildPaysakiOnlySql(source, primaryLabel, secondaryLabel, orderBy);

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}

	static string BuildPaysakiOnlySql(string source, string primaryLabel, string secondaryLabel, string orderBy) => $@"
WITH {source}
SELECT
    primaryDay AS item1,
    shimeDay AS item2,
    parentCode AS item3,
    parentName AS item4,
    SUM(prevBalance) AS item5,
    SUM(balance) AS item6,
    SUM(Cash) AS item7,
    SUM(Fee) AS item8,
    SUM(Densai) AS item9,
    SUM(Offset) AS item10,
    SUM(Other) AS item11,
    SUM(TotalIn) AS item12,
    SUM(Uriage) AS item13,
    SUM(Henpin) AS item14,
    SUM(Nebiki) AS item15,
    SUM(Sonota) AS item16,
    SUM(tax) AS item17,
    SUM(TotalSales) AS item18,
    secondaryDay AS item19,
    '{primaryLabel}' AS item20,
    '{secondaryLabel}' AS item21,
    SUM(carryOver) AS item22,
    SUM(netSales) AS item23
FROM source
GROUP BY primaryDay, secondaryDay, shimeDay, parentId, parentCode, parentName
ORDER BY {orderBy}";

	static string BuildIncludeChildrenSql(string source, string primaryLabel, string secondaryLabel, string orderBy) => $@"
WITH {source},
rows AS (
    SELECT
        primaryDay, secondaryDay, shimeDay, childId, childCode, childName, parentId, parentCode, parentName,
        prevBalance, balance, Cash, Fee, Densai, Offset, Other, TotalIn,
        Uriage, Henpin, Nebiki, Sonota, tax, TotalSales, carryOver, netSales
    FROM source
)
SELECT
    primaryDay AS item1,
    shimeDay AS item2,
    childCode AS item3,
    childName AS item4,
    prevBalance AS item5,
    balance AS item6,
    Cash AS item7,
    Fee AS item8,
    Densai AS item9,
    Offset AS item10,
    Other AS item11,
    TotalIn AS item12,
    Uriage AS item13,
    Henpin AS item14,
    Nebiki AS item15,
    Sonota AS item16,
    tax AS item17,
    TotalSales AS item18,
    secondaryDay AS item19,
    '{primaryLabel}' AS item20,
    '{secondaryLabel}' AS item21,
    carryOver AS item22,
    netSales AS item23,
    parentCode AS item24,
    parentName AS item25,
    SUM(prevBalance) OVER (PARTITION BY parentId, primaryDay) AS item26,
    SUM(balance) OVER (PARTITION BY parentId, primaryDay) AS item27,
    SUM(Cash) OVER (PARTITION BY parentId, primaryDay) AS item28,
    SUM(Fee) OVER (PARTITION BY parentId, primaryDay) AS item29,
    SUM(Densai) OVER (PARTITION BY parentId, primaryDay) AS item30,
    SUM(Offset) OVER (PARTITION BY parentId, primaryDay) AS item31,
    SUM(Other) OVER (PARTITION BY parentId, primaryDay) AS item32,
    SUM(TotalIn) OVER (PARTITION BY parentId, primaryDay) AS item33,
    SUM(Uriage) OVER (PARTITION BY parentId, primaryDay) AS item34,
    SUM(Henpin) OVER (PARTITION BY parentId, primaryDay) AS item35,
    SUM(Nebiki) OVER (PARTITION BY parentId, primaryDay) AS item36,
    SUM(Sonota) OVER (PARTITION BY parentId, primaryDay) AS item37,
    SUM(tax) OVER (PARTITION BY parentId, primaryDay) AS item38,
    SUM(TotalSales) OVER (PARTITION BY parentId, primaryDay) AS item39,
    SUM(carryOver) OVER (PARTITION BY parentId, primaryDay) AS item40,
    SUM(netSales) OVER (PARTITION BY parentId, primaryDay) AS item41,
    MAX(secondaryDay) OVER (PARTITION BY parentId, primaryDay) AS item42
FROM rows
ORDER BY {orderBy}";
}
