using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 棚卸チェックリスト。指定期間・倉庫の棚卸入力伝票(Tran60Tana)を明細1行=1レコードで印字し、
/// 入力内容の目視突合に使う。棚番(TanaNo)単位で入力するため棚番順に並べる。
/// 棚卸差異の突合は棚卸差異問合せ(Phase 9)が担当する。こちらは入力そのものの確認用。
/// </summary>
public partial class StockCheckListViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "棚卸チェックリスト";
	protected override string FormFileName => "StockCheckList.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TanaNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TanaNoTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	/// <summary>出力対象。true=数量0を除く / false=全て。</summary>
	[ObservableProperty]
	public partial bool ExcludeZero { get; set; }

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
			MessageEx.ShowWarningDialog("棚卸日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VSoko"), SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, "ifnull(h.TanaNo,'')", TanaNoFrom, TanaNoTo);
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.Str("Code_Shohin"), ShohinCodeFrom, ShohinCodeTo);
		if (ExcludeZero) {
			where += $" AND {TranMeisaiSql.Num("Su")} != 0";
		}

		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("h.DenDay")} AS denDayLabel,
    {TranMeisaiSql.HeaderCode("VSoko")}   AS sokoCode,
    {TranMeisaiSql.HeaderName("VSoko")}   AS sokoName,
    ifnull(h.TanaNo,'')                   AS tanaNo,
    h.Id                                  AS denNo,
    {TranMeisaiSql.Str("Code_Shohin")}    AS shohinCode,
    {TranMeisaiSql.Str("Mei_Shohin")}     AS shohinName,
    {TranMeisaiSql.Str("Mei_Col")}        AS colName,
    {TranMeisaiSql.Str("Mei_Siz")}        AS sizName,
    {TranMeisaiSql.Num("Su")}             AS su,
    {TranMeisaiSql.Num("Tanka")}          AS tanka,
    {TranMeisaiSql.Num("Kingaku")}        AS kingaku
FROM Tran60Tana h, {TranMeisaiSql.From}
WHERE {TranMeisaiSql.Guard}
  AND {where}
ORDER BY h.DenDay, sokoCode, tanaNo, h.Id, {TranMeisaiSql.Num("No")}";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
