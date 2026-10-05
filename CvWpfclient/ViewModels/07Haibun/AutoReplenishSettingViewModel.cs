using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CvWpfclient.ViewModels._07Haibun;

// 設定だけを編集し、数量計算・FK検証・競合判定は専用サーバー処理に任せる。
public abstract partial class AutoReplenishSettingViewModel : BaseViewModel {
	protected abstract bool IsStockSetting { get; }
	long editId;
	long expectedVdu;
	[ObservableProperty] public partial ObservableCollection<AutoReplenishSettingRow> Rows { get; set; } = [];
	[ObservableProperty] public partial AutoReplenishSettingRow? SelectedRow { get; set; }
	[ObservableProperty] public partial MasterTokui? Soko { get; set; }
	[ObservableProperty] public partial MasterTokui? Tenpo { get; set; }
	[ObservableProperty] public partial MasterShohin? Shohin { get; set; }
	[ObservableProperty] public partial ObservableCollection<MasterShohinColSiz> Skus { get; set; } = [];
	[ObservableProperty] public partial MasterShohinColSiz? SelectedSku { get; set; }
	[ObservableProperty] public partial string TargetSuText { get; set; } = "0";
	[ObservableProperty] public partial string PriorityText { get; set; } = "0";
	[ObservableProperty] public partial bool FlagValue { get; set; }
	[ObservableProperty] public partial bool IsBusy { get; set; }
	[ObservableProperty] public partial string Message { get; set; } = string.Empty;
	[ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))] public partial bool IsSaveOutcomeUnknown { get; set; }
	[ObservableProperty] public partial string EditStatus { get; set; } = "新規";

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Init(CancellationToken ct) => await Refresh(ct);

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Refresh(CancellationToken ct) {
		await RunAsync(async () => {
			await LoadRowsAsync(ct);
			IsSaveOutcomeUnknown = false;
			Message = $"{Rows.Count:N0}件を取得しました。行を選択して［読込］してください。";
		});
	}

	async Task LoadRowsAsync(CancellationToken ct) {
		var result = await SendAsync(new(0, string.Empty, AutoReplenishOperation.LoadSettings), ct);
		var stock = result.StockSettings;
		var exclude = result.ExcludeSettings;
		var tokuiIds = IsStockSetting ? stock.SelectMany(x => new[] { x.Id_Soko, x.Id_Tenpo }) : exclude.Select(x => x.Id_Soko);
		var productIds = IsStockSetting ? stock.Select(x => x.Id_Shohin) : exclude.Select(x => x.Id_Shohin);
		var tokuiMap = (await LoadIdsAsync<MasterTokui>(tokuiIds, ct)).ToDictionary(x => x.Id);
		var productMap = (await LoadIdsAsync<MasterShohin>(productIds, ct)).ToDictionary(x => x.Id);
		string Place(long id) => tokuiMap.TryGetValue(id, out var value) ? $"{value.Code} {value.Name}" : $"Id:{id}";
		AutoReplenishSettingRow Make(BaseDbClass item, long soko, long tenpo, long product, long col, long siz, int target, int priority, int flag) {
			productMap.TryGetValue(product, out var value);
			var sku = value?.Jcolsiz?.FirstOrDefault(x => x.Id_Col == col && x.Id_Siz == siz);
			return new(item, Place(soko), tenpo == 0 ? string.Empty : Place(tenpo),
				value == null ? $"Id:{product}" : $"{value.Code} {value.Name}",
				sku == null ? $"色Id:{col} サイズId:{siz}" : $"{sku.Code_Col} {sku.Mei_Col} / {sku.Code_Siz} {sku.Mei_Siz}", target, priority, flag == 1);
		}
		Rows = IsStockSetting
			? new(stock.Select(x => Make(x, x.Id_Soko, x.Id_Tenpo, x.Id_Shohin, x.Id_Col, x.Id_Siz, x.TargetSu, x.Priority, x.Enabled)))
			: new(exclude.Select(x => Make(x, x.Id_Soko, 0, x.Id_Shohin, x.Id_Col, x.Id_Siz, 0, 0, x.Excluded)));
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task LoadSelected(CancellationToken ct) {
		if (SelectedRow == null) { Message = "一覧から行を選択してください。"; return; }
		var item = SelectedRow.Item;
		await RunAsync(async () => {
			long soko, tenpo, product, col, siz;
			if (item is MasterAutoReplenishStock x) {
				(soko, tenpo, product, col, siz) = (x.Id_Soko, x.Id_Tenpo, x.Id_Shohin, x.Id_Col, x.Id_Siz);
				TargetSuText = x.TargetSu.ToString(CultureInfo.InvariantCulture);
				PriorityText = x.Priority.ToString(CultureInfo.InvariantCulture);
				FlagValue = x.Enabled == 1;
			} else if (item is MasterAutoReplenishExclude y) {
				(soko, tenpo, product, col, siz) = (y.Id_Soko, 0, y.Id_Shohin, y.Id_Col, y.Id_Siz);
				FlagValue = y.Excluded == 1;
			} else { return; }
			var places = await LoadIdsAsync<MasterTokui>([soko, tenpo], ct);
			Soko = places.FirstOrDefault(x => x.Id == soko);
			Tenpo = places.FirstOrDefault(x => x.Id == tenpo);
			SetShohin((await LoadIdsAsync<MasterShohin>([product], ct)).FirstOrDefault());
			SelectedSku = Skus.FirstOrDefault(x => x.Id_Col == col && x.Id_Siz == siz);
			editId = item.Id;
			expectedVdu = item.Vdu;
			EditStatus = $"編集中 Id:{editId}";
			Message = "読込しました。変更後に［保存］してください。";
		});
	}

	[RelayCommand]
	void New() {
		editId = expectedVdu = 0;
		Soko = null;
		Tenpo = null;
		SetShohin(null);
		TargetSuText = PriorityText = "0";
		FlagValue = !IsStockSetting;
		EditStatus = "新規";
		Message = IsStockSetting ? "新規設定は無効です。必要な場合は［有効］にしてください。" : "除外する倉庫・SKUを選択してください。";
	}

	[RelayCommand]
	void SelectSoko() { var selected = Select<MasterTokui>("TenType=0"); if (selected != null) Soko = selected; }
	[RelayCommand]
	void SelectTenpo() { var selected = Select<MasterTokui>("TenType=6"); if (selected != null) Tenpo = selected; }
	[RelayCommand(IncludeCancelCommand = true)]
	async Task SelectShohin(CancellationToken ct) {
		var selected = Select<MasterShohin>(string.Empty);
		if (selected == null) return;
		// 選択画面は名称列だけを取得するためSKUを含む商品を再読込する。
		await RunAsync(async () => SetShohin((await LoadIdsAsync<MasterShohin>([selected.Id], ct)).FirstOrDefault()));
	}

	void SetShohin(MasterShohin? value) {
		Shohin = value;
		Skus = new(value?.Jcolsiz ?? []);
		SelectedSku = Skus.FirstOrDefault();
	}

	T? Select<T>(string where) where T : BaseDbClass {
		var window = new Views.Sub.SelectWinView();
		if (window.DataContext is not SelectWinViewModel vm) return null;
		vm.SetParam(typeof(T), where, "Code");
		return ClientLib.ShowDialogView(window, this) == true ? vm.Current as T : null;
	}

	[RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanSave))]
	async Task Save(CancellationToken ct) {
		if (Soko == null || Shohin == null || SelectedSku == null || (IsStockSetting && Tenpo == null)) {
			Message = "倉庫・商品・SKUと対象店舗を選択してください。";
			return;
		}
		if (!int.TryParse(TargetSuText, out var target) || target < 0 || !int.TryParse(PriorityText, out var priority) || priority < 0) {
			Message = "基準数・優先順位は0以上の整数で入力してください。";
			return;
		}
		await RunAsync(async () => {
			var parameter = new AutoReplenishParam(Soko.Id, string.Empty,
				IsStockSetting ? AutoReplenishOperation.SaveStockSetting : AutoReplenishOperation.SaveExcludeSetting,
				ExpectedVdu: expectedVdu) {
				StockSetting = IsStockSetting ? new() { Id = editId, Vdu = expectedVdu, Id_Soko = Soko.Id, Id_Tenpo = Tenpo!.Id,
					Id_Shohin = Shohin.Id, Id_Col = SelectedSku.Id_Col, Id_Siz = SelectedSku.Id_Siz, TargetSu = target, Priority = priority, Enabled = FlagValue ? 1 : 0 } : null,
				ExcludeSetting = !IsStockSetting ? new() { Id = editId, Vdu = expectedVdu, Id_Soko = Soko.Id,
					Id_Shohin = Shohin.Id, Id_Col = SelectedSku.Id_Col, Id_Siz = SelectedSku.Id_Siz, Excluded = FlagValue ? 1 : 0 } : null
			};
			ct.ThrowIfCancellationRequested();
			try {
				// 保存開始後は応答まで待ち、クライアント取消で成功を取り落とさない。
				await SendAsync(parameter, CancellationToken.None);
			} catch (Exception ex) when (ex is not ServerSettingException) {
				IsSaveOutcomeUnknown = true;
				throw new InvalidOperationException("保存結果が不明です。再送せず［一覧更新］で既存設定を確認してください。" + ex.Message, ex);
			}
			// 再保存時に旧Vduを使わないよう新規状態に戻し、一覧を取り直す。
			New();
			await LoadRowsAsync(CancellationToken.None);
			Message = "保存しました。続けて編集する場合は一覧から読込してください。";
		});
	}

	bool CanSave() => !IsSaveOutcomeUnknown;

	static async Task<List<T>> LoadIdsAsync<T>(IEnumerable<long> ids, CancellationToken ct) {
		var values = ids.Where(x => x > 0).Distinct().ToArray();
		if (values.Length == 0) return [];
		return await CoreServiceClient.QuerySqlListAsync<T>($"SELECT * FROM {typeof(T).Name} WHERE Id IN ({string.Join(",", values)})", [], ct);
	}

	async Task<AutoReplenishResult> SendAsync(AutoReplenishParam parameter, CancellationToken ct) {
		var reply = await CoreServiceClient.SendExecuteAsync(parameter, ct);
		ct.ThrowIfCancellationRequested();
		if (reply.Code < 0) throw new ServerSettingException(string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option);
		var result = Common.DeserializeObject<AutoReplenishResult>(reply.DataMsg)
			?? throw new InvalidOperationException("設定の応答を取得できませんでした。");
		if (result.Errors.Count > 0) throw new ServerSettingException(string.Join(Environment.NewLine, result.Errors));
		return result;
	}

	sealed class ServerSettingException(string message) : Exception(message) { }

	async Task RunAsync(Func<Task> action) {
		if (IsBusy) return;
		IsBusy = true;
		try { await action(); }
		catch (OperationCanceledException) { Message = "処理をキャンセルしました。"; }
		catch (Exception ex) { Message = ex.Message; }
		finally { IsBusy = false; }
	}
}

public sealed record AutoReplenishSettingRow(BaseDbClass Item, string SokoDisplay, string TenpoDisplay,
	string ShohinDisplay, string SkuDisplay, int TargetSu, int Priority, bool FlagValue);
