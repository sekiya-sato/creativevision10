using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfpos.Models;
using CvWpfpos.Services;
using CvWpfpos.Views._06Uriage;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;

namespace CvWpfpos.ViewModels._06Uriage;

/// <summary>
/// 売上入力（バーコード読取 → 明細 → 会計 → レシート印字）。
/// ログイン直後に表示されるメイン画面。
/// </summary>
public partial class PosUriageInputViewModel : ObservableObject, IDisposable {
	private readonly PosSettings settings;
	private readonly PosGrpcClient client;
	private readonly PosPeripheralService peripherals;
	private string checkoutClientSaleId = string.Empty;
	private bool disposed;

	/// <summary>ViewModel から View に閉じるよう要求する（BaseWindow の ExitCommand 経由）</summary>
	public event EventHandler? CloseRequested;

	[ObservableProperty, NotifyCanExecuteChangedFor(nameof(ScanBarcodeCommand))] public partial string BarcodeText { get; set; } = string.Empty;
	[ObservableProperty] public partial ObservableCollection<PosCartLine> CartLines { get; set; } = [];
	[ObservableProperty] public partial PosCartLine? SelectedLine { get; set; }
	[ObservableProperty] public partial string StatusMessage { get; set; } = "バーコードを読み取ってください。";
	[ObservableProperty] public partial bool IsCheckoutMode { get; set; }
	[ObservableProperty, NotifyCanExecuteChangedFor(nameof(PrintTaxInvoiceCommand))] public partial bool IsBusy { get; set; }
	[ObservableProperty] public partial bool IsDisplayConnected { get; set; }
	[ObservableProperty] public partial bool IsPrinterConnected { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(PaymentAmount), nameof(ChangeAmount))] public partial int CashAmount { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(PaymentAmount), nameof(ChangeAmount))] public partial int CardAmount { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(PaymentAmount), nameof(ChangeAmount))] public partial int OtherAmount { get; set; }
	[ObservableProperty] public partial bool IsReturnMode { get; set; }
	[ObservableProperty] public partial ObservableCollection<PosHoldEntry> HoldList { get; set; } = [];

	/// <summary>直近に確定した売上。領収書ボタンはこれを印字する。</summary>
	[ObservableProperty, NotifyCanExecuteChangedFor(nameof(PrintTaxInvoiceCommand))] public partial ReceiptData? LastReceipt { get; set; }

	public string StoreName => settings.StoreName;
	public string LoginId => AppGlobal.LoginId;
	public int TotalQuantity => CartLines.Sum(line => line.Quantity);

	/// <summary>税抜小計（上代の合計）。サーバが計上する売上金額と一致する。</summary>
	public int SubTotal => CartLines.Sum(line => line.Amount);

	public int TaxRatePercent => settings.TaxRatePercent;

	/// <summary>消費税額（外税、円未満切り捨て）。</summary>
	public int TaxAmount => (int)((long)SubTotal * settings.TaxRatePercent / 100);

	/// <summary>お買上合計（税込）。お客様への請求額。</summary>
	public int TotalAmount => checked(SubTotal + TaxAmount);

	public int PaymentAmount => checked(CashAmount + CardAmount + OtherAmount);
	public int ChangeAmount => Math.Max(0, PaymentAmount - TotalAmount);

	/// <summary>XAML の DataContext 宣言用。AppGlobal から共有インスタンスを取得する。</summary>
	public PosUriageInputViewModel() : this(AppGlobal.Settings, AppGlobal.Client, AppGlobal.Peripherals) { }

	public PosUriageInputViewModel(PosSettings settings, PosGrpcClient client, PosPeripheralService peripherals) {
		this.settings = settings;
		this.client = client;
		this.peripherals = peripherals;
	}

	private bool CanScanBarcode() => !IsBusy && !IsCheckoutMode && !string.IsNullOrWhiteSpace(BarcodeText);

	/// <summary>
	/// BaseWindow.OnContentRendered から呼ばれる初期化。
	/// 客用ディスプレイのみ接続する。レシートプリンタは売上開始時（最初の明細追加時）に接続する。
	/// </summary>
	[RelayCommand]
	private void Init() {
		try {
			peripherals.ConnectDisplay();
			IsDisplayConnected = true;
			StatusMessage = "バーコードを読み取ってください。";
		}
		catch (Exception ex) {
			IsDisplayConnected = false;
			StatusMessage = $"DM-D30 に接続できませんでした（{ex.Message}）。接続ボタンで再試行してください。";
		}
	}

	/// <summary>ESC。会計中なら明細に戻り、そうでなければ画面を閉じる。</summary>
	[RelayCommand]
	private void Exit() {
		if (IsCheckoutMode) {
			CancelCheckout();
			return;
		}
		CloseRequested?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void ConnectDisplay() {
		try { peripherals.ConnectDisplay(); IsDisplayConnected = true; StatusMessage = $"{settings.PosDisplayName} を {settings.DisplayPortName} に接続しました。"; }
		catch (Exception ex) { IsDisplayConnected = false; StatusMessage = $"{settings.PosDisplayName} 接続エラー: {ex.Message}"; }
	}

	/// <summary>接続ボタンによる手動接続。Open がブロックしうるため UI スレッドから外す。</summary>
	[RelayCommand]
	private async Task ConnectPrinter() {
		try { await Task.Run(peripherals.ConnectPrinter); IsPrinterConnected = true; StatusMessage = $"{settings.PosPrinterName} を {settings.PrinterPortName} に接続しました。"; }
		catch (Exception ex) { IsPrinterConnected = false; StatusMessage = $"{settings.PosPrinterName} 接続エラー: {ex.Message}"; }
	}

	[RelayCommand(CanExecute = nameof(CanScanBarcode), IncludeCancelCommand = true)]
	private async Task ScanBarcode(CancellationToken cancellationToken) {
		var barcode = BarcodeText.Trim();
		IsBusy = true;
		try {
			var product = await client.LookupProductAsync(barcode, cancellationToken);
			if (product == null) { StatusMessage = $"バーコードが見つかりません: {barcode}"; return; }

			var line = new PosCartLine {
				Barcode = barcode,
				ProductId = product.ProductId,
				ProductCode = product.ProductCode,
				ColorId = product.ColorId,
				ColorCode = product.ColorCode,
				ColorName = product.ColorName,
				SizeId = product.SizeId,
				SizeCode = product.SizeCode,
				SizeName = product.SizeName,
				Name = product.ProductName,
				UnitPrice = product.UnitPrice,
				Quantity = 1,
				Kubun = 0,
				StaffId = settings.StaffId,
				StaffCode = settings.ResolvedStaffCode,
				StaffName = string.Empty
			};
			AddOrMergeCartLine(line);

			BarcodeText = string.Empty;
			NotifyTotalsChanged();
			await peripherals.UpdateDisplayAsync($"点数 {line.Quantity:N0} 金額 {line.Amount:N0}", $"合計 {TotalQuantity:N0}点 {TotalAmount:N0}", cancellationToken);
			StatusMessage = $"{line.Name} を追加しました。";

			// 売上開始時にレシートプリンタへ接続し、失敗はこの時点で通知する
			if (CartLines.Count == 1) await ConnectPrinterOnSaleStartAsync(cancellationToken);
		}
		catch (OperationCanceledException) { StatusMessage = "バーコード読取を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"バーコード読取エラー: {ex.Message}"; }
		finally { IsBusy = false; ScanBarcodeCommand.NotifyCanExecuteChanged(); }
	}

	/// <summary>
	/// 明細が空の状態から 1 件目を積む＝この読取が売上の開始
	/// </summary>
	private void AddOrMergeCartLine(PosCartLine line) {
		var existing = CartLines.FirstOrDefault(item => string.Equals(item.Barcode, line.Barcode, StringComparison.OrdinalIgnoreCase));
		if (existing == null) {
			line.LineNo = CartLines.Count + 1;
			CartLines.Add(line);
			SelectedLine = line;
		}
		else {
			existing.Quantity++;
			SelectedLine = existing;
		}
	}

	/// <summary>
	/// 売上開始時のプリンタ接続。既に開いていれば何もしない。失敗はステータスに即時表示する。
	/// Bluetooth 仮想 COM の Open は数秒ブロックすることがあるため、UI スレッドから外して実行する。
	/// </summary>
	private async Task ConnectPrinterOnSaleStartAsync(CancellationToken cancellationToken) {
		if (peripherals.IsPrinterOpen) { IsPrinterConnected = true; return; }

		try {
			await Task.Run(peripherals.ConnectPrinter, cancellationToken);
			IsPrinterConnected = true;
		}
		catch (OperationCanceledException) {
			IsPrinterConnected = false;
		}
		catch (Exception ex) {
			IsPrinterConnected = false;
			StatusMessage = $"TM-m30II 接続エラー: {ex.Message}／このままではレシートを印字できません。接続ボタンで再試行してください。";
		}
	}

	[RelayCommand]
	private void StartCheckout() {
		if (CartLines.Count == 0) { StatusMessage = "会計する明細がありません。"; return; }
		IsCheckoutMode = true;
		checkoutClientSaleId = Guid.NewGuid().ToString("N");
		CashAmount = TotalAmount;
		CardAmount = OtherAmount = 0;
	}

	[RelayCommand] private void CancelCheckout() => IsCheckoutMode = false;

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task CompleteCheckout(CancellationToken cancellationToken) {
		if (PaymentAmount < TotalAmount) { StatusMessage = "お預り金額が合計金額に不足しています。"; return; }
		if (settings.StoreId <= 0 || settings.WarehouseId <= 0 || settings.StaffId <= 0) { StatusMessage = "appsettings.json に StoreId、WarehouseId、StaffId を設定してください。"; return; }

		IsBusy = true;
		try {
			var response = await client.CheckoutAsync(new PosCheckoutRequest {
				ClientSaleId = checkoutClientSaleId,
				StoreId = settings.StoreId,
				WarehouseId = settings.WarehouseId,
				StaffId = settings.StaffId,
				Lines = [.. CartLines.Select(line => new PosCheckoutLine {
					Barcode = line.Barcode, ProductId = line.ProductId, ColorId = line.ColorId, ColorCode = line.ColorCode, ColorName = line.ColorName,
					SizeId = line.SizeId, SizeCode = line.SizeCode, SizeName = line.SizeName, Quantity = line.Quantity,
					Kubun = line.Kubun, StaffId = line.StaffId, StaffCode = line.StaffCode, StaffName = line.StaffName
				})],
				Payment = new PosPayment { CashAmount = CashAmount, CardAmount = CardAmount, OtherAmount = OtherAmount },
				Kubun = IsReturnMode ? 20 : 10
			}, cancellationToken);
			if (!response.IsSuccess) { StatusMessage = response.Message; return; }

			// 売上は確定済み。印字が失敗しても取引を宙ぶらりんにしないよう、先に明細を締めて領収書用に保持する
			var receipt = BuildReceipt(response.SaleId);
			LastReceipt = receipt;
			CartLines.Clear();
			checkoutClientSaleId = string.Empty;
			SelectedLine = null;
			IsCheckoutMode = false;
			IsReturnMode = false;
			CashAmount = CardAmount = OtherAmount = 0;
			NotifyTotalsChanged();

			try {
				await peripherals.PrintAsync(receipt, cancellationToken);
				StatusMessage = $"売上No. {response.SaleId:N0} を確定し、レシートを印字しました。領収書が必要な場合は［領収書］ボタンを押してください。";
			}
			catch (Exception ex) {
				StatusMessage = $"売上No. {response.SaleId:N0} は確定しましたが、レシートを印字できませんでした（{ex.Message}）。";
			}
		}
		catch (OperationCanceledException) { StatusMessage = "会計処理を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"会計処理エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	private bool CanPrintTaxInvoice() => !IsBusy && LastReceipt != null;

	/// <summary>［領収書］ボタン。直近に確定した売上の領収書を、必要なときだけ印字する。</summary>
	[RelayCommand(CanExecute = nameof(CanPrintTaxInvoice), IncludeCancelCommand = true)]
	private async Task PrintTaxInvoice(CancellationToken cancellationToken) {
		if (LastReceipt is not { } receipt) return;

		IsBusy = true;
		try {
			await peripherals.PrintTaxInvoiceAsync(receipt, cancellationToken);
			StatusMessage = $"売上No. {receipt.SaleId:N0} の領収書を印字しました。";
		}
		catch (OperationCanceledException) { StatusMessage = "領収書の印字を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"領収書印字エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand]
	private void DeleteLine() {
		if (SelectedLine == null) return;
		CartLines.Remove(SelectedLine);
		SelectedLine = null;
		RenumberLines();
		NotifyTotalsChanged();
		_ = UpdateDisplayAsync();
		StatusMessage = "行を削除しました。";
	}

	[RelayCommand]
	private void IncreaseQuantity() {
		if (SelectedLine == null) return;
		SelectedLine.Quantity++;
		NotifyTotalsChanged();
		_ = UpdateDisplayAsync();
	}

	[RelayCommand]
	private void DecreaseQuantity() {
		if (SelectedLine == null || SelectedLine.Quantity <= 1) return;
		SelectedLine.Quantity--;
		NotifyTotalsChanged();
		_ = UpdateDisplayAsync();
	}

	[RelayCommand]
	private void ToggleLinePS() {
		if (SelectedLine == null) return;
		SelectedLine.Kubun = SelectedLine.Kubun == 0 ? 1 : 0;
		NotifyTotalsChanged();
		_ = UpdateDisplayAsync();
	}

	[RelayCommand]
	private void AssignLineStaff() {
		if (SelectedLine == null) return;
		var dialog = new PosStaffSelectView { Owner = Application.Current.MainWindow };
		if (dialog.ShowDialog() == true && dialog.ViewModel?.SelectedStaff is { } staff) {
			SelectedLine.StaffId = staff.Id;
			SelectedLine.StaffCode = staff.Code;
			SelectedLine.StaffName = staff.Name;
			NotifyTotalsChanged();
			_ = UpdateDisplayAsync();
			StatusMessage = $"担当を {staff.Name} に変更しました。";
		}
	}

	[RelayCommand]
	private void SearchProduct() {
		var dialog = new PosProductSearchView { Owner = Application.Current.MainWindow };
		if (dialog.ShowDialog() == true && dialog.ViewModel?.SelectedProduct is { } product) {
			AddProductLine(product, dialog.ViewModel.SelectedBarcode);
		}
	}

	private void AddProductLine(PosProduct product, string barcode) {
		var line = new PosCartLine {
			Barcode = barcode,
			ProductId = product.ProductId,
			ProductCode = product.ProductCode,
			ColorId = product.ColorId,
			ColorCode = product.ColorCode,
			ColorName = product.ColorName,
			SizeId = product.SizeId,
			SizeCode = product.SizeCode,
			SizeName = product.SizeName,
			Name = product.ProductName,
			UnitPrice = product.UnitPrice,
			Quantity = 1,
			Kubun = 0,
			StaffId = settings.StaffId,
			StaffCode = settings.ResolvedStaffCode,
			StaffName = string.Empty
		};
		AddOrMergeCartLine(line);
		NotifyTotalsChanged();
		_ = UpdateDisplayAsync();
		StatusMessage = $"{line.Name} を追加しました。";
	}

	[RelayCommand]
	private void HoldSale() {
		if (CartLines.Count == 0) { StatusMessage = "保留する明細がありません。"; return; }
		var snapshot = new ObservableCollection<PosCartLine>();
		foreach (var line in CartLines) {
			snapshot.Add(new PosCartLine {
				LineNo = line.LineNo,
				Barcode = line.Barcode,
				ProductId = line.ProductId,
				ProductCode = line.ProductCode,
				ColorId = line.ColorId,
				ColorCode = line.ColorCode,
				ColorName = line.ColorName,
				SizeId = line.SizeId,
				SizeCode = line.SizeCode,
				SizeName = line.SizeName,
				Name = line.Name,
				UnitPrice = line.UnitPrice,
				Quantity = line.Quantity,
				Kubun = line.Kubun,
				StaffId = line.StaffId,
				StaffCode = line.StaffCode,
				StaffName = line.StaffName
			});
		}
		HoldList.Add(new PosHoldEntry {
			HeldAt = DateTime.Now,
			ItemCount = CartLines.Count,
			TotalAmount = TotalAmount,
			Lines = snapshot
		});
		CartLines.Clear();
		SelectedLine = null;
		NotifyTotalsChanged();
		StatusMessage = "売上を保留しました。";
	}

	[RelayCommand]
	private void ResumeHold() {
		if (HoldList.Count == 0) { StatusMessage = "保留中の売上がありません。"; return; }
		var dialog = new PosHoldListView(HoldList) { Owner = Application.Current.MainWindow };
		if (dialog.ShowDialog() == true && dialog.ViewModel?.SelectedEntry is { } entry) {
			CartLines.Clear();
			foreach (var line in entry.Lines) {
				CartLines.Add(new PosCartLine {
					LineNo = line.LineNo,
					Barcode = line.Barcode,
					ProductId = line.ProductId,
					ProductCode = line.ProductCode,
					ColorId = line.ColorId,
					ColorCode = line.ColorCode,
					ColorName = line.ColorName,
					SizeId = line.SizeId,
					SizeCode = line.SizeCode,
					SizeName = line.SizeName,
					Name = line.Name,
					UnitPrice = line.UnitPrice,
					Quantity = line.Quantity,
					Kubun = line.Kubun,
					StaffId = line.StaffId,
					StaffCode = line.StaffCode,
					StaffName = line.StaffName
				});
			}
			RenumberLines();
			SelectedLine = CartLines.FirstOrDefault();
			NotifyTotalsChanged();
			StatusMessage = "保留していた売上を復元しました。";
		}
	}

	[RelayCommand]
	private void ToggleReturnMode() {
		if (CartLines.Count > 0) { StatusMessage = "明細がある状態では返品モードに切り替えられません。"; return; }
		IsReturnMode = !IsReturnMode;
		StatusMessage = IsReturnMode ? "返品モード" : "バーコードを読み取ってください。";
	}

	private void RenumberLines() {
		int no = 1;
		foreach (var line in CartLines) line.LineNo = no++;
	}

	private async Task UpdateDisplayAsync() {
		try {
			await peripherals.UpdateDisplayAsync($"合計 {TotalQuantity:N0}点 {TotalAmount:N0}", string.Empty, CancellationToken.None);
		}
		catch { /* 客用ディスプレイは必須ではない */ }
	}

	private ReceiptData BuildReceipt(long saleId) => new(
		saleId,
		DateTime.Now,
		new ReceiptStore(settings.StoreName, settings.StoreAddress, settings.StorePhone),
		settings.ResolvedStaffCode,
		[.. CartLines.Select(line => new ReceiptLine(line.ProductCode, line.Name, FormatColorSize(line), line.Barcode, line.Quantity, line.UnitPrice, line.Amount))],
		TotalQuantity,
		SubTotal,
		TaxRatePercent,
		TaxAmount,
		TotalAmount,
		CashAmount,
		CardAmount,
		OtherAmount,
		// サーバは消費税を持たないため釣銭もクライアント計算値（税込合計に対する釣銭）を使う
		ChangeAmount,
		AppVersion,
		IsReturnMode);

	/// <summary>「10-シロ 00-サンプル」形式のカラー・サイズ表記。</summary>
	private static string FormatColorSize(PosCartLine line) =>
		string.Join(' ', new[] { JoinCodeName(line.ColorCode, line.ColorName), JoinCodeName(line.SizeCode, line.SizeName) }.Where(text => text.Length > 0));

	private static string JoinCodeName(string code, string name) =>
		(code.Length, name.Length) switch { (0, 0) => string.Empty, (0, _) => name, (_, 0) => code, _ => $"{code}-{name}" };

	private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;

	private void NotifyTotalsChanged() {
		OnPropertyChanged(nameof(TotalQuantity));
		OnPropertyChanged(nameof(SubTotal));
		OnPropertyChanged(nameof(TaxAmount));
		OnPropertyChanged(nameof(TotalAmount));
		OnPropertyChanged(nameof(ChangeAmount));
	}

	public void Dispose() {
		if (disposed) return;
		disposed = true;
	}
}
