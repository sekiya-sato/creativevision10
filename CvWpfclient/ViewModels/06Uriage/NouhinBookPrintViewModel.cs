using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Newtonsoft.Json;
using System.Windows;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 納品書印刷。卸売上伝票(Tran00Uriage)から納品書を出力する。
///
/// 出力は単票形式で、伝票ヘッダ（納品日・得意先・伝票NO・合計）を各明細行に繰り返した CSV を渡し、
/// qfm 側でヘッダ領域と明細領域へ振り分ける。
///
/// 発行済み管理は Tran00Uriage.IsPrint（0=未発行 / 1=発行済）で行う。
/// 印刷実行では自動で立てず、PDFを確認したうえで「発行済みにする」を明示的に実行する運用にしている。
/// 印刷が失敗・中断した伝票を発行済みにしてしまうと、未発行チェックリストから漏れて追跡できなくなるため。
/// </summary>
/// <summary>専用伝票の互換出力用。標準納品書とは列定義が異なるため分離する。</summary>
public partial class NouhinBookPrintLegacyViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "納品書印刷";
	protected override string FormFileName => "NouhinBookPrint.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	/// <summary>true=未発行(IsPrint=0)のみ / false=発行済みも含める。</summary>
	[ObservableProperty]
	public partial bool IsPendingOnly { get; set; } = true;

	[RelayCommand]
	void SelectTokuiCodeFrom() => TokuiCodeFrom = SelectTokuiCode() ?? TokuiCodeFrom;

	[RelayCommand]
	void SelectTokuiCodeTo() => TokuiCodeTo = SelectTokuiCode() ?? TokuiCodeTo;

	/// <summary>
	/// 抽出条件のWHERE句を組み立てる。ヘッダ別名は h。
	/// </summary>
	string BuildWhere(List<string> parameters, DateTime from, DateTime to) {
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VTokui"), TokuiCodeFrom, TokuiCodeTo);
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) {
			where += $" AND h.Id >= {noFrom}";
		}
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) {
			where += $" AND h.Id <= {noTo}";
		}
		if (IsPendingOnly) {
			where += " AND ifnull(h.IsPrint,0) = 0";
		}
		return where;
	}

	bool TryGetTerm(out DateTime from, out DateTime to) {
		to = default;
		if (!TryParseDate(DenDayFrom, out from) || !TryParseDate(DenDayTo, out to)) return false;
		if (from > to) {
			MessageEx.ShowWarningDialog("売上日の範囲が逆転しています。", owner: ActiveWindow);
			return false;
		}
		return true;
	}

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryGetTerm(out var from, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = BuildWhere(parameters, from, to);

		const string Kingaku = "CASE WHEN h.Total != 0 THEN h.Total ELSE h.KingakuTotal + (h.Tax1+h.Tax2+h.Tax3) END";
		var kubunLabel = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumUri00.Uriage, "売上"), ((int)EnumUri00.UriSale, "売上SALE"),
			((int)EnumUri00.Henpin, "返品"), ((int)EnumUri00.HenSale, "返品SALE"),
			((int)EnumUri00.Nebiki, "値引"), ((int)EnumUri00.Other, "その他"));

		// item1..9 = ヘッダ（明細各行に同値を繰り返す） / item10..16 = 明細
		var sql = $@"
SELECT
    {TranMeisaiSql.DateLabel("h.DenDay")}  AS denDayLabel,
    CAST(h.Id AS TEXT)                     AS denNoText,
    {TranMeisaiSql.HeaderCode("VTokui")}   AS tokuiCode,
    {TranMeisaiSql.HeaderName("VTokui")}   AS tokuiName,
    {kubunLabel}                           AS kubunText,
    h.SuTotal                              AS suTotal,
    h.KingakuTotal                         AS kingakuTotal,
    (h.Tax1+h.Tax2+h.Tax3)                  AS tax,
    {Kingaku}                              AS total,
    {TranMeisaiSql.Str("Code_Shohin")}     AS shohinCode,
    {TranMeisaiSql.Str("Mei_Shohin")}      AS shohinName,
    {TranMeisaiSql.Str("Mei_Col")}         AS colName,
    {TranMeisaiSql.Str("Mei_Siz")}         AS sizName,
    {TranMeisaiSql.Num("Su")}              AS su,
    {TranMeisaiSql.Num("Tanka")}           AS tanka,
    {TranMeisaiSql.Num("Kingaku")}         AS kingaku
FROM Tran00Uriage h, {TranMeisaiSql.From}
WHERE {TranMeisaiSql.Guard}
  AND {where}
ORDER BY h.Id, {TranMeisaiSql.Num("No")}";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}

	/// <summary>
	/// 抽出条件に一致する伝票の IsPrint を 1 にする。
	/// PDF を確認したあとに手動で実行する（印刷失敗分を発行済みにしないため）。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task MarkPrinted(CancellationToken ct) {
		if (!TryGetTerm(out var from, out var to)) return;

		try {
			ClientLib.Cursor2Wait();

			var targets = await FetchTargetsAsync(from, to, ct);
			if (targets.Count == 0) {
				Message = "対象の伝票がありません";
				MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
				return;
			}
			if (!ConfirmAction($"{targets.Count}件の伝票を発行済みにしますか？")) return;

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var updated = 0;
			foreach (var den in targets) {
				ct.ThrowIfCancellationRequested();
				den.IsPrint = 1;
				var msg = new CvMsg {
					Code = 0,
					Flag = CvFlag.Msg201_Op_Execute,
					DataType = typeof(UpdateParam),
					DataMsg = Common.SerializeObject(new UpdateParam(typeof(Tran00Uriage), Common.SerializeObject(den))),
				};
				var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
				if (reply.Code < 0) {
					Message = $"伝票NO {den.Id} の更新に失敗しました: {reply.DataMsg}";
					MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
					return;
				}
				updated++;
			}
			Message = $"{updated}件を発行済みにしました";
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "発行済み更新をキャンセルしました";
		}
		catch (Exception ex) {
			Message = $"発行済み更新に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>抽出条件に一致する伝票を取得する（IsPrint 更新対象）。</summary>
	async Task<List<Tran00Uriage>> FetchTargetsAsync(DateTime from, DateTime to, CancellationToken ct) {
		List<string> parameters = [];
		// QueryListParam の Where は h 別名を使えないため、同じ条件を別名なしで組み直す。
		var where = $"DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, "ifnull(json_extract(VTokui,'$.Cd'),'')", TokuiCodeFrom, TokuiCodeTo);
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) where += $" AND Id >= {noFrom}";
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) where += $" AND Id <= {noTo}";
		if (IsPendingOnly) where += " AND ifnull(IsPrint,0) = 0";

		// Where は "where" を含めない（QueryListParam.AddWhereOrder が付ける）
		var param = new QueryListParam(typeof(Tran00Uriage), where, "Id", [.. parameters]);
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param),
		};
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0) return [];
		return JsonConvert.DeserializeObject<List<Tran00Uriage>>(reply.DataMsg) ?? [];
	}
}
