using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 担当別展示会受注合計表。展示会受注を担当社員×展示会で集計し、件数・数量・金額・上代・構成比を印字する。
///
/// 展示会は商品マスタの展示会区分(MasterShohin.Id_Tenji → MasterMeisho の 'TNJ' 区分)で判定する。
/// 受注伝票側に展示会列は無いため、明細の商品から辿るしかない。
/// 1伝票に複数展示会の商品が混在する場合、明細単位でそれぞれの展示会へ振り分けられる。
///
/// 担当は明細の担当社員(Id_Shain)を優先し、未設定(0)の明細は伝票ヘッダの入力社員へ寄せる
/// （販売員予算表と同じ規約）。
/// </summary>
public partial class TantoTenjiJuchuGoukeiTableViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "担当別展示会受注合計表";
	protected override string FormFileName => "TantoTenjiJuchuGoukeiTable.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.AddMonths(-3).ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShainCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenjiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenjiCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=担当×展示会 / false=担当計。</summary>
	[ObservableProperty]
	public partial bool IsByTenji { get; set; } = true;

	/// <summary>true=展示会未設定の商品も含める / false=展示会が設定された商品のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeNoTenji { get; set; }

	[RelayCommand]
	void SelectShainCodeFrom() => ShainCodeFrom = SelectShainCode() ?? ShainCodeFrom;

	[RelayCommand]
	void SelectShainCodeTo() => ShainCodeTo = SelectShainCode() ?? ShainCodeTo;

	[RelayCommand]
	void SelectTenjiCodeFrom() => TenjiCodeFrom = SelectTenjiCode() ?? TenjiCodeFrom;

	[RelayCommand]
	void SelectTenjiCodeTo() => TenjiCodeTo = SelectTenjiCode() ?? TenjiCodeTo;

	/// <summary>展示会選択ダイアログ(MasterMeisho の TNJ 区分)。</summary>
	string? SelectTenjiCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunTenji}'", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("受注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ " AND h.Kubun = 10";
		var shainWhere = BuildCodeRangeWhere(parameters, "ifnull(sn.Code,'')", ShainCodeFrom, ShainCodeTo);
		var tenjiWhere = BuildCodeRangeWhere(parameters, "ifnull(tj.Code,'')", TenjiCodeFrom, TenjiCodeTo);
		// 展示会未設定を含めない場合は内部結合相当にする
		var tenjiFilter = IncludeNoTenji ? "" : " AND tj.Id IS NOT NULL";

		var tenjiCode = IsByTenji ? "ifnull(tj.Code,'(未設定)')" : "''";
		var tenjiName = IsByTenji ? "ifnull(tj.Name,'(未設定)')" : "'(担当計)'";

		var sql = $@"
WITH meisai AS (
    SELECT
        -- 明細担当が未設定(0)なら伝票ヘッダの入力社員へ寄せる
        COALESCE(NULLIF({TranMeisaiSql.Num("Id_Shain")}, 0), h.Id_Shain) AS idShain,
        (SELECT s.Id_Tenji FROM MasterShohin s
         WHERE s.Id = {TranMeisaiSql.Num("Id_Shohin")}) AS idTenji,
        {TranMeisaiSql.Num("Su")}      AS su,
        {TranMeisaiSql.Num("Kingaku")} AS kingaku,
        {TranMeisaiSql.Num("Jodai")}   AS jodai,
        h.Id AS denNo
    FROM Tran12Jyuchu h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND {where}
),
agg AS (
    SELECT
        ifnull(sn.Code,'(未設定)') AS shainCode,
        ifnull(sn.Name,'(未設定)') AS shainName,
        {tenjiCode} AS tenjiCode,
        {tenjiName} AS tenjiName,
        COUNT(DISTINCT m.denNo) AS denCount,
        SUM(m.su)               AS su,
        SUM(m.kingaku)          AS kingaku,
        SUM(m.su * m.jodai)     AS jodaiTotal
    FROM meisai m
    LEFT JOIN MasterShain sn  ON sn.Id = m.idShain
    LEFT JOIN MasterMeisho tj ON tj.Id = m.idTenji AND tj.Kubun = '{MasterMeisho.KubunTenji}'
    WHERE 1=1 {shainWhere}{tenjiWhere}{tenjiFilter}
    GROUP BY shainCode, shainName, {tenjiCode}, {tenjiName}
)
SELECT
    shainCode, shainName,
    tenjiCode, tenjiName,
    denCount, su, kingaku, jodaiTotal,
    SUM(kingaku) OVER (PARTITION BY shainCode) AS shainTotal,
    CASE WHEN SUM(kingaku) OVER (PARTITION BY shainCode) != 0
         THEN ROUND(CAST(kingaku AS REAL) / SUM(kingaku) OVER (PARTITION BY shainCode) * 100, 1)
         ELSE 0 END AS shareRatio
FROM agg
ORDER BY shainCode, tenjiCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
