using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 在庫強制調整実績表。在庫強制調整入力で登録した <see cref="Tran61Chosei"/>（区分=強制調整）を、
/// 倉庫・調整日範囲で一覧印字する。棚卸確定が作った調整(<see cref="EnumChosei.Tanaoroshi"/>)は対象外。
/// 調整理由(<see cref="Tran61Chosei.Id_Riyu"/>)は <see cref="MasterMeisho"/> の <c>CHR</c> 区分名で表示する。
/// <para>
/// 明細（SKU別調整数）は <see cref="Tran61Chosei.Jmeisai"/>（JSON）にあり SQL 展開できないため、
/// 本表は伝票単位（調整数計）で出す。SKU別の内訳は「在庫強制調整実績照会」画面で確認する。
/// 仕様は `Doc/spec/2026-08-18_F2_在庫強制調整入力_詳細設計.md` の follow-up を参照する。
/// </para>
/// </summary>
public partial class StockForceReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "在庫強制調整実績表";
	protected override string FormFileName => "StockForceReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddMonths(-3).ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[RelayCommand]
	void SelectSoko() =>
		SokoCode = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType IN (0,3,6)", "Code")?.Code ?? SokoCode;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("調整日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var sokoWhere = string.IsNullOrWhiteSpace(SokoCode)
			? string.Empty
			: $" AND soko.Code = {AddSqlParameter(parameters, SokoCode.Trim())}";

		// SELECT の列順は StockForceReport.qfm の item1..item8 と一致させる。
		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("h.DenDay")} AS denDayLabel,
    h.Id         AS denNo,
    soko.Code    AS sokoCode,
    soko.Name    AS sokoName,
    riyu.Name    AS riyuName,
    h.SuTotal    AS suTotal,
    TRIM(COALESCE(shain.Code, '') || ' ' || COALESCE(shain.Name, '')) AS shainLabel,
    h.Memo       AS memo
FROM {nameof(Tran61Chosei)} h
LEFT JOIN {nameof(MasterTokui)}  soko  ON soko.Id  = h.Id_Soko
LEFT JOIN {nameof(MasterMeisho)} riyu  ON riyu.Id  = h.Id_Riyu
LEFT JOIN {nameof(MasterShain)}  shain ON shain.Id = h.Id_Shain
WHERE h.Kubun = {(int)EnumChosei.Kyosei}
  AND h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
  {sokoWhere}
ORDER BY h.DenDay DESC, h.Id DESC";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
