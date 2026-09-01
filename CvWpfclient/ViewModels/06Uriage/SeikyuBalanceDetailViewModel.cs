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
/// SummaryUriSei は対象期間のみの集計（繰越なし）。前回残高(prevBalance)は、対象期間の開始
/// (DayFrom)より前の全行を SUM(TotalSales - TotalIn) で積んで都度算出する（PreviousBalance、
/// `Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 2.3）。
/// 当月残高(balance)は PreviousBalance + Balance。
///
/// 明細1行=CSV1行で、ヘッダ項目は各行に同じ値を繰り返す。qfm 側でヘッダ領域と明細領域に
/// 振り分ける前提（CSV入力のフォームで単票を作る際の定石）。
/// </summary>
public partial class SeikyuBalanceDetailViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "請求書印刷";
	protected override string FormFileName => IsMeisaiUnit
		? "SeikyuBalanceDetailMeisai.qfm"
		: "SeikyuBalanceDetailDenpyo.qfm";

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

	/// <summary>false=伝票単位 / true=商品CD単位（旧cvnetの明細単位）。</summary>
	[ObservableProperty]
	public partial bool IsMeisaiUnit { get; set; }

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	protected override async Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(SeikyuDay, out var day)) {
			return null;
		}
		ct.ThrowIfCancellationRequested();

		// 税区分(Id_Tax 1-3)→表示税率(10%/8%/非課税)の対応は、この請求締日(DayTo)時点の
		// MasterSysTax で1回だけ解決する（`Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` D-05）。
		// 判定は CvDomainLogic/SummaryDb.cs と同じ TaxRateResolver.ResolveTaxRatePercent に揃えており、
		// マッピングをこの1箇所へまとめている（CvWpfclient は CvDomainLogic を参照しないため、
		// TaxRateResolver 自体を CvBase へ移設して両側から同じ実装を呼べるようにした）。
		var sysman = (await CvWpfclient.Helpers.CoreServiceClient.QuerySqlListAsync<MasterSysman>(
			"SELECT * FROM MasterSysman ORDER BY Id LIMIT 1", [], ct)).FirstOrDefault();

		List<string> parameters = [];
		var seikyuDayValue = ToDenDay(day);
		var seikyuDay = AddSqlParameter(parameters, seikyuDayValue);
		var tokuiWhere = BuildCodeRangeWhere(parameters, "t.Code", TokuiCodeFrom, TokuiCodeTo);

		var rates = new[] {
			TaxRateResolver.ResolveTaxRatePercent(sysman, 1, seikyuDayValue),
			TaxRateResolver.ResolveTaxRatePercent(sysman, 2, seikyuDayValue),
			TaxRateResolver.ResolveTaxRatePercent(sysman, 3, seikyuDayValue),
		};
		// PreviousBalance は対象期間の開始(DayFrom)より前の全行を SUM(TotalSales - TotalIn) で積む
		// （設計書 2.3）。相関スカラサブクエリにしているのは、この式が headersCte（"s." エイリアス）と
		// ValidateTaxBreakdownAsync（"s." を取り除いた無エイリアスのWHERE断片）の両方から
		// 文字列置換だけで使い回せるようにするため。
		const string PrevBalanceExpr =
			"(SELECT ifnull(SUM(pb.TotalSales - pb.TotalIn),0) FROM SummaryUriSei pb WHERE pb.Id_Tokui = s.Id_Tokui AND pb.DayTo < s.DayFrom)";
		var activeOnly = IsActiveOnly ? $"AND (s.TotalSales != 0 OR ({PrevBalanceExpr} + s.Balance) != 0)" : "";
		var kubunLabel = TranMeisaiSql.KubunLabel("u.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));
		// 税率別内訳(taxable10/tax10/taxable8/tax8/taxExempt)は、明細JSONを丸め直さず
		// SummaryUriSei.Tax1/2/3・TaxableAmount1/2/3（請求期間で1回だけ丸め済み。3.4/3.5）をそのまま使う。
		// taxExempt は「請求書の課税対象額に含まれない金額」= 税抜売上合計 − 税区分1-3の課税対象額合計。
		var headersCte = $@"
headers AS (
    SELECT
        s.Id_Tokui AS Id_Tokui,
        t.Code AS tokuiCode, t.Name AS tokuiName,
        t.PostalCode AS tokuiPostalCode, t.Address1 AS tokuiAddress1, t.Address2 AS tokuiAddress2, t.Address3 AS tokuiAddress3,
        t.Id_Shain AS tokuiIdShain,
        s.DenDay AS seikyuDay, s.DayFrom AS dayFrom, s.DayTo AS dayTo,
        {PrevBalanceExpr} AS prevBalance,
        s.TotalSales AS totalSales,
        s.TotalIn    AS totalIn,
        s.Cash, s.Fee, s.Densai, s.Offset, s.Other,
        s.Uriage, s.Henpin, s.Nebiki, s.Sonota,
        (s.Tax1 + s.Tax2 + s.Tax3) AS tax,
        {PrevBalanceExpr} + s.Balance AS balance,
        s.SeikyuNo   AS seikyuNo,
        s.NyukinYoteiDay AS nyukinYoteiDay
    FROM SummaryUriSei s
    JOIN MasterTokui t ON t.Id = s.Id_Tokui
    WHERE s.DenDay = {seikyuDay}
      {activeOnly}{tokuiWhere}
),";

		if (!await ValidateTaxBreakdownAsync(seikyuDay, activeOnly, tokuiWhere, rates, parameters, ct)) {
			return null;
		}

		var saleRows = IsMeisaiUnit
			? BuildMeisaiRows(kubunLabel)
			: BuildDenpyoRows(kubunLabel);
		var nyukinRows = IncludeNyukin ? @"
    UNION ALL
    SELECT
        h.Id_Tokui AS idTokui, n.KakeDay AS denDay, 2 AS srcOrder, n.Id AS denNo,
        6 AS processKubun, 0 AS kubun, '入金' AS kubunText,
        0 AS su, 0 AS kingaku, 0 AS tax, ifnull(n.ManualNo,'') AS manualNo, ifnull(n.Memo,'') AS memo,
        1 AS lineNo, n.KingakuTotal AS payment, 0 AS taxable1, 0 AS taxable2, 0 AS taxable3,
        '' AS shohinCode, '' AS shohinName, 0 AS meisaiKingaku, 0 AS meisaiSu
    FROM headers h
    JOIN Tran06Nyukin n
      ON n.Id_Torisaki = h.Id_Tokui
     AND n.KakeDay >= h.dayFrom AND n.KakeDay <= h.dayTo" : string.Empty;

		var taxRateText = $@"CASE
    WHEN ifnull(d.processKubun,0) = 6 OR ifnull(d.kubun,0) >= 80 THEN ''
    WHEN ifnull(d.taxable1,0) != 0 THEN '{rates[0]}%'
    WHEN ifnull(d.taxable2,0) != 0 THEN '{rates[1]}%'
    WHEN ifnull(d.taxable3,0) != 0 THEN '{rates[2]}%'
    ELSE '' END";

		// 旧CRSのSELECT列順（d_sql.txt）をそのまま再現する。qfmはこのCSV順と内蔵スクリプトに依存する。
		var sql = $@"
WITH {headersCte}
details AS (
{saleRows}{nyukinRows}
),
sysman AS (
    SELECT Name, PostalCode, Address1, Address2, Address3, Tel, BankAccount1, BankAccount2, BankAccount3
    FROM MasterSysman ORDER BY Id LIMIT 1
)
SELECT
    h.Id_Tokui AS item1,
    h.seikyuDay AS item2,
    h.balance AS item3,
    (h.Cash+h.Fee+h.Densai+h.Other) AS item4,
    h.Uriage AS item5,
    (h.Henpin+h.Nebiki) AS item6,
    h.Sonota AS item7,
    h.tax AS item8,
    h.prevBalance AS item9,
    h.tokuiCode AS item10,
    h.tokuiCode AS item11,
    CASE WHEN substr(h.seikyuDay,7,2) = '99' THEN '末' ELSE CAST(CAST(substr(h.seikyuDay,7,2) AS INTEGER) AS TEXT) END AS item12,
    ifnull(d.denNo,0) AS item13,
    ifnull(d.processKubun,0) AS item14,
    ifnull(d.denDay,'') AS item15,
    ifnull(d.kubun,0) AS item16,
    ifnull(d.kubunText,'') AS item17,
    ifnull(d.su,0) AS item18,
    ifnull(d.kingaku,0) AS item19,
    ifnull(d.tax,0) AS item20,
    ifnull(d.manualNo,'') AS item21,
    ifnull(d.memo,'') AS item22,
    ifnull(d.lineNo,0) AS item23,
    ifnull(d.payment,0) AS item24,
    0 AS item25,
    ifnull(d.memo,'') AS item26,
    ifnull(c.Name,'') AS item27,
    ifnull(c.PostalCode,'') AS item28,
    ifnull(c.Address1,'') AS item29,
    ifnull(c.Address2,'') AS item30,
    ifnull(c.Address3,'') AS item31,
    ifnull(c.Tel,'') AS item32,
    '' AS item33,
    ifnull(c.BankAccount1,'') AS item34,
    ifnull(c.BankAccount2,'') AS item35,
    ifnull(c.BankAccount3,'') AS item36,
    h.Offset AS item37,
    h.seikyuNo AS item38,
    h.prevBalance-h.totalIn AS item39,
    h.balance AS item40,
    h.tokuiName AS item41,
    h.tokuiPostalCode AS item42,
    h.tokuiAddress1 AS item43,
    h.tokuiAddress2 AS item44,
    h.tokuiAddress3 AS item45,
    h.tokuiIdShain AS item46,
    '' AS item47,
    (h.Uriage-h.Henpin-h.Nebiki+h.Sonota) AS item48,
    h.totalSales AS item49,
    '' AS item50,
    h.seikyuNo AS item51,
    ifnull(d.shohinCode,'') AS item52,
    ifnull(d.shohinName,'') AS item53,
    ifnull(d.meisaiKingaku,0) AS item54,
    ifnull(d.denNo,0) AS item55,
    ifnull(d.meisaiSu,0) AS item56,
    0 AS item57,
    1 AS item58,
    h.seikyuNo AS item59,
    {taxRateText} AS item60,
    h.nyukinYoteiDay AS item61
FROM headers h
LEFT JOIN details d ON d.idTokui = h.Id_Tokui
LEFT JOIN sysman c ON 1=1
ORDER BY h.tokuiCode, h.seikyuDay, ifnull(d.denDay,''), ifnull(d.srcOrder,0), ifnull(d.denNo,0), ifnull(d.lineNo,0)";

		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	private static string BuildDenpyoRows(string kubunLabel) => $@"
    SELECT
        h.Id_Tokui AS idTokui, u.KakeDay AS denDay, 1 AS srcOrder, u.Id AS denNo,
        0 AS processKubun, u.Kubun AS kubun, {kubunLabel} AS kubunText,
        u.CalcFlag*u.SuTotal AS su, u.CalcFlag*u.KingakuTotal AS kingaku,
        u.CalcFlag*(u.Tax1+u.Tax2+u.Tax3) AS tax, ifnull(u.ManualNo,'') AS manualNo, ifnull(u.Memo,'') AS memo,
        1 AS lineNo, 0 AS payment, u.TaxableAmount1 AS taxable1, u.TaxableAmount2 AS taxable2, u.TaxableAmount3 AS taxable3,
        '' AS shohinCode, '' AS shohinName, 0 AS meisaiKingaku, 0 AS meisaiSu
    FROM headers h
    JOIN Tran00Uriage u
      ON u.Id_Tokui = h.Id_Tokui
     AND u.KakeDay >= h.dayFrom AND u.KakeDay <= h.dayTo";

	private static string BuildMeisaiRows(string kubunLabel) => $@"
    SELECT
        h.Id_Tokui AS idTokui, u.KakeDay AS denDay, 1 AS srcOrder, u.Id AS denNo,
        0 AS processKubun, u.Kubun AS kubun, {kubunLabel} AS kubunText,
        u.CalcFlag*u.SuTotal AS su, u.CalcFlag*u.KingakuTotal AS kingaku,
        u.CalcFlag*(u.Tax1+u.Tax2+u.Tax3) AS tax, ifnull(u.ManualNo,'') AS manualNo, ifnull(u.Memo,'') AS memo,
        {TranMeisaiSql.Num("No")} AS lineNo, 0 AS payment,
        u.TaxableAmount1 AS taxable1, u.TaxableAmount2 AS taxable2, u.TaxableAmount3 AS taxable3,
        {TranMeisaiSql.Str("Code_Shohin")} AS shohinCode,
        {TranMeisaiSql.Str("Mei_Shohin")} AS shohinName,
        u.CalcFlag*SUM({TranMeisaiSql.Num("Kingaku")}) AS meisaiKingaku,
        u.CalcFlag*SUM({TranMeisaiSql.Num("Su")}) AS meisaiSu
    FROM headers h
    JOIN Tran00Uriage u
      ON u.Id_Tokui = h.Id_Tokui
     AND u.KakeDay >= h.dayFrom AND u.KakeDay <= h.dayTo
    JOIN json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END) m
    GROUP BY
        h.Id_Tokui,
        u.KakeDay, u.Id, u.Kubun, u.CalcFlag, u.SuTotal, u.KingakuTotal,
        u.Tax1, u.Tax2, u.Tax3, u.ManualNo, u.Memo,
        u.TaxableAmount1, u.TaxableAmount2, u.TaxableAmount3,
        {TranMeisaiSql.Num("No")}, {TranMeisaiSql.Str("Code_Shohin")}, {TranMeisaiSql.Str("Mei_Shohin")}
    UNION ALL
    SELECT
        h.Id_Tokui, u.KakeDay, 1, u.Id,
        0, u.Kubun, {kubunLabel},
        u.CalcFlag*u.SuTotal, u.CalcFlag*u.KingakuTotal,
        u.CalcFlag*(u.Tax1+u.Tax2+u.Tax3), ifnull(u.ManualNo,''), ifnull(u.Memo,''),
        0, 0, u.TaxableAmount1, u.TaxableAmount2, u.TaxableAmount3,
        '', '', 0, 0
    FROM headers h
    JOIN Tran00Uriage u
      ON u.Id_Tokui = h.Id_Tokui
     AND u.KakeDay >= h.dayFrom AND u.KakeDay <= h.dayTo
    WHERE NOT EXISTS (SELECT 1 FROM json_each(CASE WHEN json_valid(u.Jmeisai) THEN u.Jmeisai ELSE '[]' END))";

	/// <summary>
	/// 適格請求書の税率別内訳（10%/8%/非課税）が、請求残の税額・課税対象額と食い違わないことを確認する。
	/// <para>
	/// 請求単位の伝票は明細 <c>Tax</c> が常に0のため（3.4）、旧来の「明細ごとに丸めた <c>Tax</c> を
	/// 税率でグルーピングして単純SUM」する検査は成立しない。新方式では内訳自体を
	/// <c>SummaryUriSei.Tax1/2/3</c>・<c>TaxableAmount1/2/3</c> を税区分ごとに振り分けて作るため
	/// （<see cref="BuildPrintSqlParamAsync"/>）、この検査で確かめるべきは「振り分け漏れが無いこと」――
	/// つまり <c>Tax1+Tax2+Tax3</c> が税率別内訳の合計と、<c>TaxableAmount1+2+3</c> が
	/// 税率別の課税対象額の合計と、それぞれ一致することである。1つでも税区分の解決税率が10%/8%の
	/// どちらでもなければ（想定外の税率改定など）その分だけ内訳から漏れ、ここで不一致として検出する。
	/// </para>
	/// </summary>
	private async Task<bool> ValidateTaxBreakdownAsync(string seikyuDay, string activeOnly, string tokuiWhere, int[] rates, List<string> parameters, CancellationToken ct) {
		static string SumForRate(string columnPrefix, int[] rates, int targetRate) {
			List<string> parts = [];
			for (var n = 1; n <= 3; n++) {
				if (rates[n - 1] == targetRate) parts.Add($"{columnPrefix}{n}");
			}
			return parts.Count > 0 ? string.Join(" + ", parts) : "0";
		}
		var tax10Expr = SumForRate("Tax", rates, 10);
		var tax8Expr = SumForRate("Tax", rates, 8);
		var taxable10Expr = SumForRate("TaxableAmount", rates, 10);
		var taxable8Expr = SumForRate("TaxableAmount", rates, 8);

		var sql = $@"
WHERE DenDay = {seikyuDay}
  {activeOnly.Replace("s.", string.Empty, StringComparison.Ordinal)}
  AND Id_Tokui IN (SELECT t.Id FROM MasterTokui t WHERE 1 = 1 {tokuiWhere})
  AND (
      (Tax1 + Tax2 + Tax3) <> ({tax10Expr}) + ({tax8Expr})
      OR (TaxableAmount1 + TaxableAmount2 + TaxableAmount3) <> ({taxable10Expr}) + ({taxable8Expr})
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
