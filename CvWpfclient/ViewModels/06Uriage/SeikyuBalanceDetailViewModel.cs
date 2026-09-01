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
		// 税区分1-3のうち、指定税率(10/8)に該当する列だけを足し合わせる式を作る。
		// 該当するIdが無ければ "0"（その税率の内訳が無い＝0円）。
		static string SumForRate(string columnPrefix, int[] rates, int targetRate) {
			List<string> parts = [];
			for (var n = 1; n <= 3; n++) {
				if (rates[n - 1] == targetRate) parts.Add($"{columnPrefix}{n}");
			}
			return parts.Count > 0 ? string.Join(" + ", parts) : "0";
		}
		var taxable10Expr = SumForRate("s.TaxableAmount", rates, 10);
		var tax10Expr = SumForRate("s.Tax", rates, 10);
		var taxable8Expr = SumForRate("s.TaxableAmount", rates, 8);
		var tax8Expr = SumForRate("s.Tax", rates, 8);

		const string UriageKingaku = "CASE WHEN u.Total != 0 THEN u.Total ELSE u.KingakuTotal + (u.Tax1+u.Tax2+u.Tax3) END";
		var activeOnly = IsActiveOnly ? "AND (s.TotalSales != 0 OR s.Balance != 0)" : "";
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
        s.DenDay AS seikyuDay, s.DayFrom AS dayFrom, s.DayTo AS dayTo,
        s.Balance + s.TotalSales - s.TotalIn AS prevBalance,
        s.TotalSales AS totalSales,
        s.TotalIn    AS totalIn,
        (s.Tax1 + s.Tax2 + s.Tax3) AS tax,
        s.Balance    AS balance,
        s.SeikyuNo   AS seikyuNo,
        ({taxable10Expr}) AS taxable10,
        ({tax10Expr}) AS tax10,
        ({taxable8Expr}) AS taxable8,
        ({tax8Expr}) AS tax8,
        (s.TotalSales - (s.Tax1 + s.Tax2 + s.Tax3) - (s.TaxableAmount1 + s.TaxableAmount2 + s.TaxableAmount3)) AS taxExempt
    FROM SummaryUriSei s
    JOIN MasterTokui t ON t.Id = s.Id_Tokui
    WHERE s.DenDay = {seikyuDay}
      {activeOnly}{tokuiWhere}
),";

		if (!await ValidateTaxBreakdownAsync(seikyuDay, activeOnly, tokuiWhere, rates, parameters, ct)) {
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
    h.taxable10,
    h.tax10,
    h.taxable8,
    h.tax8,
    h.taxExempt
FROM headers h
LEFT JOIN details d ON d.idTokui = h.Id_Tokui
ORDER BY h.tokuiCode, d.denDay, d.srcOrder, d.denNo";

		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

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
