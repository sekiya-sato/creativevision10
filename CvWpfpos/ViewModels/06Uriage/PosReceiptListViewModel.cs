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
			var receipt = ReceiptData.FromSale(tran, settings, AppVersion);
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

	// POS以外の売上（端末取引IDなし）は取消しない（設計判断記録 §2.18）
	private bool CanCancelSale() =>
		SelectedTransaction != null &&
		SelectedTransaction.Kubun is 10 or 11 &&
		!string.IsNullOrEmpty(SelectedTransaction.PosClientSaleId) &&
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
			var cancelSale = await client.CancelSaleAsync(SelectedTransaction, settings.StaffId, cancellationToken);
			StatusMessage = $"売上No. {SelectedTransaction.Id:N0} を取消しました（取消No. {cancelSale.Id:N0}）。";
			await LoadAsync(cancellationToken);
		}
		catch (OperationCanceledException) { StatusMessage = "取消処理を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"取消エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand]
	private void Exit() => RequestClose?.Invoke(this, EventArgs.Empty);

	private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
}
