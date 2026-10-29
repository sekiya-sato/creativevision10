using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfpos.Models;
using CvWpfpos.Services;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;

namespace CvWpfpos.ViewModels._06Uriage;

/// <summary>
/// レシート一覧画面ViewModel。
/// 指定日範囲のTran01Tenuriを照会し、一覧表示・レシート再印字・領収書印字を行う。
/// </summary>
public partial class PosReceiptListViewModel : ObservableObject {
	private readonly PosSettings settings;
	private readonly PosGrpcClient client;
	private readonly PosPeripheralService peripherals;

	[ObservableProperty] public partial DateTime FromDate { get; set; } = DateTime.Today.AddDays(-7);
	[ObservableProperty] public partial DateTime ToDate { get; set; } = DateTime.Today;
	[ObservableProperty] public partial ObservableCollection<Tran01Tenuri> Transactions { get; set; } = [];
	[ObservableProperty] public partial Tran01Tenuri? SelectedTransaction { get; set; }
	[ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
	[ObservableProperty] public partial bool IsBusy { get; set; }

	public event EventHandler? RequestClose;

	public PosReceiptListViewModel() : this(AppGlobal.Settings, AppGlobal.Client, AppGlobal.Peripherals) { }

	public PosReceiptListViewModel(PosSettings settings, PosGrpcClient client, PosPeripheralService peripherals) {
		this.settings = settings;
		this.client = client;
		this.peripherals = peripherals;
	}

	[RelayCommand]
	private async Task Init(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Load(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

	private async Task LoadAsync(CancellationToken cancellationToken) {
		IsBusy = true;
		try {
			var fromDay = FromDate.ToString("yyyyMMdd");
			var toDay = ToDate.AddDays(1).ToString("yyyyMMdd");
			var list = await client.QueryListAsync<Tran01Tenuri>(
				"DenDay>=@0 and DenDay<@1 and Id_Tenpo=@2", "DenDay desc,Id desc",
				[fromDay, toDay, settings.StoreId.ToString()], 1000, cancellationToken);
			Transactions.Clear();
			foreach (var t in list) Transactions.Add(t);
			StatusMessage = $"{list.Count} 件の伝票を読み込みました。";
		}
		catch (OperationCanceledException) { StatusMessage = "読み込みを中止しました。"; }
		catch (Exception ex) { StatusMessage = $"読み込みエラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Reprint(CancellationToken cancellationToken) {
		if (SelectedTransaction == null) { StatusMessage = "伝票を選択してください。"; return; }
		await PrintAsync(SelectedTransaction, false, cancellationToken);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task PrintTaxInvoice(CancellationToken cancellationToken) {
		if (SelectedTransaction == null) { StatusMessage = "伝票を選択してください。"; return; }
		await PrintAsync(SelectedTransaction, true, cancellationToken);
	}

	private async Task PrintAsync(Tran01Tenuri tran, bool isTaxInvoice, CancellationToken cancellationToken) {
		IsBusy = true;
		try {
			var receipt = BuildReceiptFromTran(tran);
			if (isTaxInvoice)
				await peripherals.PrintTaxInvoiceAsync(receipt, cancellationToken);
			else
				await peripherals.PrintAsync(receipt, cancellationToken);
			StatusMessage = $"売上No. {tran.Id:N0} の{(isTaxInvoice ? "領収書" : "レシート")}を印字しました。";
		}
		catch (OperationCanceledException) { StatusMessage = "印字を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"印字エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand]
	private async Task ConnectPrinter() {
		try { await Task.Run(peripherals.ConnectPrinter); StatusMessage = $"{settings.PosPrinterName} を {settings.PrinterPortName} に接続しました。"; }
		catch (Exception ex) { StatusMessage = $"{settings.PosPrinterName} 接続エラー: {ex.Message}"; }
	}

	private bool CanCancelSale() =>
		SelectedTransaction != null &&
		SelectedTransaction.Kubun is 10 or 11 &&
		!IsCancelled(SelectedTransaction);

	private static bool IsCancelled(Tran01Tenuri tran) =>
		!string.IsNullOrEmpty(tran.PosClientSaleId) && tran.PosClientSaleId.EndsWith(":C", StringComparison.Ordinal);

	[RelayCommand(CanExecute = nameof(CanCancelSale))]
	private async Task CancelSale(CancellationToken cancellationToken) {
		if (SelectedTransaction == null) return;
		var result = MessageBox.Show(
			$"売上No. {SelectedTransaction.Id:N0} を取消しますか？\n取消後は元の売上を復元できません。",
			"取消確認",
			MessageBoxButton.YesNo,
			MessageBoxImage.Question,
			MessageBoxResult.No);
		if (result != MessageBoxResult.Yes) return;

		IsBusy = true;
		try {
			var response = await client.CancelSaleAsync(new PosCancelSaleRequest {
				SaleId = SelectedTransaction.Id,
				StaffId = settings.StaffId
			}, cancellationToken);
			if (!response.IsSuccess) { StatusMessage = response.Message; return; }
			StatusMessage = $"売上No. {SelectedTransaction.Id:N0} を取消しました（取消No. {response.CancelSaleId:N0}）。";
			await LoadAsync(cancellationToken);
		}
		catch (OperationCanceledException) { StatusMessage = "取消処理を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"取消エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand]
	private void Exit() => RequestClose?.Invoke(this, EventArgs.Empty);

	private ReceiptData BuildReceiptFromTran(Tran01Tenuri tran) {
		var subTotal = tran.KingakuTotal > 0 ? tran.KingakuTotal : tran.Total;
		var taxAmount = ((long)subTotal * settings.TaxRatePercent / 100);
		var lines = tran.Jmeisai?.Select(m => new ReceiptLine(
			m.Code_Shohin,
			m.Mei_Shohin,
			FormatColorSize(m),
			m.JanCode,
			m.Su,
			m.Tanka,
			(int)m.Kingaku
		)).ToList() ?? [];
		var payment = tran.JposPayment ?? new PosPaymentDetail();
		var staffCode = tran.VShain?.Cd ?? settings.ResolvedStaffCode;
		var soldAt = tran.Vdc > 0 ? new DateTime(tran.Vdc, DateTimeKind.Utc).ToLocalTime() : DateTime.Now;
		return new ReceiptData(
			tran.Id,
			soldAt,
			new ReceiptStore(settings.StoreName, settings.StoreAddress, settings.StorePhone),
			staffCode,
			lines,
			tran.SuTotal,
			(int)subTotal,
			settings.TaxRatePercent,
			(int)taxAmount,
			(int)(subTotal + taxAmount),
			(int)payment.CashAmount,
			(int)payment.CardAmount,
			payment.OtherAmount,
			payment.ChangeAmount,
			AppVersion,
			tran.Kubun >= 20 && tran.Kubun <= 29
		);
	}

	private static string FormatColorSize(Tran99Meisai m) =>
		string.Join(' ', new[] { JoinCodeName(m.Code_Col, m.Mei_Col), JoinCodeName(m.Code_Siz, m.Mei_Siz) }.Where(text => text.Length > 0));

	private static string JoinCodeName(string code, string name) =>
		(code.Length, name.Length) switch { (0, 0) => string.Empty, (0, _) => name, (_, 0) => code, _ => $"{code}-{name}" };

	private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
}
