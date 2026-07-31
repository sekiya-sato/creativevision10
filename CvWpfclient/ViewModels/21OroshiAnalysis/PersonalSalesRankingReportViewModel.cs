using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._21OroshiAnalysis;

/// <summary>
/// 個人売上ランキング表。社員別の売上を順位付けし、担当得意先数・客単価・予算・予算比・構成比を印字する。
///
/// 実績の集計軸を2通り選べる:
///   - 営業担当（卸）: 得意先マスタの営業担当で卸売上(Tran00Uriage)を集計する
///   - 販売員（店舗）: 店舗売上(Tran01Tenuri)の明細担当社員で集計する。明細が未設定なら伝票の入力社員
/// 卸と店舗で「誰の売上か」の決まり方が違うため混ぜず、どちらかを選ぶ形にしている。
/// </summary>
public partial class PersonalSalesRankingReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "個人売上ランキング表";
	protected override string FormFileName => "PersonalSalesRankingReport.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/01");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShainCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShainCodeTo { get; set; } = string.Empty;

	/// <summary>true=営業担当(卸売上) / false=販売員(店舗売上)。</summary>
	[ObservableProperty]
	public partial bool IsByOroshiTanto { get; set; } = true;

	/// <summary>順位付けの基準。true=金額順 / false=数量順。</summary>
	[ObservableProperty]
	public partial bool IsByKingaku { get; set; } = true;

	/// <summary>出力対象。true=実績がある社員のみ / false=全社員。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	[RelayCommand]
	void SelectShainCodeFrom() => ShainCodeFrom = SelectShainCode() ?? ShainCodeFrom;

	[RelayCommand]
	void SelectShainCodeTo() => ShainCodeTo = SelectShainCode() ?? ShainCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("期間の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var dayFrom = AddSqlParameter(parameters, ToDenDay(from));
		var dayTo = AddSqlParameter(parameters, ToDenDay(to));
		var shainWhere = BuildCodeRangeWhere(parameters, "sn.Code", ShainCodeFrom, ShainCodeTo);

		var orderKey = IsByKingaku ? "kingaku" : "su";
		var join = IsActiveOnly ? "JOIN" : "LEFT JOIN";

		// 卸=得意先の営業担当で集計 / 店舗=明細担当（未設定なら伝票の入力社員）で集計
		var actualCte = IsByOroshiTanto ? $@"
actual AS (
    SELECT
        t.Id_Shain AS idShain,
        COUNT(*)            AS denCount,
        SUM(h.SuTotal)      AS su,
        SUM(h.KingakuTotal) AS kingaku,
        COUNT(DISTINCT h.Id_Tokui) AS toriCount
    FROM Tran00Uriage h
    JOIN MasterTokui t ON t.Id = h.Id_Tokui AND t.TenType = 1
    WHERE h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY t.Id_Shain
)" : $@"
actual AS (
    SELECT
        COALESCE(NULLIF({TranMeisaiSql.Num("Id_Shain")}, 0), h.Id_Shain) AS idShain,
        COUNT(DISTINCT h.Id)              AS denCount,
        SUM({TranMeisaiSql.Num("Su")})      AS su,
        SUM({TranMeisaiSql.Num("Kingaku")}) AS kingaku,
        COUNT(DISTINCT h.Id_Tenpo)        AS toriCount
    FROM Tran01Tenuri h, {TranMeisaiSql.From}
    WHERE {TranMeisaiSql.Guard}
      AND h.DenDay >= {dayFrom} AND h.DenDay <= {dayTo}
    GROUP BY idShain
)";

		var sql = $@"
WITH shains AS (
    SELECT sn.Id, sn.Code, sn.Name FROM MasterShain sn
    WHERE 1=1 {shainWhere}
),{actualCte},
budget AS (
    SELECT Id_Shain AS idShain, SUM(UriYosan) AS yosan
    FROM MasterYosanHanbai
    WHERE DenDay >= {dayFrom} AND DenDay <= {dayTo}
    GROUP BY Id_Shain
),
agg AS (
    SELECT
        s.Code AS shainCode, s.Name AS shainName,
        ifnull(a.toriCount, 0) AS toriCount,
        ifnull(a.denCount, 0)  AS denCount,
        ifnull(a.su, 0)        AS su,
        ifnull(a.kingaku, 0)   AS kingaku,
        ifnull(b.yosan, 0)     AS yosan
    FROM shains s
    {join} actual a ON a.idShain = s.Id
    LEFT JOIN budget b ON b.idShain = s.Id
),
ranked AS (
    SELECT
        g.*,
        ROW_NUMBER() OVER (ORDER BY {orderKey} DESC) AS rank,
        SUM(kingaku) OVER ()                         AS grandTotal
    FROM agg g
)
SELECT
    rank,
    shainCode, shainName,
    toriCount, denCount, su, kingaku,
    CASE WHEN denCount != 0
         THEN CAST(ROUND(CAST(kingaku AS REAL) / denCount) AS INTEGER)
         ELSE 0 END AS kyakuTanka,
    yosan,
    CASE WHEN yosan != 0 THEN ROUND(CAST(kingaku AS REAL) / yosan * 100, 1) ELSE 0 END AS yosanRatio,
    CASE WHEN grandTotal != 0 THEN ROUND(CAST(kingaku AS REAL) / grandTotal * 100, 1) ELSE 0 END AS shareRatio
FROM ranked
ORDER BY rank";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
