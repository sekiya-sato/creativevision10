using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>補充計画の確認・保存・確定。計算と伝票生成はサーバーで行う。</summary>
public partial class AutoOrderReplenishExecuteViewModel : BaseViewModel {
	string fingerprint = string.Empty;
	string executionKey = string.Empty;
	AutoReplenishParam? pendingSave;

	[ObservableProperty] public partial long WarehouseId { get; set; }
	[ObservableProperty] public partial string WarehouseText { get; set; } = "倉庫を選択してください";
	[ObservableProperty] public partial bool IsProcessing { get; set; }
	[ObservableProperty] public partial TranAutoReplenishBatch? CurrentBatch { get; set; }
	[ObservableProperty] public partial TranAutoReplenishBatch? SelectedHistory { get; set; }
	[ObservableProperty] public partial ObservableCollection<AutoReplenishRow> Rows { get; set; } = [];
	[ObservableProperty] public partial ObservableCollection<TranAutoReplenishBatch> HistoryRows { get; set; } = [];
	[ObservableProperty] public partial string StatusMessage { get; set; } = "倉庫を選択して補充候補を計算してください。";
	[ObservableProperty] public partial string SummaryText { get; set; } = string.Empty;
	[ObservableProperty] public partial string ErrorText { get; set; } = string.Empty;

	public string BasisDay => DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
	public string BasisDayText => DateTime.Today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
	public bool HasPendingSave => pendingSave != null;
	public string BatchText => CurrentBatch == null ? "未保存"
		: $"補充No. {CurrentBatch.Id} / {CurrentBatch.Status switch { 0 => "未確定", 1 => "確定済", _ => "取消済" }}";

	bool CanChooseWarehouse() => !IsProcessing && !HasPendingSave;
	bool CanQuery() => CanChooseWarehouse() && WarehouseId > 0;
	bool CanSave() => !IsProcessing && (HasPendingSave
		|| (CurrentBatch == null && Rows.Any(x => x.Row.DemandSu > 0) && fingerprint.Length > 0 && ErrorText.Length == 0));
	bool CanLoad() => !IsProcessing && !HasPendingSave && SelectedHistory?.Id > 0;
	bool CanChangeBatch() => !IsProcessing && !HasPendingSave && CurrentBatch?.Status == 0;

	partial void OnIsProcessingChanged(bool value) => RefreshCommands();
	partial void OnCurrentBatchChanged(TranAutoReplenishBatch? value) {
		OnPropertyChanged(nameof(BatchText));
		RefreshCommands();
	}
	partial void OnSelectedHistoryChanged(TranAutoReplenishBatch? value) => RefreshCommands();
	partial void OnWarehouseIdChanged(long value) {
		if (IsProcessing) return;
		ClearPlan();
		HistoryRows.Clear();
		SelectedHistory = null;
		RefreshCommands();
	}

	[RelayCommand] void Init() { }

	[RelayCommand(CanExecute = nameof(CanChooseWarehouse))]
	void SelectWarehouse() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterTokui>(this, typeof(MasterTokui), "TenType=0 and IsZaiko=1", "Code", WarehouseId);
		if (selected == null) return;
		WarehouseId = selected.Id;
		WarehouseText = $"{selected.Code} {selected.Name}";
	}

	[RelayCommand(CanExecute = nameof(CanQuery), IncludeCancelCommand = true)]
	async Task Preview(CancellationToken ct) {
		ClearPlan();
		var result = await CallAsync(Request(AutoReplenishOperation.Preview), "補充候補計算", ct);
		if (result == null) return;
		ApplyPlan(result);
		executionKey = Guid.NewGuid().ToString("N");
		StatusMessage = ErrorText.Length > 0 ? "設定・対象エラーを確認してください。保存はできません。"
			: !Rows.Any(x => x.Row.DemandSu > 0) ? "補充対象はありません。" : "内訳を確認して補充データを保存してください。";
		RefreshCommands();
	}

	[RelayCommand(CanExecute = nameof(CanSave))]
	async Task Save() {
		// 応答不明時は条件と実行キーを保持し、同じ要求だけを再送する。
		pendingSave ??= Request(AutoReplenishOperation.Save) with { ExecutionKey = executionKey, Fingerprint = fingerprint };
		OnPropertyChanged(nameof(HasPendingSave));
		var result = await CallAsync(pendingSave, "補充保存", CancellationToken.None);
		if (result == null) StatusMessage += HasPendingSave
			? " 同じ「補充保存」で結果を再確認してください。" : " 再計算してください。";
		else {
			pendingSave = null;
			ApplyPlan(result);
			StatusMessage = ErrorText.Length == 0 ? "補充データを保存しました。確定で配分指示と発注を作成します。" : ErrorText;
		}
		OnPropertyChanged(nameof(HasPendingSave));
		RefreshCommands();
	}

	[RelayCommand(CanExecute = nameof(CanQuery), IncludeCancelCommand = true)]
	async Task LoadHistory(CancellationToken ct) {
		var result = await CallAsync(Request(AutoReplenishOperation.History), "履歴取得", ct);
		if (result == null) return;
		HistoryRows = new(result.Batches);
		StatusMessage = $"補充履歴 {HistoryRows.Count:N0} 件。対象を選び「補充読込」を実行してください。";
	}

	[RelayCommand(CanExecute = nameof(CanLoad), IncludeCancelCommand = true)]
	async Task Load(CancellationToken ct) {
		var result = await CallAsync(Request(AutoReplenishOperation.Load) with { Id_Batch = SelectedHistory!.Id }, "補充読込", ct);
		if (result == null) return;
		ApplyPlan(result);
		StatusMessage = ErrorText.Length > 0 ? ErrorText : $"{BatchText}を読み込みました。";
	}

	[RelayCommand(CanExecute = nameof(CanChangeBatch))]
	async Task Commit() {
		if (MessageEx.ShowQuestionDialog($"{BatchText}を確定しますか？\n{SummaryText}\n在庫配分指示と仕入先発注を作成します。",
			owner: ClientLib.GetActiveView(this)) != System.Windows.MessageBoxResult.Yes) return;
		var result = await CallAsync(Request(AutoReplenishOperation.Commit), "補充確定", CancellationToken.None);
		if (result == null) {
			StatusMessage += " 補充読込で状態を確認するか、同じ補充No.で確定を再試行してください。";
			return;
		}
		ApplyPlan(result);
		StatusMessage = ErrorText.Length > 0 ? ErrorText
			: $"確定しました。発注 {result.CreatedHachuIds.Count:N0} 件、配分指示 {result.CreatedHaibunIds.Count:N0} 件。";
	}

	[RelayCommand(CanExecute = nameof(CanChangeBatch))]
	async Task Cancel() {
		if (MessageEx.ShowQuestionDialog($"{BatchText}を取消しますか？", owner: ClientLib.GetActiveView(this)) != System.Windows.MessageBoxResult.Yes) return;
		var result = await CallAsync(Request(AutoReplenishOperation.Cancel), "補充取消", CancellationToken.None);
		if (result == null) return;
		ApplyPlan(result);
		StatusMessage = ErrorText.Length > 0 ? ErrorText : "補充データを取消しました。再計算できます。";
	}

	AutoReplenishParam Request(AutoReplenishOperation operation) => new(WarehouseId, BasisDay, operation,
		ExecutionKey: CurrentBatch?.ExecutionKey ?? string.Empty,
		Id_Batch: CurrentBatch?.Id ?? 0, ExpectedVdu: CurrentBatch?.Vdu ?? 0);

	async Task<AutoReplenishResult?> CallAsync(AutoReplenishParam request, string label, CancellationToken ct) {
		IsProcessing = true;
		StatusMessage = $"{label}中...";
		try {
			var reply = await CoreServiceClient.SendExecuteAsync(request, ct);
			if (reply.Code < 0) {
				if (request.Operation == AutoReplenishOperation.Save) {
					pendingSave = null;
					fingerprint = string.Empty;
					OnPropertyChanged(nameof(HasPendingSave));
				}
				StatusMessage = string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg ?? "処理に失敗しました。" : reply.Option;
				return null;
			}
			return Common.DeserializeObject<AutoReplenishResult>(reply.DataMsg ?? string.Empty)
				?? throw new InvalidOperationException("応答を読み取れませんでした。");
		}
		catch (OperationCanceledException) { StatusMessage = $"{label}を中止しました。"; return null; }
		catch (Exception ex) { StatusMessage = $"{label}の結果を確認できませんでした。{ex.Message}"; return null; }
		finally { IsProcessing = false; }
	}

	void ApplyPlan(AutoReplenishResult result) {
		if (result.Errors.Count > 0 && result.Batch == null && CurrentBatch != null) {
			ErrorText = string.Join(Environment.NewLine, result.Errors);
			RefreshCommands();
			return;
		}
		CurrentBatch = result.Batch;
		Rows = new(result.Rows);
		fingerprint = result.Fingerprint;
		ErrorText = string.Join(Environment.NewLine, result.Errors);
		SummaryText = $"店舗配分 {Rows.Sum(x => (long)x.Row.TransferSu):N0} 点 / "
			+ $"既存供給充当 {Rows.Sum(x => (long)x.Row.CoveredSu):N0} 点 / "
			+ $"追加発注 {Rows.Sum(x => (long)x.Row.Su):N0} 点・{Rows.Sum(x => (long)x.Row.Kingaku):N0} 円";
		RefreshCommands();
	}

	void ClearPlan() {
		CurrentBatch = null;
		Rows.Clear();
		fingerprint = string.Empty;
		executionKey = string.Empty;
		ErrorText = string.Empty;
		SummaryText = string.Empty;
		RefreshCommands();
	}

	void RefreshCommands() {
		SelectWarehouseCommand.NotifyCanExecuteChanged();
		PreviewCommand.NotifyCanExecuteChanged();
		SaveCommand.NotifyCanExecuteChanged();
		LoadHistoryCommand.NotifyCanExecuteChanged();
		LoadCommand.NotifyCanExecuteChanged();
		CommitCommand.NotifyCanExecuteChanged();
		CancelCommand.NotifyCanExecuteChanged();
	}
}
