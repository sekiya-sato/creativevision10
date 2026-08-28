using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodeShare;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 請求書印刷。得意先へ渡す請求書を、請求ヘッダ（前回残高・当月売上・当月入金・当月請求額）と
/// 対象期間の売上／入金明細で構成して印字する。
///
/// 請求ヘッダは集計テーブル SummaryUriSei（請求計算＝月次更新処理の成果物）を読む。
/// 対象期間は同テーブルの DayFrom〜DayTo。締め処理を回していない請求日は行が無く空になる。
/// 前回残高は当月残高から当月増減を戻して算出する（Balance + TotalSales - TotalIn）。
/// SummaryUriSei の当月残高は Balance = 前回残高 + TotalIn - TotalSales で作られるため、
/// 逆算は TotalSales を足し TotalIn を引く。符号を逆にすると当月増減を2回効かせてしまう。
///
/// 明細1行=CSV1行で、ヘッダ項目は各行に同じ値を繰り返す。qfm 側でヘッダ領域と明細領域に
/// 振り分ける前提（CSV入力のフォームで単票を作る際の定石）。
/// </summary>
public partial class SeikyuBalanceDetailViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "請求書印刷";
	protected override string FormFileName => "SeikyuBalanceDetail.qfm";

	[ObservableProperty]
	public partial string SeikyuDay { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>true=請求額または残高があるもののみ / false=全て。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	/// <summary>true=入金明細も印字 / false=売上明細のみ。</summary>
	[ObservableProperty]
	public partial bool IncludeNyukin { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override async Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(SeikyuDay, out var day)) {
			return null;
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var seikyuDay = AddSqlParameter(parameters, ToDenDay(day));
		var tokuiWhere = BuildCodeRangeWhere(parameters, "t.Code", TokuiCodeFrom, TokuiCodeTo);

		const string UriageKingaku = "CASE WHEN u.Total != 0 THEN u.Total ELSE u.KingakuTotal + u.Tax END";
		var activeOnly = IsActiveOnly ? "AND (s.TotalSales != 0 OR s.Balance != 0)" : "";
		var kubunLabel = TranMeisaiSql.KubunLabel("u.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));
		var headersCte = $@"
headers AS (
    SELECT
        s.Id_Tokui AS Id_Tokui,
        t.Code AS tokuiCode, t.Name AS tokuiName,
        s.DenDay AS seikyuDay, s.DayFrom AS dayFrom, s.DayTo AS dayTo,
        s.Balance + s.TotalSales - s.TotalIn AS prevBalance,
        s.TotalSales AS totalSales,
        s.TotalIn    AS totalIn,
        s.Tax        AS tax,
        s.Balance    AS balance,
        s.SeikyuNo   AS seikyuNo
    FROM SummaryUriSei s
    JOIN MasterTokui t ON t.Id = s.Id_Tokui
    WHERE s.DenDay = {seikyuDay}
      {activeOnly}{tokuiWhere}
),";

		if (!await ValidateTaxBreakdownAsync(seikyuDay, activeOnly, tokuiWhere, parameters, ct)) {
			return null;
		}

		// 入金明細を含めない場合は売上側だけを UNION 対象にする
		var nyukinPart = IncludeNyukin ? $@"
    UNION ALL
    SELECT
        h.Id_Tokui AS idTokui, n.KakeDay AS denDay, 2 AS srcOrder, n.Id AS denNo,
        '入金' AS kubunText, 0 AS su, -n.KingakuTotal AS kingaku
    FROM headers h
    JOIN Tran06Nyukin n
      ON n.Id_Torisaki = h.Id_Tokui
     AND n.KakeDay >= h.dayFrom AND n.KakeDay <= h.dayTo" : "";

		var sql = $@"
WITH {headersCte}
taxBreakdown AS (
    SELECT
        h.Id_Tokui,
        SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) = 10
            THEN CASE WHEN u.Kubun BETWEEN 20 AND 39 THEN -1 ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Kingaku'), 0) AS INTEGER) ELSE 0 END) AS taxable10,
        SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) = 10
            THEN CASE WHEN u.Kubun BETWEEN 20 AND 29 THEN u.CalcFlag ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Tax'), 0) AS INTEGER) ELSE 0 END) AS tax10,
        SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) = 8
            THEN CASE WHEN u.Kubun BETWEEN 20 AND 39 THEN -1 ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Kingaku'), 0) AS INTEGER) ELSE 0 END) AS taxable8,
        SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) = 8
            THEN CASE WHEN u.Kubun BETWEEN 20 AND 29 THEN u.CalcFlag ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Tax'), 0) AS INTEGER) ELSE 0 END) AS tax8,
        SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) = 0
            THEN CASE WHEN u.Kubun BETWEEN 20 AND 39 THEN -1 ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Kingaku'), 0) AS INTEGER) ELSE 0 END) AS taxExempt
    FROM headers h
    LEFT JOIN Tran00Uriage u
      ON u.Id_Tokui = h.Id_Tokui
     AND u.IsPay = 1
     AND u.KakeDay >= h.dayFrom AND u.KakeDay <= h.dayTo
    LEFT JOIN json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END) AS m
    GROUP BY h.Id_Tokui
),
details AS (
    SELECT
        h.Id_Tokui AS idTokui, u.KakeDay AS denDay, 1 AS srcOrder, u.Id AS denNo,
        {kubunLabel} AS kubunText, u.SuTotal AS su, {UriageKingaku} AS kingaku
    FROM headers h
    JOIN Tran00Uriage u
      ON u.Id_Tokui = h.Id_Tokui
     AND u.KakeDay >= h.dayFrom AND u.KakeDay <= h.dayTo{nyukinPart}
)
SELECT
    {TranMeisaiSql.DateLabel("h.seikyuDay")} AS seikyuDayLabel,
    h.tokuiCode, h.tokuiName,
    {TranMeisaiSql.DateLabel("h.dayFrom")} || '～' || {TranMeisaiSql.DateLabel("h.dayTo")} AS termLabel,
    h.prevBalance, h.totalSales, h.totalIn, h.tax, h.balance,
    {TranMeisaiSql.DateLabel("d.denDay")} AS denDayLabel,
    CAST(d.denNo AS TEXT) AS denNoText,
    d.kubunText,
    d.su,
    d.kingaku,
    h.seikyuNo,
    IFNULL(b.taxable10, 0) AS taxable10,
    IFNULL(b.tax10, 0) AS tax10,
    IFNULL(b.taxable8, 0) AS taxable8,
    IFNULL(b.tax8, 0) AS tax8,
    IFNULL(b.taxExempt, 0) AS taxExempt
FROM headers h
LEFT JOIN details d ON d.idTokui = h.Id_Tokui
LEFT JOIN taxBreakdown b ON b.Id_Tokui = h.Id_Tokui
ORDER BY h.tokuiCode, d.denDay, d.srcOrder, d.denNo";

		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	/// <summary>
	/// 適格請求書の税率別内訳を、請求対象の売上明細スナップショットから作れることを確認する。
	/// 明細欠落・未対応税率・集計値不一致のままPDFを出すと請求額と内訳が食い違うため、印刷前に止める。
	/// </summary>
	private async Task<bool> ValidateTaxBreakdownAsync(string seikyuDay, string activeOnly, string tokuiWhere, List<string> parameters, CancellationToken ct) {
		var sql = $@"
WHERE DenDay = {seikyuDay}
  {activeOnly.Replace("s.", string.Empty, StringComparison.Ordinal)}
  AND Id_Tokui IN (SELECT t.Id FROM MasterTokui t WHERE 1 = 1 {tokuiWhere})
  AND (
      TotalSales <> IFNULL((
          SELECT SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) IN (0, 8, 10)
              THEN CASE WHEN u.Kubun BETWEEN 20 AND 39 THEN -1 ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Kingaku'), 0) AS INTEGER) ELSE 0 END)
          FROM Tran00Uriage u
          LEFT JOIN json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END) AS m
          WHERE u.Id_Tokui = SummaryUriSei.Id_Tokui AND u.IsPay = 1 AND u.KakeDay BETWEEN SummaryUriSei.DayFrom AND SummaryUriSei.DayTo), 0)
          + IFNULL((
          SELECT SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) IN (0, 8, 10)
              THEN CASE WHEN u.Kubun BETWEEN 20 AND 29 THEN u.CalcFlag ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Tax'), 0) AS INTEGER) ELSE 0 END)
          FROM Tran00Uriage u
          LEFT JOIN json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END) AS m
          WHERE u.Id_Tokui = SummaryUriSei.Id_Tokui AND u.IsPay = 1 AND u.KakeDay BETWEEN SummaryUriSei.DayFrom AND SummaryUriSei.DayTo), 0)
      OR Tax <> IFNULL((
          SELECT SUM(CASE WHEN CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) IN (0, 8, 10)
              THEN CASE WHEN u.Kubun BETWEEN 20 AND 29 THEN u.CalcFlag ELSE 1 END * CAST(IFNULL(json_extract(m.value, '$.Tax'), 0) AS INTEGER) ELSE 0 END)
          FROM Tran00Uriage u
          LEFT JOIN json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END) AS m
          WHERE u.Id_Tokui = SummaryUriSei.Id_Tokui AND u.IsPay = 1 AND u.KakeDay BETWEEN SummaryUriSei.DayFrom AND SummaryUriSei.DayTo), 0)
      OR EXISTS (SELECT 1 FROM Tran00Uriage u LEFT JOIN json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END) AS m
          WHERE u.Id_Tokui = SummaryUriSei.Id_Tokui AND u.IsPay = 1 AND u.KakeDay BETWEEN SummaryUriSei.DayFrom AND SummaryUriSei.DayTo
            AND (NOT json_valid(u.Jmeisai) OR CAST(IFNULL(json_extract(m.value, '$.TaxRate'), 0) AS INTEGER) NOT IN (0, 8, 10)))
  )";

		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var request = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(SummaryUriSei), sql, [.. parameters])),
		};
		var reply = await coreService.QueryMsgAsync(request, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0 && reply.Code != -1) {
			var detail = string.IsNullOrWhiteSpace(reply.Option) ? string.Empty : $"{Environment.NewLine}{reply.Option}";
			MessageEx.ShowWarningDialog(
				"税率別内訳の印刷前検査に失敗しました。サーバー接続と対象伝票を確認してください。" + detail,
				owner: ActiveWindow);
			return false;
		}

		var invalidRows = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as List<SummaryUriSei> ?? [];
		if (invalidRows.Count == 0) return true;

		var summary = string.Join(Environment.NewLine, invalidRows.Take(5).Select(x =>
			$"請求書 {x.SeikyuNo}（得意先Id={x.Id_Tokui}）"));
		var suffix = invalidRows.Count > 5 ? $"{Environment.NewLine}ほか {invalidRows.Count - 5} 件" : string.Empty;
		MessageEx.ShowWarningDialog(
			"税率別内訳が請求集計と一致しないため印刷を中止しました。"
			+ $"{Environment.NewLine}明細消費税の再更新または対象伝票の修正後に再実行してください。{Environment.NewLine}{summary}{suffix}",
			owner: ActiveWindow);
		return false;
	}
}
