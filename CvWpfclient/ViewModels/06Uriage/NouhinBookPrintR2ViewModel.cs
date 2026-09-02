using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Newtonsoft.Json;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>納品書・適格返還請求書印刷。</summary>
public partial class NouhinBookPrintViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "納品書印刷";
	protected override string FormFileName => "NouhinBookPrint.qfm";

	public IReadOnlyList<TransactionCategoryOption> TransactionCategories { get; } = [
		new("全部", 10, 39), new("売上", 10, 19), new("返品", 20, 29), new("値引", 30, 39),
	];
	public IReadOnlyList<SlipFormTypeOption> SlipFormTypes { get; } = [new(1, "1 自社伝票")];

	[ObservableProperty] public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");
	[ObservableProperty] public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");
	[ObservableProperty] public partial string TokuiCodeFrom { get; set; } = string.Empty;
	[ObservableProperty] public partial string TokuiCodeTo { get; set; } = string.Empty;
	[ObservableProperty] public partial string DenNoFrom { get; set; } = string.Empty;
	[ObservableProperty] public partial string DenNoTo { get; set; } = string.Empty;
	[ObservableProperty] public partial bool IsPendingOnly { get; set; } = true;
	[ObservableProperty] public partial TransactionCategoryOption SelectedTransactionCategory { get; set; } = new("全部", 10, 39);
	[ObservableProperty] public partial SlipFormTypeOption SelectedSlipFormType { get; set; } = new(1, "1 自社伝票");

	[RelayCommand] void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;
	[RelayCommand] void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Print(CancellationToken ct) {
		if (!TryGetTerm(out var from, out var to)) return;
		try {
			ClientLib.Cursor2Wait();
			var targets = await FetchTargetsAsync(from, to, ct);
			if (targets.Count == 0) {
				Message = "指定した条件に一致する伝票がありません。売上日・得意先・伝票NO・取引区分と「未発行のみ」の指定を確認してください。";
				MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
				return;
			}
			if (!ValidateTaxRates(targets)) return;

			var update = new PartialUpdateParam(typeof(Tran00Uriage), [nameof(Tran00Uriage.IsPrint)],
				[.. targets.Select(x => new PartialUpdateRow(x.Id, x.Vdu, ["1"]))]);
			var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(PartialUpdateParam),
				DataMsg = Common.SerializeObject(update),
			}, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				Message = $"発行済み更新に失敗しました: {reply.DataMsg}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			await RunPrintPdfAsync(FormFileName, null, BuildPrintSqlParam(targets), ct);
		}
		catch (OperationCanceledException) {
			Message = "印刷をキャンセルしました";
		}
		catch (Exception ex) {
			Message = $"印刷に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) =>
		Task.FromResult<QueryListSqlParam?>(null);

	bool TryGetTerm(out DateTime from, out DateTime to) {
		to = default;
		if (!TryParseDate(DenDayFrom, out from) || !TryParseDate(DenDayTo, out to)) return false;
		if (from <= to) return true;
		MessageEx.ShowWarningDialog("売上日の範囲が逆転しています。", owner: ActiveWindow);
		return false;
	}

	bool ValidateTaxRates(IEnumerable<Tran00Uriage> targets) {
		var invalid = targets.FirstOrDefault(x => x.Jmeisai is null || x.Jmeisai.Count == 0 || x.Jmeisai.Any(m => m.TaxRate is not (0 or 8 or 10)));
		if (invalid is null) return true;
		Message = $"伝票NO {invalid.Id} の税率スナップショットが不正です。明細消費税を再更新してから印刷してください。";
		MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
		return false;
	}

	async Task<List<Tran00Uriage>> FetchTargetsAsync(DateTime from, DateTime to, CancellationToken ct) {
		List<string> parameters = [];
		var where = $"DenDay >= {AddSqlParameter(parameters, ToDenDay(from))} AND DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, "ifnull(json_extract(VTokui,'$.Cd'),'')", TokuiCodeFrom, TokuiCodeTo);
		where += $" AND Kubun BETWEEN {SelectedTransactionCategory.KubunFrom} AND {SelectedTransactionCategory.KubunTo}";
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) where += $" AND Id >= {noFrom}";
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) where += $" AND Id <= {noTo}";
		if (IsPendingOnly) where += " AND ifnull(IsPrint,0) = 0";

		var param = new QueryListParam(typeof(Tran00Uriage), where, "Id", [.. parameters]);
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Code = 0, Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param),
		}, AppGlobal.GetDefaultCallContext(ct));
		// 0件は NotFound(-1) として返る。障害と区別せず投げると DataMsg の "[]" が
		// そのままエラーメッセージになるため、0件は空リストとして呼び元へ返す。
		if (reply.Code == CvMsgErrorCode.NotFound) return [];
		if (reply.Code < 0) throw new InvalidOperationException(string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option);
		return JsonConvert.DeserializeObject<List<Tran00Uriage>>(reply.DataMsg) ?? [];
	}

	QueryListSqlParam BuildPrintSqlParam(IReadOnlyList<Tran00Uriage> targets) {
		var parameters = new List<string>();
		var ids = AddSqlParameter(parameters, Common.SerializeObject(targets.Select(x => x.Id).ToArray()));
		const string taxable10 = "(case when tr.rate1=10 then h.TaxableAmount1 else 0 end + case when tr.rate2=10 then h.TaxableAmount2 else 0 end + case when tr.rate3=10 then h.TaxableAmount3 else 0 end)";
		const string taxable8 = "(case when tr.rate1=8 then h.TaxableAmount1 else 0 end + case when tr.rate2=8 then h.TaxableAmount2 else 0 end + case when tr.rate3=8 then h.TaxableAmount3 else 0 end)";
		const string tax10Stored = "(case when tr.rate1=10 then h.Tax1 else 0 end + case when tr.rate2=10 then h.Tax2 else 0 end + case when tr.rate3=10 then h.Tax3 else 0 end)";
		const string tax8Stored = "(case when tr.rate1=8 then h.Tax1 else 0 end + case when tr.rate2=8 then h.Tax2 else 0 end + case when tr.rate3=8 then h.Tax3 else 0 end)";
		var tax10 = BuildTaxExpression(taxable10, tax10Stored, 10);
		var tax8 = BuildTaxExpression(taxable8, tax8Stored, 8);
		var kubunLabel = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"));
		var sql = $@"
WITH selected AS (SELECT cast(value as integer) AS Id FROM json_each({ids})),
tax_rates AS (
    SELECT h.Id,
        MAX(CASE WHEN cast(json_extract(m.value,'$.Id_Tax') as integer)=1 THEN cast(json_extract(m.value,'$.TaxRate') as integer) END) AS rate1,
        MAX(CASE WHEN cast(json_extract(m.value,'$.Id_Tax') as integer)=2 THEN cast(json_extract(m.value,'$.TaxRate') as integer) END) AS rate2,
        MAX(CASE WHEN cast(json_extract(m.value,'$.Id_Tax') as integer)=3 THEN cast(json_extract(m.value,'$.TaxRate') as integer) END) AS rate3
    FROM Tran00Uriage h JOIN selected x ON x.Id=h.Id
    JOIN json_each(CASE WHEN json_valid(h.Jmeisai) THEN h.Jmeisai ELSE '[]' END) m
    GROUP BY h.Id
)
SELECT
  /* RawExecCmd は SELECT 列名を Dictionary キーにするため、全列に一意な item 別名を付ける。
     別名が無いと '' や h.DenDay のような重複式でキー衝突し、行の材料化が ArgumentException で失敗する。
     item 番号は NouhinBookPrint.qfm の datarecord item1..item66 と 1:1 で対応させる。 */
  {TranMeisaiSql.HeaderCode("VTokui")} AS item1, {TranMeisaiSql.HeaderName("VTokui")} AS item2, {TranMeisaiSql.HeaderCode("VTokui")} AS item3, cast(h.Id as text) AS item4, {TranMeisaiSql.Num("No")} AS item5,
  h.DenDay AS item6, h.KakeDay AS item7, h.DenDay AS item8, h.Rate AS item9, {TranMeisaiSql.HeaderCode("VShain")} AS item10, {TranMeisaiSql.HeaderName("VShain")} AS item11, '' AS item12, {TranMeisaiSql.HeaderName("VShain")} AS item13,
  h.CalcFlag*h.SuTotal AS item14, h.CalcFlag*h.KingakuTotal AS item15, h.CalcFlag*h.JodaiTotal AS item16, h.CalcFlag*(h.Tax1+h.Tax2+h.Tax3) AS item17, h.CalcFlag*CASE WHEN h.Total!=0 THEN h.Total ELSE h.KingakuTotal+h.Tax1+h.Tax2+h.Tax3 END AS item18, ifnull(h.Memo,'') AS item19,
  {TranMeisaiSql.Str("Code_Shohin")} AS item20, {TranMeisaiSql.Str("Mei_Shohin")} AS item21, {TranMeisaiSql.Str("Code_Col")} AS item22, {TranMeisaiSql.Str("Mei_Col")} AS item23, {TranMeisaiSql.Str("Code_Siz")} AS item24, {TranMeisaiSql.Str("Mei_Siz")} AS item25,
  h.CalcFlag*{TranMeisaiSql.Num("Su")} AS item26, {TranMeisaiSql.Num("Tanka")} AS item27, h.CalcFlag*{TranMeisaiSql.Num("Kingaku")} AS item28, {TranMeisaiSql.Num("Jodai")} AS item29, h.CalcFlag*{TranMeisaiSql.Num("Su")}*{TranMeisaiSql.Num("Jodai")} AS item30, {TranMeisaiSql.Num("No")} AS item31, '' AS item32, '' AS item33, '' AS item34, '' AS item35,
  ifnull(sys.Name,'') AS item36, ifnull(sys.PostalCode,'') AS item37, trim(ifnull(sys.Address1,'') || ifnull(sys.Address2,'') || ifnull(sys.Address3,'')) AS item38, ifnull(sys.Tel,'') AS item39, '' AS item40,
  CASE WHEN h.Kubun BETWEEN 20 AND 39 THEN '適格返還請求書' ELSE '納品伝票' END AS item41, ifnull(sys.TaxRegistrationNumber,'') AS item42,
  CASE WHEN {taxable10} != 0 THEN '10%対象' ELSE '' END AS item43, abs({taxable10}) AS item44, ifnull(tokui.PostalCode,'') AS item45, abs({tax10}) AS item46,
  CASE WHEN {taxable8} != 0 THEN '8%対象 *' ELSE '' END AS item47, abs({taxable8}) AS item48, ifnull(tokui.Tel,'') AS item49, '' AS item50, trim(ifnull(tokui.Address1,'') || ifnull(tokui.Address2,'') || ifnull(tokui.Address3,'')) AS item51, abs({tax8}) AS item52,
  {TranMeisaiSql.HeaderName("VSoko")} AS item53, ifnull(soko.PostalCode,'') AS item54, trim(ifnull(soko.Address1,'') || ifnull(soko.Address2,'') || ifnull(soko.Address3,'')) AS item55, ifnull(soko.Tel,'') AS item56, '' AS item57,
  CASE WHEN abs(h.KingakuTotal)-abs({taxable10})-abs({taxable8}) != 0 THEN '非課税' ELSE '' END AS item58, {kubunLabel} AS item59, ifnull(h.ManualNo,'') AS item60, abs(h.KingakuTotal)-abs({taxable10})-abs({taxable8}) AS item61, '※は軽減税率対象商品' AS item62,
  {TranMeisaiSql.Num("TaxRate")} AS item63, {TranMeisaiSql.Num("Jodai")} AS item64, {TranMeisaiSql.Num("No")} AS item65, 0 AS item66
FROM Tran00Uriage h
JOIN selected x ON x.Id=h.Id
JOIN tax_rates tr ON tr.Id=h.Id
JOIN json_each(CASE WHEN json_valid(h.Jmeisai) THEN h.Jmeisai ELSE '[]' END) m
LEFT JOIN MasterSysman sys ON sys.Id = 1
LEFT JOIN MasterTokui tokui ON tokui.Id=h.Id_Tokui
LEFT JOIN MasterTokui soko ON soko.Id=h.Id_Soko
ORDER BY h.Id, {TranMeisaiSql.Num("No")}";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	static string BuildTaxExpression(string taxable, string storedTax, int rate) => $@"CASE WHEN h.TaxCalcUnit=0 THEN
  CASE h.TaxRounding WHEN 1 THEN cast((abs({taxable})*{rate}+99)/100 as integer) WHEN 2 THEN cast(abs({taxable})*{rate}/100 as integer) ELSE cast((abs({taxable})*{rate}+50)/100 as integer) END
  ELSE abs({storedTax}) END";
}

public sealed record TransactionCategoryOption(string Name, int KubunFrom, int KubunTo);
public sealed record SlipFormTypeOption(int Value, string Name);
