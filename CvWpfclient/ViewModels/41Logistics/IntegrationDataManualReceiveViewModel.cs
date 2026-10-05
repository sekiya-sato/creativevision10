using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._41Logistics;

/// <summary>
/// L03 物流連携 連携データ手動受信。仕様は `Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md` 4章・7章。
/// 取込・検査（在庫・伝票は変えない）→ 行一覧で確認 → 反映（エラー行を除いて伝票作成・在庫更新）。
/// </summary>
public partial class IntegrationDataManualReceiveViewModel : LogisticsViewModelBase {
	/// <summary>受信フォルダの未取込ファイル</summary>
	[ObservableProperty]
	public partial ObservableCollection<LogisticsReceiveFileRow> ReceiveFiles { get; set; } = [];

	/// <summary>PCから選んだファイル（あれば受信フォルダは読まない）</summary>
	List<LogisticsUploadFile> uploads = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasUploads))]
	public partial string UploadText { get; set; } = string.Empty;

	public bool HasUploads => uploads.Count > 0;

	/// <summary>受信バッチの選択肢（未反映）</summary>
	[ObservableProperty]
	public partial ObservableCollection<LogisticsBatchOption> Batches { get; set; } = [];

	[ObservableProperty]
	public partial LogisticsBatchOption? SelectedBatch { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<LogisticsLineRow> Lines { get; set; } = [];

	[ObservableProperty]
	public partial string LineSummaryText { get; set; } = string.Empty;

	/// <summary>反映対象（未処理）の行数</summary>
	[ObservableProperty]
	public partial int PendingCount { get; set; }

	/// <summary>一覧の再取得中（選択変更での二重取得を防ぐ）</summary>
	bool reloading;

	bool CanImport() => CanRunBase && (HasUploads || ReceiveFiles.Any(f => f.IsChecked));

	bool CanRefreshFiles() => CanRunBase;

	bool CanPickFiles() => !IsProcessing && SettingsLoaded && !HasUnusableReason;

	bool CanClearUploads() => !IsProcessing && HasUploads;

	// 反映済みのバッチ（未処理・エラーの行が無い）は再検査の対象が無いので押せないようにする
	bool CanRecheck() => CanRunBase && SelectedBatch != null && Lines.Any(l => l.Status is (int)EnumLogisticsLineStatus.Pending or (int)EnumLogisticsLineStatus.Error);

	bool CanApply() => CanRunBase && SelectedBatch != null && PendingCount > 0;

	protected override void RefreshCommands() {
		ImportCommand.NotifyCanExecuteChanged();
		RefreshFilesCommand.NotifyCanExecuteChanged();
		PickFilesCommand.NotifyCanExecuteChanged();
		ClearUploadsCommand.NotifyCanExecuteChanged();
		RecheckCommand.NotifyCanExecuteChanged();
		ApplyCommand.NotifyCanExecuteChanged();
	}

	partial void OnPendingCountChanged(int value) => RefreshCommands();

	partial void OnSelectedBatchChanged(LogisticsBatchOption? value) {
		RefreshCommands();
		if (!reloading) {
			_ = LoadLinesAsync();
		}
	}

	[RelayCommand]
	async Task Init() {
		if (!await LoadSettingsAsync()) {
			RefreshCommands();
			return;
		}
		if (HasUnusableReason) {
			RefreshCommands();
			return;
		}
		await LoadFilesAsync();
		await LoadBatchesAsync(0);
		StatusMessage = "取り込むファイルを選んで「取込・検査」を実行してください。";
	}

	[RelayCommand(CanExecute = nameof(CanRefreshFiles))]
	async Task RefreshFiles() => await LoadFilesAsync();

	[RelayCommand(CanExecute = nameof(CanPickFiles))]
	void PickFiles() {
		var dialog = new OpenFileDialog {
			Title = "取り込むファイルを選択",
			Filter = "連携ファイル (*.csv;*.txt)|*.csv;*.txt|すべてのファイル (*.*)|*.*",
			Multiselect = true,
		};
		var owner = ClientLib.GetActiveView(this);
		if ((owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != true) return;
		try {
			uploads = [.. dialog.FileNames.Select(path => new LogisticsUploadFile(Path.GetFileName(path), File.ReadAllBytes(path)))];
		}
		catch (Exception ex) {
			uploads = [];
			MessageEx.ShowErrorDialog($"ファイルを読み込めませんでした。{ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		UpdateUploadText();
	}

	[RelayCommand(CanExecute = nameof(CanClearUploads))]
	void ClearUploads() {
		uploads = [];
		UpdateUploadText();
	}

	[RelayCommand(CanExecute = nameof(CanImport))]
	async Task Import() {
		string[] fileNames = HasUploads ? [] : [.. ReceiveFiles.Where(f => f.IsChecked).Select(f => f.FileName)];
		var result = await CallAsync<LogisticsRunResult>(new LogisticsReceiveImportParam(fileNames, [.. uploads], 0), "取込・検査", isUpdate: true);
		if (result == null) {
			await LoadFilesAsync();
			return;
		}
		ResultText = LogisticsText.RunResult(result);
		var summary = result.Message ?? string.Empty;
		if (HasUploads) {
			uploads = [];
			UpdateUploadText();
		}
		await LoadFilesAsync();
		var firstBatch = (result.Kinds ?? []).FirstOrDefault(k => k.BatchId > 0)?.BatchId ?? 0;
		await LoadBatchesAsync(firstBatch);
		StatusMessage = $"取込・検査が完了しました。{summary}";
	}

	[RelayCommand(CanExecute = nameof(CanRecheck))]
	async Task Recheck() {
		if (SelectedBatch is not { } batch) return;
		var result = await CallAsync<LogisticsRunResult>(new LogisticsRecheckParam(batch.Id, 0), "再検査", isUpdate: true);
		if (result == null) return;
		ResultText = LogisticsText.RunResult(result);
		var summary = result.Message ?? string.Empty;
		await LoadBatchesAsync(batch.Id);
		StatusMessage = $"再検査が完了しました。{summary}";
	}

	[RelayCommand(CanExecute = nameof(CanApply))]
	async Task Apply() {
		if (SelectedBatch is not { } batch) return;
		var message = new StringBuilder();
		message.AppendLine($"バッチ{batch.Id}（{batch.KindName}）の未処理 {PendingCount:N0}行を反映しますか？");
		message.AppendLine("エラー行を除いて反映し、伝票作成・在庫更新を行います。");
		var errors = Lines.Count(l => l.IsError);
		if (errors > 0) {
			message.AppendLine($"エラー {errors:N0}行は反映しません（連携エラーデータ照会で除外・訂正できます）。");
		}
		if (!Confirm(message.ToString().TrimEnd())) return;

		var result = await CallAsync<LogisticsRunResult>(new LogisticsApplyParam(batch.Id, 0), "反映", isUpdate: true);
		if (result == null) {
			await LoadBatchesAsync(batch.Id);
			return;
		}
		ResultText = LogisticsText.RunResult(result);
		var summary = result.Message ?? string.Empty;
		await LoadBatchesAsync(batch.Id);
		StatusMessage = $"反映が完了しました。{summary}";
	}

	async Task LoadFilesAsync() {
		var files = await CallAsync<List<LogisticsReceiveFileInfo>>(new LogisticsReceiveFilesQueryParam(), "受信フォルダの照会", isUpdate: false);
		foreach (var row in ReceiveFiles) {
			row.PropertyChanged -= OnFilePropertyChanged;
		}
		var list = new ObservableCollection<LogisticsReceiveFileRow>((files ?? []).Select(f => new LogisticsReceiveFileRow(f)));
		foreach (var row in list) {
			row.PropertyChanged += OnFilePropertyChanged;
		}
		ReceiveFiles = list;
		RefreshCommands();
	}

	void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(LogisticsReceiveFileRow.IsChecked)) {
			ImportCommand.NotifyCanExecuteChanged();
		}
	}

	/// <summary>受信バッチの選択肢を取り直す（未反映。<paramref name="selectId"/> は反映済みでも残す）</summary>
	async Task LoadBatchesAsync(long selectId) {
		var rows = await CallAsync<List<LogisticsBatchRow>>(
			new LogisticsBatchQueryParam((int)EnumLogisticsDirection.Receive, string.Empty, string.Empty, string.Empty, false), "受信バッチの照会", isUpdate: false);
		var options = (rows ?? [])
			.Where(r => r.Batch.Status is (int)EnumLogisticsReceiveStatus.Imported or (int)EnumLogisticsReceiveStatus.PartialError || r.Batch.Id == selectId)
			.OrderByDescending(r => r.Batch.Id)
			.Select(r => new LogisticsBatchOption(r.Batch))
			.ToList();
		reloading = true;
		try {
			Batches = new ObservableCollection<LogisticsBatchOption>(options);
			SelectedBatch = options.FirstOrDefault(o => o.Id == selectId) ?? options.FirstOrDefault();
		}
		finally {
			reloading = false;
		}
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
	}

	void SetLines(List<TranLogisticsLine> lines) {
		Lines = new ObservableCollection<LogisticsLineRow>(lines.Select(l => new LogisticsLineRow(l)));
		PendingCount = Lines.Count(l => l.Status == (int)EnumLogisticsLineStatus.Pending);
		LineSummaryText = Lines.Count == 0
			? string.Empty
			: $"全 {Lines.Count:N0}行　未処理 {PendingCount:N0}　エラー {Lines.Count(l => l.IsError):N0}　警告 {Lines.Count(l => l.IsWarning):N0}　適用済み {Lines.Count(l => l.Status == (int)EnumLogisticsLineStatus.Applied):N0}";
		RefreshCommands();
	}

	void UpdateUploadText() {
		UploadText = uploads.Count == 0 ? string.Empty : $"選択したファイル {uploads.Count:N0}件: {string.Join("、", uploads.Select(u => u.FileName))}";
		RefreshCommands();
	}
}

/// <summary>受信フォルダのファイルの表示行</summary>
public sealed partial class LogisticsReceiveFileRow : ObservableObject {
	public LogisticsReceiveFileRow(LogisticsReceiveFileInfo info) {
		Info = info;
		IsChecked = true;
	}

	public LogisticsReceiveFileInfo Info { get; }

	[ObservableProperty]
	public partial bool IsChecked { get; set; }

	public string FileName => Info.FileName;
	public string KindName => string.IsNullOrEmpty(Info.Kind) ? "(種別不明)" : LogisticsDataKind.DisplayName(Info.Kind);
	public long Size => Info.Size;
	public string LastWrite => Info.LastWrite;
}

/// <summary>バッチの選択肢</summary>
public sealed class LogisticsBatchOption {
	public LogisticsBatchOption(TranLogisticsBatch batch) {
		Batch = batch;
	}

	public TranLogisticsBatch Batch { get; }
	public long Id => Batch.Id;
	public string KindName => LogisticsDataKind.DisplayName(Batch.DataKind);
	public string DisplayText =>
		$"{Batch.Id}  {KindName}  {Batch.FileName}  [{LogisticsText.BatchStatusText(Batch.Direction, Batch.Status)}]  {Batch.RowCount:N0}行 エラー{Batch.ErrorCount:N0}";
}
