using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// ブランド別仕入金額表。指定期間の仕入明細をブランド別・年月別に集計し、
/// 数量・仕入金額・上代金額・原価率・構成比を印字する。
///
/// ブランドは明細の商品Idから MasterShohin.Id_Brand を引いて判定する（伝票側にブランド列は無い）。
/// 原価率 = 仕入金額 ÷ 上代金額。構成比 = そのブランドの仕入金額 ÷ 全体の仕入金額。
/// </summary>
public partial class BrandShiireKingakuTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "ブランド別仕入金額表";
	protected override string FormFileName => "BrandShiireKingakuTable.qfm";

	[ObservableProperty]
	public partial string StartYearMonth { get; set; } = DateTime.Today.AddMonths(-5).ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>出力月数（1〜36）</summary>
	[ObservableProperty]
	public partial string MonthCountText { get; set; } = "6";

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=ブランド×年月 / false=ブランド計（期間合計）。</summary>
	[ObservableProperty]
	public partial bool IsByMonth { get; set; } = true;

	/// <summary>true=返品･値引も含める / false=仕入(Kubun=10)のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeHenpin { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	[RelayCommand]
	void SelectBrandCodeFrom() => BrandCodeFrom = SelectBrandCode() ?? BrandCodeFrom;

	[RelayCommand]
	void SelectBrandCodeTo() => BrandCodeTo = SelectBrandCode() ?? BrandCodeTo;

	/// <summary>ブランド選択ダイアログ(MasterMeisho の BRD 区分)。</summary>
	string? SelectBrandCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunBrand}'", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(StartYearMonth, out var start)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (!int.TryParse(MonthCountText.Trim(), out var months) || months < 1 || months > 36) {
			MessageEx.ShowWarningDialog("出力月数は 1〜36 で入力してください。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		var end = start.AddMonths(months - 1);
		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(start));
		var dayTo = AddSqlParameter(parameters, ToDenDay(new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month))));
		var shiireWhere = BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);
		var brandWhere = BuildCodeRangeWhere(parameters, "br.Code", BrandCodeFrom, BrandCodeTo);
		var kubunFilter = IncludeHenpin ? "" : $" AND h.Kubun = {(int)EnumShiire.Shiire}";

		// ブランド計のときは年月キーを潰して期間合計にする
		var ymKey = IsByMonth ? "substr(h.DenDay,1,6)" : "''";
		var ymLabel = IsByMonth
			? "substr(ym,1,4) || '/' || substr(ym,5,2)"
			: $"'{start:yyyy/MM}～{end:yyyy/MM}'";

		var sql = $@"
WITH meisai AS (
    SELECT
        {ymKey} AS ym,
        (SELECT s.Id_Brand FROM MasterShohin s
         WHERE s.Id = {TranMeisaiSql.Num("Id_Shohin")}) AS idBrand,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        h.Id AS denNo
    FROM Tran03Shiire h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}{kubunFilter}{shiireWhere}
),
agg AS (
    SELECT
        m.ym,
        br.Code AS brandCode,
        br.Name AS brandName,
        SUM(m.su)             AS su,
        SUM(m.kingaku)        AS kingaku,
        SUM(m.su * m.jodai)   AS jodaiTotal,
        COUNT(DISTINCT m.denNo) AS denCount
    FROM meisai m
    JOIN MasterMeisho br ON br.Id = m.idBrand AND br.Kubun = '{MasterMeisho.KubunBrand}'
    WHERE 1=1 {brandWhere}
    GROUP BY m.ym, br.Code, br.Name
)
SELECT
    {ymLabel} AS ymLabel,
    brandCode, brandName,
    su, kingaku, jodaiTotal,
    CASE WHEN jodaiTotal != 0
         THEN ROUND(CAST(kingaku AS REAL) / jodaiTotal * 100, 1)
         ELSE 0 END AS genkaRatio,
    denCount,
    SUM(kingaku) OVER (PARTITION BY ym) AS ymTotal,
    CASE WHEN SUM(kingaku) OVER (PARTITION BY ym) != 0
         THEN ROUND(CAST(kingaku AS REAL) / SUM(kingaku) OVER (PARTITION BY ym) * 100, 1)
         ELSE 0 END AS shareRatio
FROM agg
ORDER BY ym, brandCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
