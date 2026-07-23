using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Grpc.Core;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 仕入伝票印刷。印刷範囲(仕入日/仕入先/倉庫/伝票NO/手入力NO/取引区分)を指定し、
/// ShiireSlipPrint.qfm へ SQL で明細1行=CSV1行のデータを渡して PDF 出力する。
/// レイアウトの標準は ShopBudgetReport(BaseViewModel 派生の印刷ダイアログ)に合わせる。
/// </summary>
public partial class ShiireSlipPrintViewModel : Helpers.BaseViewModel {

	/// <summary>取引区分の選択肢。Value=null は「全て」。</summary>
	public sealed record KubunOption(int? Value, string Name);

	[ObservableProperty]
	public partial string Title { get; set; } = "仕入伝票印刷";

	// 仕入日(DenDay)範囲
	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	// 仕入先コード範囲
	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	// 倉庫コード範囲
	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	// 伝票NO(Tran03Shiire.Id)範囲
	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	// 手入力NO(ManualNo)範囲
	[ObservableProperty]
	public partial string ManualNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ManualNoTo { get; set; } = string.Empty;

	// 取引区分(Kubun)
	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(null, "全て"),
		new((int)EnumShiire.Shiire, "仕入"),
		new((int)EnumShiire.Henpin, "仕入返品"),
		new((int)EnumShiire.Nebiki, "値引"),
		new((int)EnumShiire.Other, "その他"),
	];

	[ObservableProperty]
	public partial KubunOption SelectedKubun { get; set; }

	public ShiireSlipPrintViewModel() {
		SelectedKubun = KubunOptions[0];
	}

	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		ClientLib.Exit(this);
	}

	[RelayCommand]
	void Init() { }

	[RelayCommand]
	void SelectShiireCodeFrom() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code");
		if (shiire == null) return;
		ShiireCodeFrom = shiire.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectShiireCodeTo() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code");
		if (shiire == null) return;
		ShiireCodeTo = shiire.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectSokoCodeFrom() {
		var soko = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code");
		if (soko == null) return;
		SokoCodeFrom = soko.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectSokoCodeTo() {
		var soko = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code");
		if (soko == null) return;
		SokoCodeTo = soko.Code ?? string.Empty;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoOutputPdf(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		try {
			ClientLib.Cursor2Wait();
			var sqlParam = BuildPrintSqlParam();
			if (sqlParam == null) return; // BuildPrintSqlParam 側で警告済み
			await RunPrintPdfAsync("ShiireSlipPrint.qfm", null, sqlParam, ct);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	// 明細JSON(Jmeisai)の各値。ShiireInput の明細印刷SQLと同じ抽出規則。
	const string M = "json_extract(m.value,";
	// 取引区分(Kubun)ラベル。ShiireInput.KubunLabel と同義(ここでは h. 修飾)。
	const string KubunLabel = "case h.Kubun when 10 then '仕入' when 20 then '仕入返品' when 30 then '値引' when 99 then 'その他' else cast(h.Kubun as text) end";

	/// <summary>
	/// ShiireSlipPrint.qfm の item1..item46 に対応する 46 列を、明細1行=CSV1行で SELECT する。
	/// 列順が qfm の itemN 順(datasrc)と一致することが前提。参照されない item は '' プレースホルダで詰める。
	/// </summary>
	QueryListSqlParam? BuildPrintSqlParam() {
		List<string> parameters = [];
		List<string> where = [];

		// 仕入日範囲(yyyyMMdd へ正規化)
		if (!string.IsNullOrWhiteSpace(DenDayFrom)) {
			if (!TryToYmd(DenDayFrom, out var from)) { WarnInvalidDate("仕入日(開始)"); return null; }
			where.Add($"h.DenDay >= {AddSqlParameter(parameters, from)}");
		}
		if (!string.IsNullOrWhiteSpace(DenDayTo)) {
			if (!TryToYmd(DenDayTo, out var to)) { WarnInvalidDate("仕入日(終了)"); return null; }
			where.Add($"h.DenDay <= {AddSqlParameter(parameters, to)}");
		}

		// 仕入先コード範囲(MasterShiire.Code)
		if (!string.IsNullOrWhiteSpace(ShiireCodeFrom)) {
			where.Add($"si.Code >= {AddSqlParameter(parameters, ShiireCodeFrom.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(ShiireCodeTo)) {
			where.Add($"si.Code <= {AddSqlParameter(parameters, ShiireCodeTo.Trim())}");
		}

		// 倉庫コード範囲(MasterTokui.Code)
		if (!string.IsNullOrWhiteSpace(SokoCodeFrom)) {
			where.Add($"so.Code >= {AddSqlParameter(parameters, SokoCodeFrom.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(SokoCodeTo)) {
			where.Add($"so.Code <= {AddSqlParameter(parameters, SokoCodeTo.Trim())}");
		}

		// 伝票NO範囲(Tran03Shiire.Id / 数値なので直接埋め込み)
		if (!string.IsNullOrWhiteSpace(DenNoFrom)) {
			if (!long.TryParse(DenNoFrom.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { WarnInvalidNumber("伝票NO(開始)"); return null; }
			where.Add($"h.Id >= {n}");
		}
		if (!string.IsNullOrWhiteSpace(DenNoTo)) {
			if (!long.TryParse(DenNoTo.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { WarnInvalidNumber("伝票NO(終了)"); return null; }
			where.Add($"h.Id <= {n}");
		}

		// 手入力NO範囲(ManualNo / 文字列比較)
		if (!string.IsNullOrWhiteSpace(ManualNoFrom)) {
			where.Add($"h.ManualNo >= {AddSqlParameter(parameters, ManualNoFrom.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(ManualNoTo)) {
			where.Add($"h.ManualNo <= {AddSqlParameter(parameters, ManualNoTo.Trim())}");
		}

		// 取引区分(Kubun)
		if (SelectedKubun?.Value is int kubun) {
			where.Add($"h.Kubun = {kubun}");
		}

		var whereClause = where.Count > 0 ? "where " + string.Join(" and ", where) : "";

		// 明細の数値(数量/単価/金額/上代)
		var su = $"cast(ifnull({M}'$.Su'),0) as int)";
		var tanka = $"cast(ifnull({M}'$.Tanka'),0) as int)";
		var kingaku = $"cast(ifnull({M}'$.Kingaku'),0) as int)";
		var jodai = $"cast(ifnull({M}'$.Jodai'),0) as int)";

		// 内側: 伝票ヘッダ + 仕入先/倉庫/自社の住所を結合(明細展開前)。
		var header = $@"
select h.*,
	ifnull(si.PostalCode,'') siZip,
	trim(ifnull(si.Address1,'') || ifnull(si.Address2,'') || ifnull(si.Address3,'')) siAddr,
	ifnull(si.Tel,'') siTel,
	ifnull(so.Name,'') soName,
	ifnull(so.PostalCode,'') soZip,
	trim(ifnull(so.Address1,'') || ifnull(so.Address2,'') || ifnull(so.Address3,'')) soAddr,
	ifnull(so.Tel,'') soTel,
	ifnull(sys.Name,'') sysName,
	ifnull(sys.PostalCode,'') sysZip,
	trim(ifnull(sys.Address1,'') || ifnull(sys.Address2,'') || ifnull(sys.Address3,'')) sysAddr,
	ifnull(sys.Tel,'') sysTel
from Tran03Shiire h
left join MasterShiire si on si.Id = h.Id_Shiire
left join MasterTokui so on so.Id = h.Id_Soko
left join MasterSysman sys on sys.Id = 1
{whereClause}
";

		// 外側: 明細(Jmeisai)を json_each で1行ずつ展開し、qfm の item1..item46 順に SELECT。
		var sql = $@"
select
	h.Id,																	/* item1  伝票No */
	ifnull(h.KakeDay,''),													/* item2  発送日(掛計上日) */
	ifnull(h.DenDay,''),													/* item3  仕入日 */
	'',																		/* item4  (予備) */
	ifnull(json_extract(h.VShiire,'$.Mei'),''),								/* item5  仕入先名 */
	ifnull(json_extract(h.VShiire,'$.Cd'),''),								/* item6  仕入先CD */
	h.Rate,																	/* item7  掛率 */
	'',																		/* item8  (未使用) */
	'',																		/* item9  (未使用) */
	ifnull(json_extract(h.VShain,'$.Mei'),''),								/* item10 入力者名 */
	h.siZip,																/* item11 仕入先〒 */
	h.siAddr,																/* item12 仕入先住所 */
	h.siTel,																/* item13 仕入先TEL */
	'',																		/* item14 仕入先FAX(該当項目なし) */
	'',																		/* item15 (未使用) */
	ifnull(json_extract(h.VSoko,'$.Mei'),''),								/* item16 入庫倉庫名 */
	h.soZip,																/* item17 倉庫〒 */
	h.soAddr,																/* item18 倉庫住所 */
	h.soTel,																/* item19 倉庫TEL */
	'',																		/* item20 倉庫FAX(該当項目なし) */
	h.sysName,																/* item21 自社名 */
	h.sysZip,																/* item22 自社〒 */
	h.sysAddr,																/* item23 自社住所 */
	h.sysTel,																/* item24 自社TEL */
	'',																		/* item25 自社FAX(該当項目なし) */
	ifnull({M}'$.Code_Shohin'),''),											/* item26 商品CD */
	ifnull({M}'$.Mei_Shohin'),''),											/* item27 商品名 */
	ifnull({M}'$.Code_Col'),''),											/* item28 色CD */
	ifnull({M}'$.Mei_Col'),''),												/* item29 色名 */
	ifnull({M}'$.Code_Siz'),''),											/* item30 サイズCD */
	ifnull({M}'$.Mei_Siz'),''),												/* item31 サイズ名 */
	sum({su}) over (partition by h.Id),										/* item32 数量合計 */
	sum({kingaku}) over (partition by h.Id),								/* item33 金額合計 */
	sum(({su}) * ({jodai})) over (partition by h.Id),						/* item34 上代合計 */
	'請求時一括',															/* item35 消費税(仕入は請求時一括のため固定表示) */
	sum({kingaku}) over (partition by h.Id),								/* item36 総合計(=金額合計、伝票単位の消費税は持たない) */
	{su},																	/* item37 数量 */
	{tanka},																/* item38 単価 */
	{kingaku},																/* item39 金額 */
	{jodai},																/* item40 上代 */
	({su}) * ({jodai}),														/* item41 上代合計(行) */
	cast(ifnull({M}'$.No'),0) as int),										/* item42 明細No */
	'商品仕入',																/* item43 伝票処理区分 */
	{KubunLabel},															/* item44 取引区分 */
	ifnull(h.Memo,''),														/* item45 備考 */
	{KubunLabel}															/* item46 伝票種別(qfm で""伝票""を付加) */
from ({header}) h, json_each(h.Jmeisai) m
order by h.Id, cast(ifnull({M}'$.No'),0) as int)
";

		return new QueryListSqlParam(typeof(Tran03Shiire), sql, [.. parameters]);
	}

	static bool TryToYmd(string value, out string ymd) {
		ymd = string.Empty;
		var v = value.Trim();
		var formats = new[] { "yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd", "yyyy-MM-dd", "yyyy-M-d" };
		if (!DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) {
			return false;
		}
		ymd = d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	void WarnInvalidDate(string label) =>
		MessageEx.ShowWarningDialog($"{label}は yyyy/MM/dd 形式で入力してください。", owner: ClientLib.GetActiveView(this));

	void WarnInvalidNumber(string label) =>
		MessageEx.ShowWarningDialog($"{label}は数値で入力してください。", owner: ClientLib.GetActiveView(this));

	static string AddSqlParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	TResult? ShowSelectDialog<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	async Task RunPrintPdfAsync(string? formFile, PrintByCsvParam? csvParam, QueryListSqlParam? sqlParam, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(formFile)) {
			MessageEx.ShowWarningDialog("印刷フォームファイルが設定されていません", owner: ClientLib.GetActiveView(this));
			return;
		}

		if (csvParam is null && sqlParam is null) {
			MessageEx.ShowWarningDialog("印刷データが設定されていません", owner: ClientLib.GetActiveView(this));
			return;
		}

		if (csvParam is not null && sqlParam is not null) {
			MessageEx.ShowWarningDialog("印刷データは CSV と SQL のどちらか一方だけ設定してください", owner: ClientLib.GetActiveView(this));
			return;
		}
		var mess = "";
		try {
			var param = (object?)csvParam ?? sqlParam!;
			var dataType = csvParam is not null ? typeof(PrintByCsvParam) : typeof(QueryListSqlParam);
			var msg = new PrintOperation {
				DataType = dataType,
				DataMsg = Common.SerializeObject(param),
				FormFile = formFile,
			};

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			string? pdfdata = null;
			await foreach (var streamMsg in coreService.PrintPdfAsync(msg, AppGlobal.GetDefaultCallContext(ct))) {
				ct.ThrowIfCancellationRequested();
				mess = string.Join(" ", new[] { streamMsg.StatusString, streamMsg.DataMsg }.Where(s => !string.IsNullOrWhiteSpace(s)));
				if (streamMsg.Status == -2) {
					MessageEx.ShowWarningDialog(streamMsg.DataMsg, owner: ClientLib.GetActiveView(this));
					return;
				}
				if (streamMsg.Status < 0) {
					var errorDetail = string.IsNullOrWhiteSpace(streamMsg.DataMsg) ? streamMsg.StatusString : streamMsg.DataMsg;
					MessageEx.ShowErrorDialog($"PDF出力失敗: {errorDetail}", owner: ClientLib.GetActiveView(this));
					return;
				}

				if (streamMsg.IsCompleted) {
					pdfdata = streamMsg.DataMsg;
					break;
				}
			}

			if (string.IsNullOrWhiteSpace(pdfdata)) {
				MessageEx.ShowWarningDialog("PDF出力結果が取得できませんでした", owner: ClientLib.GetActiveView(this));
				return;
			}

			var viewTitle = string.IsNullOrWhiteSpace(ClientLib.GetActiveView(this)?.Title)
				? "PDF表示"
				: $"{ClientLib.GetActiveView(this)?.Title} - PDF表示";
			var view = new Views.Sub.WebPdfView { Title = viewTitle };
			if (view.DataContext is not WebPdfViewModel vm) {
				MessageEx.ShowErrorDialog("PDF表示画面の初期化に失敗しました", owner: ClientLib.GetActiveView(this));
				return;
			}

			vm.Pdfdata = $"{AppGlobal.Url}/wrk/{pdfdata}";
			view.Title += " " + vm.Pdfdata;
			ClientLib.ShowDialogView(view, this, IsDialog: false);
			view.Owner = null;
			mess = $"PDFを表示しました: {pdfdata}";
		}
		catch (OperationCanceledException cancel) {
			mess = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			mess = "PDF出力をキャンセルしました";
			return;
		}
		catch (Exception ex) {
			mess = $"PDF出力失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(mess, owner: ClientLib.GetActiveView(this));
		}
	}
}
