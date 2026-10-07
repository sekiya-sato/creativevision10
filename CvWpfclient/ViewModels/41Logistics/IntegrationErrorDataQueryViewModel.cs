using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._41Logistics;

/// <summary>
/// L04 物流連携のエラー照会。送受信バッチ・行の状態から再出力／再検査可能な対象を選ぶ。
/// 送受信バッチと行を照会し、送信バッチの再出力・取消、受信行の除外・訂正版追加・再検査を行う。
/// 在庫・数量・伝票を直接修正する機能は置かない。
/// </summary>
public partial class IntegrationErrorDataQueryViewModel : LogisticsViewModelBase {
	/// <summary>方向の選択肢（0=すべて 1=送信 2=受信）</summary>
	public IReadOnlyList<KeyValuePair<int, string>> DirectionOptions { get; } = [
		new(0, "すべて"),
		new((int)EnumLogisticsDirection.Send, "送信"),
		new((int)EnumLogisticsDirection.Receive, "受信"),
	];

	/// <summary>種別の選択肢（空=すべて）</summary>
	public IReadOnlyList<KeyValuePair<string, string>> KindOptions { get; } = [
		new(string.Empty, "すべて"),
		.. LogisticsDataKind.MasterKinds.Concat(LogisticsDataKind.SendKinds).Concat(LogisticsDataKind.ReceiveKinds)
			.Select(k => new KeyValuePair<string, string>(k, $"{LogisticsDataKind.DisplayName(k)}({k})")),
	];

	[ObservableProperty]
	public partial int Direction { get; set; }

	[ObservableProperty]
	public partial string Kind { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? FromDate { get; set; } = DateTime.Today.AddDays(-30);

	[ObservableProperty]
	public partial DateTime? ToDate { get; set; }

	[ObservableProperty]
	public partial bool ProblemOnly { get; set; } = true;

	[ObservableProperty]
	public partial ObservableCollection<LogisticsBatchListRow> Batches { get; set; } = [];

	[ObservableProperty]
	public partial LogisticsBatchListRow? SelectedBatch { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<LogisticsLineRow> Lines { get; set; } = [];

	[ObservableProperty]
	public partial LogisticsLineRow? SelectedLine { get; set; }

	[ObservableProperty]
	public partial string BatchSummaryText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string LineSummaryText { get; set; } = string.Empty;

	/// <summary>除外・訂正の理由</summary>
	[ObservableProperty]
	public partial string Reason { get; set; } = string.Empty;

	/// <summary>訂正版の編集中</summary>
	[ObservableProperty]
	public partial bool IsCorrecting { get; set; }

	/// <summary>訂正版の対象行（編集開始時の行）</summary>
	[ObservableProperty]
	public partial string CorrectionTitle { get; set; } = string.Empty;

	public ObservableCollection<LogisticsFieldRow> CorrectionFields { get; } = [];

	long correctingLineId;

	/// <summary>一覧の再取得中（選択変更での二重取得を防ぐ）</summary>
	bool reloading;

	bool IsSendBatch => SelectedBatch?.Batch.Direction == (int)EnumLogisticsDirection.Send;

	bool IsReceiveBatch => SelectedBatch?.Batch.Direction == (int)EnumLogisticsDirection.Receive;

	/// <summary>再出力・取消の対象（出荷指示・入荷予定の送信バッチ）</summary>
	bool IsActionableSendBatch => IsSendBatch && SelectedBatch!.Batch.DataKind is LogisticsDataKind.ORDER or LogisticsDataKind.STOCK;

	bool CanSearch() => !IsProcessing && SettingsLoaded;

	bool CanRewrite() => CanRunBase && IsActionableSendBatch && SelectedBatch!.Batch.Status == (int)EnumLogisticsSendStatus.PlaceFailed;

	bool CanCancelSend() => CanRunBase && IsActionableSendBatch
		&& SelectedBatch!.Batch.Status is (int)EnumLogisticsSendStatus.Placed or (int)EnumLogisticsSendStatus.PlaceFailed;

	bool CanRecheck() => CanRunBase && IsReceiveBatch
		&& SelectedBatch!.Batch.Status is (int)EnumLogisticsReceiveStatus.Imported or (int)EnumLogisticsReceiveStatus.PartialError;

	bool CanEditLine() => CanRunBase && IsReceiveBatch && SelectedLine?.IsEditable == true && !IsCorrecting;

	bool CanExclude() => CanEditLine() && !string.IsNullOrWhiteSpace(Reason);

	bool CanCommitCorrection() => CanRunBase && IsCorrecting && !string.IsNullOrWhiteSpace(Reason);

	bool CanCancelCorrection() => IsCorrecting && !IsProcessing;

	protected override void RefreshCommands() {
		SearchCommand.NotifyCanExecuteChanged();
		RewriteCommand.NotifyCanExecuteChanged();
		CancelSendCommand.NotifyCanExecuteChanged();
		RecheckCommand.NotifyCanExecuteChanged();
		ExcludeCommand.NotifyCanExecuteChanged();
		BeginCorrectionCommand.NotifyCanExecuteChanged();
		CommitCorrectionCommand.NotifyCanExecuteChanged();
		CancelCorrectionCommand.NotifyCanExecuteChanged();
	}

	partial void OnReasonChanged(string value) => RefreshCommands();

	partial void OnIsCorrectingChanged(bool value) => RefreshCommands();

	partial void OnSelectedLineChanged(LogisticsLineRow? value) => RefreshCommands();

	partial void OnSelectedBatchChanged(LogisticsBatchListRow? value) {
		EndCorrection();
		RefreshCommands();
		if (!reloading) {
			_ = LoadLinesAsync();
		}
	}

	[RelayCommand]
	async Task Init() {
		await LoadSettingsAsync();
		RefreshCommands();
		if (!SettingsLoaded) return;
		await SearchAsync(0);
		if (!HasUnusableReason) {
			StatusMessage = "条件を指定して「検索」を実行してください。バッチを選ぶと行の一覧を表示します。";
		}
	}

	[RelayCommand(CanExecute = nameof(CanSearch))]
	async Task Search() => await SearchAsync(SelectedBatch?.Id ?? 0);

	[RelayCommand(CanExecute = nameof(CanRewrite))]
	async Task Rewrite() {
		if (SelectedBatch is not { } batch) return;
		if (!Confirm($"送信バッチ{batch.Id}（{batch.KindName}）のファイルを再出力しますか？\n保存済みの送信行から同じファイル名で送信フォルダへ配置し直します。")) return;
		await RunBatchActionAsync(batch, EnumLogisticsBatchAction.Rewrite, "再出力");
	}

	[RelayCommand(CanExecute = nameof(CanCancelSend))]
	async Task CancelSend() {
		if (SelectedBatch is not { } batch) return;
		var message = new StringBuilder();
		message.AppendLine($"送信バッチ{batch.Id}（{batch.KindName} {batch.Batch.RowCount:N0}行）の送信を取り消しますか？");
		message.AppendLine("連携先が受け取っていないことを確認済みの場合だけ実行してください。");
		message.AppendLine("取り消すと対象は未送信に戻り、次回の送信で再び送られます。");
		if (!Confirm(message.ToString().TrimEnd())) return;
		await RunBatchActionAsync(batch, EnumLogisticsBatchAction.Cancel, "送信取消");
	}

	[RelayCommand(CanExecute = nameof(CanRecheck))]
	async Task Recheck() {
		if (SelectedBatch is not { } batch) return;
		var result = await CallAsync<LogisticsRunResult>(new LogisticsRecheckParam(batch.Id, 0), "再検査", isUpdate: true);
		await AfterActionAsync(batch.Id, result, "再検査");
	}

	[RelayCommand(CanExecute = nameof(CanExclude))]
	async Task Exclude() {
		if (SelectedBatch is not { } batch || SelectedLine is not { } line) return;
		if (!Confirm($"行No {line.LineNo} を除外しますか？\n除外した行は反映しません。\n理由: {Reason.Trim()}")) return;
		var result = await CallAsync<LogisticsRunResult>(
			new LogisticsLineActionParam(line.Id, EnumLogisticsLineAction.Exclude, [], Reason.Trim(), 0), "除外", isUpdate: true);
		if (result != null) {
			Reason = string.Empty;
		}
		await AfterActionAsync(batch.Id, result, "除外");
	}

	[RelayCommand(CanExecute = nameof(CanEditLine))]
	void BeginCorrection() {
		if (SelectedBatch is not { } batch || SelectedLine is not { } line) return;
		IReadOnlyList<string> columns;
		List<string> values;
		try {
			columns = LogisticsFileFormat.Columns(batch.Batch.DataKind);
			values = LogisticsFileFormat.Parse(batch.Batch.DataKind, line.RawText).FirstOrDefault()?.Fields.ToList() ?? [];
		}
		catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException) {
			MessageEx.ShowErrorDialog($"原文を項目に分解できませんでした。{ex.Message}", owner: ClientLib.GetActiveView(this));
			return;
		}
		CorrectionFields.Clear();
		for (var i = 0; i < columns.Count; i++) {
			var value = i < values.Count ? values[i] : string.Empty;
			CorrectionFields.Add(new LogisticsFieldRow { Header = columns[i], Original = value, Value = value });
		}
		correctingLineId = line.Id;
		CorrectionTitle = $"行No {line.LineNo} の訂正版"
			+ (values.Count != columns.Count ? $"（原文の項目数 {values.Count} が定義 {columns.Count} と違います）" : string.Empty);
		IsCorrecting = true;
		StatusMessage = "訂正する項目を直し、理由を入力して「訂正版を追加」を実行してください。原文は変更しません。";
	}

	[RelayCommand(CanExecute = nameof(CanCommitCorrection))]
	async Task CommitCorrection() {
		if (SelectedBatch is not { } batch || correctingLineId == 0) return;
		var changed = CorrectionFields.Where(f => f.IsChanged).Select(f => $"{f.Header}: {f.Original} → {f.Value}").ToList();
		if (changed.Count == 0) {
			MessageEx.ShowWarningDialog("訂正した項目がありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		var message = new StringBuilder();
		message.AppendLine("訂正版を追加しますか？元の行は訂正済みになり、訂正版を検査します。");
		foreach (var item in changed) {
			message.AppendLine($"・{item}");
		}
		if (!Confirm(message.ToString().TrimEnd())) return;
		string[] fields = [.. CorrectionFields.Select(f => (f.Value ?? string.Empty).Trim())];
		var result = await CallAsync<LogisticsRunResult>(
			new LogisticsLineActionParam(correctingLineId, EnumLogisticsLineAction.Correct, fields, Reason.Trim(), 0), "訂正版の追加", isUpdate: true);
		if (result != null) {
			EndCorrection();
			Reason = string.Empty;
		}
		await AfterActionAsync(batch.Id, result, "訂正版の追加");
	}

	[RelayCommand(CanExecute = nameof(CanCancelCorrection))]
	void CancelCorrection() {
		EndCorrection();
		StatusMessage = "訂正を取りやめました。";
	}

	void EndCorrection() {
		IsCorrecting = false;
		correctingLineId = 0;
		CorrectionTitle = string.Empty;
		CorrectionFields.Clear();
	}

	async Task RunBatchActionAsync(LogisticsBatchListRow batch, EnumLogisticsBatchAction action, string label) {
		var result = await CallAsync<LogisticsRunResult>(new LogisticsBatchActionParam(batch.Id, action, 0), label, isUpdate: true);
		await AfterActionAsync(batch.Id, result, label);
	}

	/// <summary>操作後に結果を表示し、一覧を取り直す（失敗時も状態が変わっている可能性があるので取り直す）</summary>
	async Task AfterActionAsync(long batchId, LogisticsRunResult? result, string label) {
		if (result != null) {
			ResultText = LogisticsText.RunResult(result);
		}
		var status = StatusMessage;
		await SearchAsync(batchId);
		StatusMessage = result != null ? $"{label}が完了しました。{result.Message}" : status;
	}

	async Task SearchAsync(long selectId) {
		var fromDay = FromDate is DateTime from ? from.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : string.Empty;
		var toDay = ToDate is DateTime to ? to.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : string.Empty;
		var rows = await CallAsync<List<LogisticsBatchRow>>(new LogisticsBatchQueryParam(Direction, Kind ?? string.Empty, fromDay, toDay, ProblemOnly), "検索", isUpdate: false);
		if (rows == null) return;
		var list = rows.OrderByDescending(r => r.Batch.Id).Select(r => new LogisticsBatchListRow(r)).ToList();
		reloading = true;
		try {
			Batches = new ObservableCollection<LogisticsBatchListRow>(list);
			SelectedBatch = list.FirstOrDefault(r => r.Id == selectId);
		}
		finally {
			reloading = false;
		}
		BatchSummaryText = $"{list.Count:N0}件";
		StatusMessage = list.Count == 0 ? "該当するバッチはありません。" : "検索が完了しました。";
		await LoadLinesAsync();
	}

	async Task LoadLinesAsync() {
		if (SelectedBatch is not { } batch) {
			SetLines([]);
			return;
		}
		var lines = await CallAsync<List<TranLogisticsLine>>(new LogisticsLineQueryParam(batch.Id), "行一覧の照会", isUpdate: false);
		if (SelectedBatch?.Id != batch.Id) return;
		SetLines(lines ?? []);
		StatusMessage = batch.Batch.Direction == (int)EnumLogisticsDirection.Receive
			? "未処理・エラーの行を選ぶと、除外・訂正版の追加ができます。"
			: "送信バッチの行を表示しています。";
	}

	void SetLines(List<TranLogisticsLine> lines) {
		Lines = new ObservableCollection<LogisticsLineRow>(lines.Select(l => new LogisticsLineRow(l)));
		SelectedLine = null;
		LineSummaryText = Lines.Count == 0
			? string.Empty
			: $"全 {Lines.Count:N0}行　未処理 {Lines.Count(l => l.Status == (int)EnumLogisticsLineStatus.Pending):N0}　エラー {Lines.Count(l => l.IsError):N0}　警告 {Lines.Count(l => l.IsWarning):N0}";
	}
}

/// <summary>L04 バッチ一覧の表示行</summary>
public sealed class LogisticsBatchListRow {
	public LogisticsBatchListRow(LogisticsBatchRow row) {
		Batch = row.Batch;
		ChangedAfterSend = row.ChangedAfterSend;
	}

	public TranLogisticsBatch Batch { get; }
	public int ChangedAfterSend { get; }
	public long Id => Batch.Id;
	public string DirectionName => LogisticsText.EnumText<EnumLogisticsDirection>(Batch.Direction);
	public string KindName => LogisticsDataKind.DisplayName(Batch.DataKind);
	public string FileName => Batch.FileName;
	public string StatusName => LogisticsText.BatchStatusText(Batch.Direction, Batch.Status);
	public int RowCount => Batch.RowCount;
	public int OkCount => Batch.OkCount;
	public int ErrorCount => Batch.ErrorCount;
	/// <summary>送信後変更あり（送信だけ表示）</summary>
	public string ChangedAfterSendText => Batch.Direction == (int)EnumLogisticsDirection.Send && ChangedAfterSend > 0 ? $"{ChangedAfterSend:N0}" : string.Empty;
	public string CreatedText => LogisticsText.LocalTime(Batch.Vdc);
	public string Memo => Batch.Memo;
	/// <summary>対応が必要なバッチ（配置失敗・取込失敗・エラー行あり・送信後変更あり）</summary>
	public bool IsProblem => Batch.Status is (int)EnumLogisticsSendStatus.PlaceFailed or (int)EnumLogisticsReceiveStatus.ImportFailed || Batch.ErrorCount > 0 || ChangedAfterSend > 0
		|| (Batch.Direction == (int)EnumLogisticsDirection.Receive && Batch.Status == (int)EnumLogisticsReceiveStatus.PartialError);
}
