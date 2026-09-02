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
				Message = "対象の伝票がありません";
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
		if (reply.Code < 0) throw new InvalidOperationException(reply.DataMsg);
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
  {TranMeisaiSql.HeaderCode("VTokui")}, {TranMeisaiSql.HeaderName("VTokui")}, {TranMeisaiSql.HeaderCode("VTokui")}, cast(h.Id as text), {TranMeisaiSql.Num("No")},
  h.DenDay, h.KakeDay, h.DenDay, h.Rate, {TranMeisaiSql.HeaderCode("VShain")}, {TranMeisaiSql.HeaderName("VShain")}, '', {TranMeisaiSql.HeaderName("VShain")},
  h.CalcFlag*h.SuTotal, h.CalcFlag*h.KingakuTotal, h.CalcFlag*h.JodaiTotal, h.CalcFlag*(h.Tax1+h.Tax2+h.Tax3), h.CalcFlag*CASE WHEN h.Total!=0 THEN h.Total ELSE h.KingakuTotal+h.Tax1+h.Tax2+h.Tax3 END, ifnull(h.Memo,''),
  {TranMeisaiSql.Str("Code_Shohin")}, {TranMeisaiSql.Str("Mei_Shohin")}, {TranMeisaiSql.Str("Code_Col")}, {TranMeisaiSql.Str("Mei_Col")}, {TranMeisaiSql.Str("Code_Siz")}, {TranMeisaiSql.Str("Mei_Siz")},
  h.CalcFlag*{TranMeisaiSql.Num("Su")}, {TranMeisaiSql.Num("Tanka")}, h.CalcFlag*{TranMeisaiSql.Num("Kingaku")}, {TranMeisaiSql.Num("Jodai")}, h.CalcFlag*{TranMeisaiSql.Num("Su")}*{TranMeisaiSql.Num("Jodai")}, {TranMeisaiSql.Num("No")}, '', '', '', '',
  ifnull(sys.Name,''), ifnull(sys.PostalCode,''), trim(ifnull(sys.Address1,'') || ifnull(sys.Address2,'') || ifnull(sys.Address3,'')), ifnull(sys.Tel,''), '',
  CASE WHEN h.Kubun BETWEEN 20 AND 39 THEN '適格返還請求書' ELSE '納品伝票' END, ifnull(sys.TaxRegistrationNumber,''),
  CASE WHEN {taxable10} != 0 THEN '10%対象' ELSE '' END, abs({taxable10}), ifnull(tokui.PostalCode,''), abs({tax10}),
  CASE WHEN {taxable8} != 0 THEN '8%対象 *' ELSE '' END, abs({taxable8}), ifnull(tokui.Tel,''), '', trim(ifnull(tokui.Address1,'') || ifnull(tokui.Address2,'') || ifnull(tokui.Address3,'')), abs({tax8}),
  {TranMeisaiSql.HeaderName("VSoko")}, ifnull(soko.PostalCode,''), trim(ifnull(soko.Address1,'') || ifnull(soko.Address2,'') || ifnull(soko.Address3,'')), ifnull(soko.Tel,''), '',
  CASE WHEN abs(h.KingakuTotal)-abs({taxable10})-abs({taxable8}) != 0 THEN '非課税' ELSE '' END, {kubunLabel}, ifnull(h.ManualNo,''), abs(h.KingakuTotal)-abs({taxable10})-abs({taxable8}), '※は軽減税率対象商品',
  {TranMeisaiSql.Num("TaxRate")}, {TranMeisaiSql.Num("Jodai")}, {TranMeisaiSql.Num("No")}, 0
FROM Tran00Uriage h
JOIN selected x ON x.Id=h.Id
JOIN tax_rates tr ON tr.Id=h.Id
JOIN json_each(CASE WHEN json_valid(h.Jmeisai) THEN h.Jmeisai ELSE '[]' END) m
LEFT JOIN MasterSysman sys ON 1=1
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
