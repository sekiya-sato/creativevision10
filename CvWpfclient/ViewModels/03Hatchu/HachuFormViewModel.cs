using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 発注書。仕入先へ渡す発注書を、発注ヘッダ（発注日・伝票NO・仕入先・掛率・合計）と明細で構成して印字する。
/// 単票形式で、ヘッダ項目は各明細行に同じ値を繰り返した CSV を渡し、qfm 側でヘッダ領域と明細領域に振り分ける。
///
/// 自社情報（社名・住所・TEL）は MasterSysman(Id=1) から取得してヘッダに載せる。
/// </summary>
public partial class HachuFormViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "発注書";
	protected override string FormFileName => "HachuForm.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	/// <summary>true=発注(Kubun=10)のみ / false=返品･値引も含める。</summary>
	[ObservableProperty]
	public partial bool IsHachuOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("発注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) where += $" AND h.Id >= {noFrom}";
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) where += $" AND h.Id <= {noTo}";
		if (IsHachuOnly) where += " AND h.Kubun = 10";

		// item1..10 = ヘッダ（明細各行に同値を繰り返す） / item11..17 = 明細
		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("h.DenDay")}  AS denDayLabel,
    CAST(h.Id AS TEXT)                     AS denNoText,
    {TranMeisaiSql.HeaderCode("VShiire")}  AS shiireCode,
    {TranMeisaiSql.HeaderName("VShiire")}  AS shiireName,
    h.Rate                                 AS rate,
    h.SuTotal                              AS suTotal,
    h.KingakuTotal                         AS kingakuTotal,
    CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + h.Tax END AS total,
    ifnull((SELECT sy.Name FROM MasterSysman sy WHERE sy.Id = 1),'')  AS sysName,
    ifnull((SELECT sy.Tel  FROM MasterSysman sy WHERE sy.Id = 1),'')  AS sysTel,
    {TranMeisaiSql.Str("Code_Shohin")}     AS shohinCode,
    {TranMeisaiSql.Str("Mei_Shohin")}      AS shohinName,
    {TranMeisaiSql.Str("Mei_Col")}         AS colName,
    {TranMeisaiSql.Str("Mei_Siz")}         AS sizName,
    {TranMeisaiSql.Num("Su")}              AS su,
    {TranMeisaiSql.Num("Tanka")}           AS tanka,
    {TranMeisaiSql.Num("Kingaku")}         AS kingaku
FROM Tran13Hachu h, {TranMeisaiSql.From}
WHERE {TranMeisaiSql.Guard}
  AND {where}
ORDER BY h.Id, {TranMeisaiSql.Num("No")}";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
