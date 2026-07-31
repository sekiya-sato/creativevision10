using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 品番別仕入チェックリスト。指定期間の仕入伝票明細を品番(商品×色×サイズ)別に集計し、
/// 数量・金額・上代・伝票数・最終仕入日・平均単価を印字する。
/// 仕入返品(Kubun=20)は数量・金額がマイナス計上されるため、含める/除外するを選べる。
/// </summary>
public partial class HinbanShiireCheckListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "品番別仕入チェックリスト";
	protected override string FormFileName => "HinbanShiireCheckList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=色サイズ別 / false=商品計（色サイズを潰す）。</summary>
	[ObservableProperty]
	public partial bool IsByColorSize { get; set; } = true;

	/// <summary>true=返品･値引も含める / false=仕入(Kubun=10)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	[RelayCommand]
	void SelectSokoCodeFrom() => SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoCodeTo() => SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectShohinCodeFrom() => ShohinCodeFrom = SelectShohinCode() ?? ShohinCodeFrom;

	[RelayCommand]
	void SelectShohinCodeTo() => ShohinCodeTo = SelectShohinCode() ?? ShohinCodeTo;

	/// <summary>倉庫選択ダイアログ(TenType=0)。</summary>
	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("仕入日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		if (!IncludeHenpin) {
			where += $" AND h.Kubun = {(int)EnumShiire.Shiire}";
		}

		// 色サイズを潰す場合は集計キーを空文字にして GROUP BY で1本にまとめる。
		var colCode = IsByColorSize ? TranMeisaiSql.Str("Code_Col") : "''";
		var colName = IsByColorSize ? TranMeisaiSql.Str("Mei_Col") : "''";
		var sizCode = IsByColorSize ? TranMeisaiSql.Str("Code_Siz") : "''";
		var sizName = IsByColorSize ? TranMeisaiSql.Str("Mei_Siz") : "''";

		var sql = $@"
WITH meisai AS (
    SELECT
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")}  AS shohinName,
        {colCode} AS colCode,
        {colName} AS colName,
        {sizCode} AS sizCode,
        {sizName} AS sizName,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        h.Id     AS denNo,
        h.DenDay AS denDay
    FROM Tran03Shiire h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
)
SELECT
    shohinCode, shohinName,
    colCode, colName,
    sizCode, sizName,
    SUM(su)                                  AS su,
    SUM(kingaku)                             AS kingaku,
    SUM(su * jodai)                          AS jodaiTotal,
    COUNT(DISTINCT denNo)                    AS denCount,
    {TranMeisaiSql.DateLabel("MAX(denDay)")} AS lastDay,
    CASE WHEN SUM(su) != 0
         THEN CAST(ROUND(CAST(SUM(kingaku) AS REAL) / SUM(su)) AS INTEGER)
         ELSE 0 END                          AS avgTanka
FROM meisai
GROUP BY shohinCode, shohinName, colCode, colName, sizCode, sizName
ORDER BY shohinCode, colCode, sizCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
