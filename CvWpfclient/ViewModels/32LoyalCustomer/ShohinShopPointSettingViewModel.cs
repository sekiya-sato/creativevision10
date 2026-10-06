using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Microsoft.Win32;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>
/// 商品店舗別ポイント設定: 優先区分「商品全店」「商品店別」のキャンペーンへ対象商品・対象店舗を設定する。
/// 商品全店は店舗一覧を無効にして店舗を送らない。商品はコード入力・一覧選択・CSV取込(1行1商品コード)で追加する。
/// </summary>
public partial class ShohinShopPointSettingViewModel : PointCampaignTargetViewModelBase {
	public override string Title => "商品店舗別ポイント設定";
	protected override int[] PriorityTypes { get; } = [(int)EnumPointCampaignPriority.ShohinAllShops, (int)EnumPointCampaignPriority.ShohinShop];

	public ObservableCollection<PointCampaignShohinRow> Shohins { get; } = [];

	[ObservableProperty]
	public partial string ShohinCodeInput { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int ShohinCount { get; set; }

	public ShohinShopPointSettingViewModel() {
		Shohins.CollectionChanged += (_, _) => {
			// 一括追加・読込中は BatchUpdate の最後に1回だけ再計算する。
			if (IsRecalcDeferred) return;
			ShohinCount = Shohins.Count;
			UpdateDirty();
		};
	}

	protected override void OnTargetsRecalculated() => ShohinCount = Shohins.Count;

	protected override IEnumerable<long> CurrentShohinIds() => Shohins.Select(x => x.Id);

	protected override void ClearShohins() => Shohins.Clear();

	protected override async Task<IReadOnlyCollection<long>> LoadShohinsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) {
		var rows = new List<MasterShohin>();
		foreach (var chunk in ids.Distinct().Chunk(500))
			rows.AddRange(await QueryListAsync<MasterShohin>(new QueryListParam(typeof(MasterShohin), $"Id IN ({string.Join(",", chunk)})"), ct));
		BatchUpdate(() => {
			Shohins.Clear();
			foreach (var s in rows.OrderBy(x => x.Code, StringComparer.Ordinal)) Shohins.Add(new PointCampaignShohinRow(s.Id, s.Code, s.Name));
		});
		return [.. rows.Select(x => x.Id)];
	}

	protected override bool ValidateBeforeSave(List<long> shopIds, List<long> shohinIds) {
		if (shohinIds.Count == 0 || (IsShopEnabled && shopIds.Count == 0)) {
			// 対象未設定は保存できるが適用されないため、意図を確認する。
			var lack = shohinIds.Count == 0 ? "対象商品" : "対象店舗";
			return MessageEx.ShowQuestionDialog($"{lack}が未設定です。このままではキャンペーンは適用されません。登録を続けますか？", owner: ActiveWindow) == System.Windows.MessageBoxResult.Yes;
		}
		return true;
	}

	/// <summary>商品コードを入力して追加する。</summary>
	[RelayCommand(CanExecute = nameof(CanEditShohins))]
	async Task AddShohin(CancellationToken ct) {
		var code = (ShohinCodeInput ?? string.Empty).Trim();
		if (code.Length == 0) {
			Message = "商品コードを入力してください。";
			return;
		}
		IsBusy = true;
		try {
			var found = await FindShohinsByCodeAsync([code], ct);
			if (!found.TryGetValue(code, out var shohin)) {
				Message = $"商品コード {code} は登録されていません。";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}
			Message = AddRows([shohin]) == 0 ? $"商品 {code} は追加済みです。" : $"商品 {code} {shohin.Name} を追加しました。";
			ShohinCodeInput = string.Empty;
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"商品の取得失敗: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	/// <summary>商品検索画面から選択して追加する。</summary>
	[RelayCommand(CanExecute = nameof(CanEditShohins))]
	void SelectShohin() {
		var win = new Views.Sub.SelectShohinView();
		if (win.DataContext is not SelectShohinViewModel vm) return;
		vm.ShohinCodeFrom = (ShohinCodeInput ?? string.Empty).Trim();
		if (ClientLib.ShowDialogView(win, this) != true || vm.SelectedShohin is not { } shohin) return;
		Message = AddRows([shohin]) == 0 ? $"商品 {shohin.Code} は追加済みです。" : $"商品 {shohin.Code} {shohin.Name} を追加しました。";
	}

	/// <summary>CSV(1行1商品コード、UTF-8)を取り込む。未登録コードは追加せず一覧表示する。</summary>
	[RelayCommand(CanExecute = nameof(CanEditShohins))]
	async Task ImportCsv(CancellationToken ct) {
		var dialog = new OpenFileDialog {
			Title = "対象商品CSVを選択（1行1商品コード）",
			Filter = "CSVファイル (*.csv)|*.csv|テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false,
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;
		await ImportCsvFileAsync(dialog.FileName, ct);
	}

	/// <summary>CSVファイルを読み、登録済みコードを追加する（UAT からも直接呼ぶ）。</summary>
	public async Task ImportCsvFileAsync(string path, CancellationToken ct) {
		if (Target == null) return;
		IsBusy = true;
		try {
			var rows = await CsvImportEngine.ReadCsvRowsAsync(path, ct);
			var codes = new List<(int LineNo, string Code)>();
			foreach (var row in rows) {
				var code = row.Fields.Count > 0 ? row.Fields[0].Trim() : string.Empty;
				if (code.Length == 0) continue;
				codes.Add((row.LineNo, code));
			}
			var found = await FindShohinsByCodeAsync(codes.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToList(), ct);
			var errors = codes.Where(x => !found.ContainsKey(x.Code)).ToList();
			// 1行目の見出しは未登録エラーにしない。
			if (errors.Count > 0 && errors[0].LineNo == codes.FirstOrDefault().LineNo && IsHeader(errors[0].Code)) errors.RemoveAt(0);
			var added = AddRows(codes.Where(x => found.ContainsKey(x.Code)).Select(x => found[x.Code]));
			Message = $"CSV取込: {codes.Count:N0} 行中 {added:N0} 件を追加しました" + (errors.Count > 0 ? $"。未登録の商品コードが {errors.Count:N0} 件あります（追加していません）。" : "。");
			if (errors.Count > 0) {
				var sb = new StringBuilder();
				foreach (var (lineNo, code) in errors) sb.AppendLine(CultureInfo.InvariantCulture, $"{lineNo}行目: {code}");
				MessageEx.ShowWarningDialog(Message, sb.ToString(), ActiveWindow);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"CSV取込エラー: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	/// <summary>選択行を削除する。</summary>
	[RelayCommand(CanExecute = nameof(CanEditShohins))]
	void RemoveShohins(IList? items) {
		var targets = items?.OfType<PointCampaignShohinRow>().ToList() ?? [];
		if (targets.Count == 0) {
			Message = "削除する商品行を選択してください。";
			return;
		}
		BatchUpdate(() => { foreach (var row in targets) Shohins.Remove(row); });
		Message = $"商品 {targets.Count:N0} 件を一覧から外しました（登録で確定します）。";
	}

	bool CanEditShohins() => HasTarget && !IsBusy;

	protected override void RefreshCommands() {
		base.RefreshCommands();
		AddShohinCommand.NotifyCanExecuteChanged();
		SelectShohinCommand.NotifyCanExecuteChanged();
		ImportCsvCommand.NotifyCanExecuteChanged();
		RemoveShohinsCommand.NotifyCanExecuteChanged();
	}

	int AddRows(IEnumerable<MasterShohin> shohins) {
		var existing = Shohins.Select(x => x.Id).ToHashSet();
		var added = 0;
		BatchUpdate(() => {
			foreach (var s in shohins) {
				if (!existing.Add(s.Id)) continue;
				Shohins.Add(new PointCampaignShohinRow(s.Id, s.Code, s.Name));
				added++;
			}
		});
		return added;
	}

	async Task<Dictionary<string, MasterShohin>> FindShohinsByCodeAsync(IReadOnlyList<string> codes, CancellationToken ct) {
		var result = new Dictionary<string, MasterShohin>(StringComparer.Ordinal);
		foreach (var chunk in codes.Chunk(500)) {
			List<string> parameters = [];
			var inList = string.Join(",", chunk.Select(code => AddParameter(parameters, code)));
			foreach (var s in await QueryListAsync<MasterShohin>(new QueryListParam(typeof(MasterShohin), $"Code IN ({inList})", "Code", [.. parameters]), ct))
				result[s.Code] = s;
		}
		return result;
	}

	static bool IsHeader(string value) => value is "商品コード" or "商品CD" or "Code" or "code" or "CODE";
}
