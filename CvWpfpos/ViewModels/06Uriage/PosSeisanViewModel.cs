using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfpos.Services;
using System.Collections.ObjectModel;

namespace CvWpfpos.ViewModels._06Uriage;

/// <summary>
/// 日次精算画面ViewModel。
/// 指定日のTran01Tenuriを照会し、クライアント側で金種別売上を集計する。
/// 金種枚数・釣銭準備金を入力し、差異を算出してTran02PosSeisanへ保存する。
/// </summary>
public partial class PosSeisanViewModel : ObservableObject {
	private readonly PosSettings settings;
	private readonly PosGrpcClient client;

	[ObservableProperty] public partial DateTime SettlementDate { get; set; } = DateTime.Today;

	// 売上集計
	[ObservableProperty] public partial int TransactionCount { get; set; }
	[ObservableProperty] public partial int ReturnCount { get; set; }
	[ObservableProperty] public partial int TotalQuantity { get; set; }
	[ObservableProperty] public partial long TotalAmount { get; set; }
	[ObservableProperty] public partial int CashSalesTotal { get; set; }
	[ObservableProperty] public partial int CardSalesTotal { get; set; }
	[ObservableProperty] public partial int OtherSalesTotal { get; set; }

	// 金種枚数入力
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai10000 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai5000 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai2000 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai1000 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai500 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai100 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai50 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai10 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai5 { get; set; }
	[ObservableProperty, NotifyPropertyChangedFor(nameof(RealAmount), nameof(AmountDiff))]
	public partial int Mai1 { get; set; }

	[ObservableProperty, NotifyPropertyChangedFor(nameof(CalcAmount), nameof(AmountDiff))]
	public partial int JunbiAmount { get; set; }
	[ObservableProperty] public partial int KyakuSu { get; set; }

	public int RealAmount => checked(
		Mai10000 * 10000 + Mai5000 * 5000 + Mai2000 * 2000 + Mai1000 * 1000 +
		Mai500 * 500 + Mai100 * 100 + Mai50 * 50 + Mai10 * 10 + Mai5 * 5 + Mai1 * 1);

	public int CalcAmount => checked(JunbiAmount + CashSalesTotal);
	public int AmountDiff => checked(RealAmount - CalcAmount);

	[ObservableProperty] public partial ObservableCollection<Tran04PosSeisan> PreviousSettlements { get; set; } = [];
	[ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
	[ObservableProperty] public partial bool IsBusy { get; set; }

	public event EventHandler? RequestClose;

	public PosSeisanViewModel() : this(AppGlobal.Settings, AppGlobal.Client) { }

	public PosSeisanViewModel(PosSettings settings, PosGrpcClient client) {
		this.settings = settings;
		this.client = client;
	}

	[RelayCommand]
	private async Task Init(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Load(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

	private async Task LoadAsync(CancellationToken cancellationToken) {
		IsBusy = true;
		try {
			var denDay = SettlementDate.ToString("yyyyMMdd");
			var sales = await client.QueryListAsync<Tran01Tenuri>(
				"DenDay=@0 and Id_Tenpo=@1", "Id",
				[denDay, settings.StoreId.ToString()], 10000, cancellationToken);

			TransactionCount = sales.Count;
			ReturnCount = sales.Count(s => s.Kubun >= 20 && s.Kubun <= 29);
			TotalQuantity = sales.Sum(s => s.SuTotal * s.CalcFlag);
			TotalAmount = sales.Sum(s => s.Total * s.CalcFlag);
			CashSalesTotal = sales.Sum(s => (s.JposPayment?.CashAmount ?? 0) * s.CalcFlag);
			CardSalesTotal = sales.Sum(s => (s.JposPayment?.CardAmount ?? 0) * s.CalcFlag);
			OtherSalesTotal = sales.Sum(s => (s.JposPayment?.OtherAmount ?? 0) * s.CalcFlag);

			OnPropertyChanged(nameof(CalcAmount));
			OnPropertyChanged(nameof(AmountDiff));

			var prev = await client.QueryListAsync<Tran04PosSeisan>(
				"Id_Tenpo=@0", "DenDay desc, SeisanCnt desc",
				[settings.StoreId.ToString()], 50, cancellationToken);
			PreviousSettlements.Clear();
			foreach (var s in prev) PreviousSettlements.Add(s);

			StatusMessage = $"売上データを集計しました（{sales.Count} 件）。";
		}
		catch (OperationCanceledException) { StatusMessage = "集計を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"集計エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SaveSeisan(CancellationToken cancellationToken) {
		if (settings.StoreId <= 0 || settings.StaffId <= 0) {
			StatusMessage = "appsettings.json に StoreId、StaffId を設定してください。";
			return;
		}

		IsBusy = true;
		try {
			var response = await client.SaveSeisanAsync(new PosSaveSeisanRequest {
				StoreId = settings.StoreId,
				DenDay = SettlementDate.ToString("yyyyMMdd"),
				StaffId = settings.StaffId,
				KyakuSu = KyakuSu,
				Mai10000 = Mai10000,
				Mai5000 = Mai5000,
				Mai2000 = Mai2000,
				Mai1000 = Mai1000,
				Mai500 = Mai500,
				Mai100 = Mai100,
				Mai50 = Mai50,
				Mai10 = Mai10,
				Mai5 = Mai5,
				Mai1 = Mai1,
				JunbiAmount = JunbiAmount,
				TotalAmount = (int)TotalAmount,
				CashAmount = CashSalesTotal,
				CardAmount = CardSalesTotal,
				OtherAmount = OtherSalesTotal,
				TransactionCount = TransactionCount,
				ReturnCount = ReturnCount,
				TotalQuantity = TotalQuantity
			}, cancellationToken);

			if (!response.IsSuccess) { StatusMessage = response.Message; return; }

			StatusMessage = $"精算を確定しました（精算No. {response.SeisanCnt}）。";
			await LoadAsync(cancellationToken);
		}
		catch (OperationCanceledException) { StatusMessage = "精算確定を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"精算確定エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	[RelayCommand]
	private void Exit() => RequestClose?.Invoke(this, EventArgs.Empty);
}
