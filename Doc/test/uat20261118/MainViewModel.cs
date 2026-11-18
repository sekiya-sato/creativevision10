using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace UatAvalonia;

public interface IPrototypeDialogs {
	Task<bool> ConfirmAsync(string message);
	Task<string?> SelectSupplierAsync();
	Task<bool> ShowProductSearchAsync();
}

public partial class MainViewModel(DummyProductService service, IPrototypeDialogs dialogs) : ObservableObject {
	public ObservableCollection<ProductRow> Rows { get; } = [];
	public ObservableCollection<DetailRow> Details { get; } = [new(), new() { Color = "白", Size = "L", Quantity = 2 }];
	public string[] Categories { get; } = ["すべて", "定番", "季節"];
	public string[] EditCategories { get; } = ["定番", "季節"];
	[ObservableProperty] public partial string Filter { get; set; } = "";
	[ObservableProperty] public partial string Category { get; set; } = "すべて";
	[ObservableProperty] public partial ProductRow? Selected { get; set; }
	[ObservableProperty] public partial ProductRow Edit { get; set; } = new();
	[ObservableProperty] public partial string Status { get; set; } = "ダミー応答のみ・保存はメモリ内 / F5で検索";
	[ObservableProperty] public partial bool IsBusy { get; set; }
	[ObservableProperty] public partial bool SimulateError { get; set; }
	[ObservableProperty] public partial bool DarkTheme { get; set; }
	[ObservableProperty] public partial int SelectedTab { get; set; }
	[ObservableProperty] public partial string Request { get; set; } = "未実行";
	private CancellationTokenSource? operation;
	private CancellationTokenSource listLifetime = new();
	private readonly HashSet<ProductRow> pendingRows = [];
	private bool loadingDetails;
	public Task DetailsTask { get; private set; } = Task.CompletedTask;
	[ObservableProperty] public partial int FullLoadedCount { get; set; }
	[ObservableProperty] public partial string DetailStatus { get; set; } = "基本情報を全件取得 → 表示行だけ詳細取得";
	public bool CanWrite => !IsBusy && Selected?.IsFullLoaded == true;
	public bool CanEdit => !IsBusy && (Selected == null || Selected.IsFullLoaded);
	public bool CanStart => !IsBusy;
	partial void OnIsBusyChanged(bool value) {
		OnPropertyChanged(nameof(CanWrite));
		OnPropertyChanged(nameof(CanEdit));
		OnPropertyChanged(nameof(CanStart));
	}
	partial void OnSelectedChanged(ProductRow? value) {
		Edit = value?.IsFullLoaded == true ? value.Copy() : new();
		OnPropertyChanged(nameof(CanWrite));
		OnPropertyChanged(nameof(CanEdit));
		if (value?.IsFullLoaded == false) RequestFullRow(value);
	}
	partial void OnFilterChanged(string value) => Invalidate();
	partial void OnCategoryChanged(string value) => Invalidate();
	private void Invalidate() {
		operation?.Cancel();
		ResetDetails();
		Rows.Clear();
		Selected = null;
		Status = "条件が変わりました。F5で再検索してください。";
	}

	[RelayCommand]
	private Task SearchAsync() => RunAsync(async ct => {
		if (!await dialogs.ShowProductSearchAsync()) return;
		ct.ThrowIfCancellationRequested();
		if (SimulateError) {
			await Task.Delay(700, ct);
			throw new InvalidOperationException("ダミー通信エラー（実通信なし）");
		}
		await LoadListCoreAsync(ct);
	}, honorSimulateError: false);

	public Task LoadInitialAsync() => RunAsync(LoadListCoreAsync);
	private async Task LoadListCoreAsync(CancellationToken ct) {
		ResetDetails();
		Rows.Clear();
		Selected = null;
		Request = $"ダミー検索: 分類={Category}, 検索語={Filter}";
		var result = await service.SearchAsync(Filter, Category, ct);
		Rows.Clear();
		foreach (var row in result) Rows.Add(row);
		Selected = Rows.FirstOrDefault();
		Status = $"基本情報 {Rows.Count} 件取得。表示行の詳細を追加取得します（ダミー）";
	}

	private void ResetDetails() {
		listLifetime.Cancel();
		listLifetime.Dispose();
		listLifetime = new();
		pendingRows.Clear();
		FullLoadedCount = 0;
		DetailStatus = "基本情報を全件取得 → 表示行だけ詳細取得";
	}

	public void RequestFullRow(ProductRow row) {
		if (row.IsFullLoaded || row.LoadState == "詳細取得中…" || !Rows.Contains(row)) return;
		row.LoadState = "詳細取得中…";
		pendingRows.Add(row);
		if (!loadingDetails) DetailsTask = LoadPendingRowsAsync();
	}

	private async Task LoadPendingRowsAsync() {
		loadingDetails = true;
		try {
			while (pendingRows.Count > 0) {
				var ct = listLifetime.Token;
				try {
					// 同じ描画で準備される行をまとめ、行ごとのダミー要求を抑える。
					await Task.Delay(80, ct);
					var batch = pendingRows.ToArray();
					pendingRows.Clear();
					DetailStatus = $"表示行 {batch.Length} 件の詳細を取得中…";
					var full = await service.LoadFullAsync(batch.Select(x => x.Code), ct);
					ct.ThrowIfCancellationRequested();
					foreach (var target in batch) {
						if (!Rows.Contains(target)) continue;
						var source = full.FirstOrDefault(x => x.Code == target.Code);
						if (source == null) { target.LoadState = "対象なし"; continue; }
						target.Supplier = source.Supplier; target.Price = source.Price;
						target.Enabled = source.Enabled; target.StartDate = source.StartDate; target.Memo = source.Memo;
						target.IsFullLoaded = true; target.LoadState = "詳細取得済み";
						FullLoadedCount++;
						// 編集中コピーを上書きしない。未取得の選択行が完成したときだけ反映する。
						if (ReferenceEquals(Selected, target)) OnSelectedChanged(target);
					}
					DetailStatus = $"基本 {Rows.Count} 件 / 詳細 {FullLoadedCount} 件取得済み（戻りスクロールは再取得なし）";
				}
				catch (OperationCanceledException) { /* 旧一覧の応答を新一覧に反映しない。 */ }
			}
		}
		finally { loadingDetails = false; }
	}

	public void StopLoading() {
		operation?.Cancel();
		listLifetime.Cancel();
		pendingRows.Clear();
	}

	[RelayCommand]
	private Task UpdateAsync() => SaveAsync(false);
	[RelayCommand]
	private Task InsertAsync() => SaveAsync(true);
	private Task SaveAsync(bool insert) => RunAsync(async ct => {
		if (!insert && Selected?.IsFullLoaded != true) { Status = "詳細取得済みの修正対象を選択してください。"; return; }
		if (string.IsNullOrWhiteSpace(Edit.Code) || string.IsNullOrWhiteSpace(Edit.Name) || Edit.Price < 0) {
			Status = "コード・商品名は必須、単価は0以上です。";
			return;
		}
		if (!insert && Edit.Code != Selected!.Code) { Status = "修正時はコードを変更できません。"; return; }
		if (!await dialogs.ConfirmAsync($"{Edit.Code} {Edit.Name} を{(insert ? "追加" : "修正")}しますか？（メモリ内のみ）")) return;
		ct.ThrowIfCancellationRequested();
		Request = $"ダミー{(insert ? "追加" : "修正")}: {Edit.Code}, {Edit.Name}, {Edit.Price:N0}";
		await service.SaveAsync(Edit.Copy(), insert, ct);
		ResetDetails();
		Rows.Clear();
		Selected = null;
		Status = "メモリ内に保存しました。再検索してください。";
	});

	[RelayCommand]
	private Task DeleteAsync() => RunAsync(async ct => {
		if (Selected?.IsFullLoaded != true) return;
		var code = Selected.Code;
		if (!await dialogs.ConfirmAsync($"{code} を削除しますか？（メモリ内のみ）")) return;
		ct.ThrowIfCancellationRequested();
		Request = $"ダミー削除: {code}";
		await service.DeleteAsync(code, ct);
		ResetDetails();
		Rows.Clear();
		Selected = null;
		Status = "メモリ内から削除しました。再検索してください。";
	});

	[RelayCommand]
	private Task SelectSupplierAsync() => RunAsync(async ct => {
		var result = await dialogs.SelectSupplierAsync();
		ct.ThrowIfCancellationRequested();
		if (result != null) Edit.Supplier = result;
	});

	[RelayCommand]
	private void AddDetail() => Details.Add(new());
	[RelayCommand]
	private void Cancel() => operation?.Cancel();
	[RelayCommand]
	private void Print() => Status = "印刷要求はダミーです。PDF生成・表示・発行済更新は未実装です。";

	private async Task RunAsync(Func<CancellationToken, Task> action, bool honorSimulateError = true) {
		if (IsBusy) return;
		using var source = new CancellationTokenSource();
		operation = source;
		IsBusy = true;
		Status = "ダミー応答を待っています…";
		try {
			if (honorSimulateError && SimulateError) {
				await Task.Delay(700, source.Token);
				throw new InvalidOperationException("ダミー通信エラー（実通信なし）");
			}
			await action(source.Token);
			if (Status == "ダミー応答を待っています…") Status = "完了または確認を取り消しました。";
		}
		catch (OperationCanceledException) { Status = "処理を取り消しました。"; }
		catch (InvalidOperationException ex) { Status = ex.Message; }
		finally { operation = null; IsBusy = false; }
	}
}
