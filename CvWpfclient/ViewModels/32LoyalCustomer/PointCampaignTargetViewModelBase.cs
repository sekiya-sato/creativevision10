using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>
/// ポイントキャンペーンの対象（店舗・商品）設定画面の共通処理。
/// 左にキャンペーン一覧、右に選択キャンペーンの条件(表示のみ)と対象店舗チェック一覧を持つ。
/// 保存は Msg064_PointCampaignTargetSave で、Preview で重複を確認してから確認済み一覧を付けて Apply する。
/// 条件変更・再検索・競合時は右側の編集対象を外し、旧対象へ保存させない。
/// </summary>
public abstract partial class PointCampaignTargetViewModelBase : BaseViewModel {
	public const int AllEnabled = -1;

	public abstract string Title { get; }
	/// <summary>一覧へ出す優先区分</summary>
	protected abstract int[] PriorityTypes { get; }

	public IReadOnlyList<KeyValuePair<int, string>> EnabledOptions { get; } = [
		new(AllEnabled, "すべて"), new((int)EnumYesNo.Yes, "有効のみ"), new((int)EnumYesNo.No, "無効のみ")];

	// ---- 一覧条件 ----------------------------------------------------------

	[ObservableProperty]
	public partial string SearchCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchDayFrom { get; set; } = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string SearchDayTo { get; set; } = DateTime.Today.AddDays(7).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial int SearchEnabled { get; set; } = AllEnabled;

	partial void OnSearchCodeChanged(string value) => InvalidateList();
	partial void OnSearchDayFromChanged(string value) => InvalidateList();
	partial void OnSearchDayToChanged(string value) => InvalidateList();
	partial void OnSearchEnabledChanged(int value) => InvalidateList();

	// ---- 一覧・選択 --------------------------------------------------------

	public ObservableCollection<PointCampaignListRow> Campaigns { get; } = [];

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsConditionEditable), nameof(IsIdle), nameof(IsShopEditable), nameof(IsTargetEditable))]
	public partial bool IsBusy { get; set; }

	PointCampaignListRow? selectedCampaign;
	/// <summary>一覧の選択行。未保存の変更があれば確認し、取り消したら選択を戻す。</summary>
	public PointCampaignListRow? SelectedCampaign {
		get => selectedCampaign;
		set {
			if (ReferenceEquals(selectedCampaign, value)) return;
			// 一覧の入替え中（Clear で DataGrid が null を押し戻す等）は選択変更として扱わない。
			if (suppressSelection) return;
			if (IsBusy) {
				// 処理中の選択切替は受け付けず、表示を元へ戻す。
				Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedCampaign)));
				return;
			}
			if (value?.Campaign.Id == Target?.Id && value != null) {
				// 再検索後の同じキャンペーン行は読み直さない（呼出元が明示的に読み直す）。
				selectedCampaign = value;
				OnPropertyChanged();
				return;
			}
			if (IsDirty && !ConfirmDiscard("未保存の変更があります。破棄して別のキャンペーンを選択しますか？")) {
				// 一覧の選択表示を元へ戻す（バインド更新中の通知は無視されるため後で通知する）。
				Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedCampaign)));
				return;
			}
			selectedCampaign = value;
			OnPropertyChanged();
			_ = LoadTargetAsync(value?.Campaign);
		}
	}

	/// <summary>右側で編集中のキャンペーン（表示のみ）。nullなら保存不可。</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasTarget), nameof(IsShopEnabled), nameof(IsShopNotRequired), nameof(IsShopEditable), nameof(IsTargetEditable),nameof(TargetPeriodText), nameof(TargetPointText), nameof(TargetRankText))]
	public partial MasterPointCampaign? Target { get; set; }

	public bool HasTarget => Target != null;
	/// <summary>商品全店（店舗指定不要）のキャンペーンを選択中</summary>
	public bool IsShopNotRequired => Target != null && !IsShopEnabled;
	public string TargetPeriodText => Target == null ? string.Empty : $"{FormatDay(Target.DayFrom)} ～ {FormatDay(Target.DayTo)}";
	public string TargetPointText => Target == null ? string.Empty : $"{Target.PointUnitPrice:N0}円ごと  プロパー {Target.PointAmountProper:N0}P / セール {Target.PointAmountSale:N0}P";
	public string TargetRankText => Target == null ? string.Empty : Target.RankKubun == 0 ? "全ランク" : $"ランク {Target.RankKubun}";

	/// <summary>対象店舗を持つ優先区分か（店別・商品店別）。商品全店では店舗一覧を無効にする。</summary>
	public bool IsShopEnabled => Target != null && Target.EnPriorityType is EnumPointCampaignPriority.Shop or EnumPointCampaignPriority.ShohinShop;

	long targetVdu;
	int loadVersion;
	bool suppressSelection;
	int deferRecalc;
	/// <summary>保存済みの対象行が1件以上あるか（画面に出せない行を含む）。対象解除の可否に使う。</summary>
	bool hasSavedTargets;

	/// <summary>処理中でなく一覧を操作できる</summary>
	public bool IsIdle => !IsBusy;
	/// <summary>対象店舗を編集できる（店舗を持つ区分・処理中でない）</summary>
	public bool IsShopEditable => IsShopEnabled && !IsBusy;
	/// <summary>対象（商品）を編集できる</summary>
	public bool IsTargetEditable => HasTarget && !IsBusy;

	// ---- 対象店舗 ----------------------------------------------------------

	public ObservableCollection<PointCampaignShopRow> Shops { get; } = [];
	public ICollectionView ShopsView { get; }

	[ObservableProperty]
	public partial string ShopFilter { get; set; } = string.Empty;

	partial void OnShopFilterChanged(string value) => ShopsView.Refresh();

	[ObservableProperty]
	public partial int ShopCheckedCount { get; set; }

	/// <summary>店舗のコピー元（店舗を持つ全キャンペーン）</summary>
	[ObservableProperty]
	public partial List<MasterPointCampaign> CopySources { get; set; } = [];

	[ObservableProperty]
	public partial MasterPointCampaign? SelectedCopySource { get; set; }

	partial void OnSelectedCopySourceChanged(MasterPointCampaign? value) => RefreshCommands();

	protected HashSet<long> OriginalShopIds { get; private set; } = [];
	protected HashSet<long> OriginalShohinIds { get; private set; } = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsConditionEditable))]
	public partial bool IsDirty { get; set; }

	/// <summary>未保存の変更がある間は一覧条件を変えさせない（条件変更で編集対象を外すため）。</summary>
	public bool IsConditionEditable => !IsDirty && !IsBusy;

	partial void OnIsDirtyChanged(bool value) => RefreshCommands();
	partial void OnIsBusyChanged(bool value) => RefreshCommands();
	partial void OnTargetChanged(MasterPointCampaign? value) => RefreshCommands();

	protected PointCampaignTargetViewModelBase() {
		ShopsView = CollectionViewSource.GetDefaultView(Shops);
		ShopsView.Filter = FilterShop;
	}

	bool FilterShop(object item) {
		if (item is not PointCampaignShopRow row) return false;
		var text = (ShopFilter ?? string.Empty).Trim();
		if (text.Length == 0) return true;
		return row.Shop.Code.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| row.Shop.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| row.Shop.Ryaku.Contains(text, StringComparison.OrdinalIgnoreCase);
	}

	// ---- 画面終了 ----------------------------------------------------------

	/// <summary>画面を閉じてよいか。未保存の変更があれば確認する（View の Closing から呼ぶ）。</summary>
	public bool ConfirmClose() => !IsDirty || ConfirmDiscard("未保存の変更があります。破棄して閉じますか？");

	protected bool ConfirmDiscard(string message) =>
		MessageEx.ShowQuestionDialog(message, owner: ActiveWindow) == MessageBoxResult.Yes;

	protected Window? ActiveWindow => ClientLib.GetActiveView(this);

	// ---- コマンド ----------------------------------------------------------

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		IsBusy = true;
		try {
			var shops = await QueryListAsync<MasterTokui>(new QueryListParam(typeof(MasterTokui), "TenType IN (3,6)", "Code, Id"), ct);
			Shops.Clear();
			foreach (var shop in shops) {
				var row = new PointCampaignShopRow(shop);
				row.PropertyChanged += OnShopRowChanged;
				Shops.Add(row);
			}
			await LoadCopySourcesAsync(ct);
		}
		catch (OperationCanceledException) { return; }
		catch (Exception ex) {
			ShowError($"店舗の取得失敗: {ex.Message}");
			return;
		}
		finally { IsBusy = false; }
		await ListCoreAsync(null, ct);
	}

	/// <summary>一覧取得（F5）。未保存の変更があれば確認する。</summary>
	[RelayCommand]
	async Task DoList(CancellationToken ct) {
		if (IsDirty && !ConfirmDiscard("未保存の変更があります。破棄して一覧を再取得しますか？")) return;
		ResetTarget();
		try { await LoadCopySourcesAsync(ct); }
		catch (OperationCanceledException) { return; }
		catch (Exception ex) { ShowError($"コピー元の取得失敗: {ex.Message}"); return; }
		await ListCoreAsync(null, ct);
	}

	/// <summary>登録（F2）。Preview で重複を確認し、了承後に確認済み一覧を付けて Apply する。</summary>
	[RelayCommand(CanExecute = nameof(CanSave))]
	Task DoSave(CancellationToken ct) {
		if (Target == null) return Task.CompletedTask;
		var shopIds = IsShopEnabled ? Shops.Where(x => x.IsChecked).Select(x => x.Shop.Id).ToList() : [];
		var shohinIds = CurrentShohinIds().ToList();
		if (!ValidateBeforeSave(shopIds, shohinIds)) return Task.CompletedTask;
		return SaveTargetsAsync(shopIds, shohinIds, "登録", ct);
	}

	bool CanSave() => HasTarget && IsDirty && !IsBusy;

	/// <summary>対象解除。全対象を外す（空リストで同じ保存手順）。</summary>
	[RelayCommand(CanExecute = nameof(CanClearTargets))]
	Task DoClearTargets(CancellationToken ct) {
		if (Target == null) return Task.CompletedTask;
		if (MessageEx.ShowQuestionDialog($"キャンペーン {Target.Code} の対象をすべて解除しますか？\n解除するとこのキャンペーンは適用されなくなります。", owner: ActiveWindow) != MessageBoxResult.Yes) return Task.CompletedTask;
		return SaveTargetsAsync([], [], "対象解除", ct);
	}

	bool CanClearTargets() => HasTarget && !IsBusy && hasSavedTargets;

	/// <summary>取消。保存済みの対象へ戻す。</summary>
	[RelayCommand(CanExecute = nameof(CanRevert))]
	Task DoRevert(CancellationToken ct) {
		if (Target == null) return Task.CompletedTask;
		if (!ConfirmDiscard("未保存の変更を取り消しますか？")) return Task.CompletedTask;
		return LoadTargetAsync(Target, ct);
	}

	bool CanRevert() => HasTarget && IsDirty && !IsBusy;

	[RelayCommand(CanExecute = nameof(CanEditShops))]
	void SelectAllShops() {
		BatchUpdate(() => { foreach (var row in ShopsView.Cast<PointCampaignShopRow>()) row.IsChecked = true; });
	}

	[RelayCommand(CanExecute = nameof(CanEditShops))]
	void ClearAllShops() {
		BatchUpdate(() => { foreach (var row in ShopsView.Cast<PointCampaignShopRow>()) row.IsChecked = false; });
	}

	bool CanEditShops() => IsShopEnabled && !IsBusy;

	/// <summary>他キャンペーンの対象店舗で置き換える。</summary>
	[RelayCommand(CanExecute = nameof(CanCopyShops))]
	async Task CopyShops(CancellationToken ct) {
		var source = SelectedCopySource;
		if (source == null || Target == null) return;
		IsBusy = true;
		try {
			var ids = (await QueryListAsync<MasterPointCampaignShop>(new QueryListParam(typeof(MasterPointCampaignShop), $"Id_PointCampaign = {source.Id}"), ct))
				.Select(x => x.Id_Tenpo).ToHashSet();
			BatchUpdate(() => { foreach (var row in Shops) row.IsChecked = ids.Contains(row.Shop.Id); });
			var missing = ids.Count - Shops.Count(x => ids.Contains(x.Shop.Id));
			Message = $"{source.Code} の対象店舗 {ids.Count:N0} 件で置き換えました。" + (missing > 0 ? $"（店舗一覧にない {missing:N0} 件は除外）" : "");
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"コピー元店舗の取得失敗: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	bool CanCopyShops() => CanEditShops() && SelectedCopySource != null && SelectedCopySource.Id != Target?.Id;

	protected virtual void RefreshCommands() {
		DoSaveCommand.NotifyCanExecuteChanged();
		DoClearTargetsCommand.NotifyCanExecuteChanged();
		DoRevertCommand.NotifyCanExecuteChanged();
		SelectAllShopsCommand.NotifyCanExecuteChanged();
		ClearAllShopsCommand.NotifyCanExecuteChanged();
		CopyShopsCommand.NotifyCanExecuteChanged();
	}

	// ---- 商品（派生画面で実装） --------------------------------------------

	/// <summary>保存する対象商品Id。商品を扱わない画面は空。</summary>
	protected virtual IEnumerable<long> CurrentShohinIds() => [];
	/// <summary>選択キャンペーンの対象商品を読込み、画面に出せた商品Idを返す（マスタから消えた商品は含まない）。</summary>
	protected virtual Task<IReadOnlyCollection<long>> LoadShohinsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<long>>([]);
	/// <summary>一括変更の後に件数表示などを更新する。</summary>
	protected virtual void OnTargetsRecalculated() { }
	/// <summary>商品明細を空にする。</summary>
	protected virtual void ClearShohins() { }
	/// <summary>保存前の画面検査。</summary>
	protected virtual bool ValidateBeforeSave(List<long> shopIds, List<long> shohinIds) => true;

	// ---- 内部処理 ----------------------------------------------------------

	void OnShopRowChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName != nameof(PointCampaignShopRow.IsChecked) || deferRecalc > 0) return;
		ShopCheckedCount = Shops.Count(x => x.IsChecked);
		UpdateDirty();
	}

	/// <summary>
	/// 行ごとの件数・未保存判定の再計算を止めて一括変更し、最後に1回だけ再計算する（全件HashSet再構築の繰返しを避ける）。
	/// </summary>
	protected void BatchUpdate(Action change) {
		deferRecalc++;
		try { change(); }
		finally {
			if (--deferRecalc == 0) {
				ShopCheckedCount = Shops.Count(x => x.IsChecked);
				OnTargetsRecalculated();
				UpdateDirty();
			}
		}
	}

	/// <summary>一括変更中か（派生画面の行追加通知で再計算を省く）。</summary>
	protected bool IsRecalcDeferred => deferRecalc > 0;

	/// <summary>保存済みの対象と画面の対象を比べて未保存状態を更新する。</summary>
	protected void UpdateDirty() {
		if (deferRecalc > 0) return;
		if (Target == null) { IsDirty = false; return; }
		var shops = IsShopEnabled ? Shops.Where(x => x.IsChecked).Select(x => x.Shop.Id).ToHashSet() : [];
		var shohins = CurrentShohinIds().ToHashSet();
		IsDirty = !shops.SetEquals(OriginalShopIds) || !shohins.SetEquals(OriginalShohinIds);
	}

	/// <summary>一覧条件の変更。旧結果と編集対象を無効化して再検索させる（未保存の間は条件入力不可）。</summary>
	void InvalidateList() {
		if (Campaigns.Count == 0 && Target == null) return;
		ResetTarget();
		suppressSelection = true;
		try { Campaigns.Clear(); } finally { suppressSelection = false; }
		Count = 0;
		Message = "条件が変更されました。［一覧取得（F5）］で再検索してください。";
	}

	/// <summary>右側の編集対象を外す。以後の保存は対象選択まで不可。</summary>
	protected void ResetTarget() {
		loadVersion++;
		selectedCampaign = null;
		OnPropertyChanged(nameof(SelectedCampaign));
		Target = null;
		targetVdu = 0;
		OriginalShopIds = [];
		OriginalShohinIds = [];
		hasSavedTargets = false;
		BatchUpdate(() => {
			foreach (var row in Shops) row.IsChecked = false;
			ClearShohins();
		});
		ShopFilter = string.Empty;
		IsDirty = false;
	}

	async Task ListCoreAsync(long? keepId, CancellationToken ct) {
		if (!IsDate(SearchDayFrom) || !IsDate(SearchDayTo) || string.CompareOrdinal(SearchDayFrom, SearchDayTo) > 0) {
			MessageEx.ShowWarningDialog("期間は開始日・終了日を正しく指定してください。", owner: ActiveWindow);
			return;
		}
		// 保存後の再取得など、呼出元が処理中のときは処理中のまま戻す。
		var wasBusy = IsBusy;
		IsBusy = true;
		try {
			List<string> clauses = [$"PriorityType IN ({string.Join(",", PriorityTypes)})"];
			List<string> parameters = [];
			var code = (SearchCode ?? string.Empty).Trim();
			if (code.Length > 0) clauses.Add($"Code LIKE {AddParameter(parameters, EscapeLike(code) + "%")} ESCAPE '\\'");
			// 期間が重なるもの（開始<=条件終了 かつ 終了>=条件開始）
			clauses.Add($"DayFrom <= {AddParameter(parameters, SearchDayTo)}");
			clauses.Add($"DayTo >= {AddParameter(parameters, SearchDayFrom)}");
			if (SearchEnabled != AllEnabled) clauses.Add($"IsEnabled = {SearchEnabled}");
			var campaigns = await QueryListAsync<MasterPointCampaign>(
				new QueryListParam(typeof(MasterPointCampaign), string.Join(" AND ", clauses), "Code, Id", [.. parameters]), ct);
			var shopCounts = new Dictionary<long, int>();
			var shohinCounts = new Dictionary<long, int>();
			foreach (var chunk in campaigns.Select(x => x.Id).Chunk(500)) {
				var inList = string.Join(",", chunk);
				foreach (var g in (await QueryListAsync<MasterPointCampaignShop>(new QueryListParam(typeof(MasterPointCampaignShop), $"Id_PointCampaign IN ({inList})"), ct)).GroupBy(x => x.Id_PointCampaign))
					shopCounts[g.Key] = g.Count();
				foreach (var g in (await QueryListAsync<MasterPointCampaignShohin>(new QueryListParam(typeof(MasterPointCampaignShohin), $"Id_PointCampaign IN ({inList})"), ct)).GroupBy(x => x.Id_PointCampaign))
					shohinCounts[g.Key] = g.Count();
			}
			// 入替え中に DataGrid が押し戻す選択(null)で、未保存確認や対象の読込が走らないようにする。
			suppressSelection = true;
			try {
				Campaigns.Clear();
				foreach (var c in campaigns) Campaigns.Add(new PointCampaignListRow(c, shopCounts.GetValueOrDefault(c.Id), shohinCounts.GetValueOrDefault(c.Id)));
			}
			finally { suppressSelection = false; }
			Count = Campaigns.Count;
			Message = Count == 0 ? "条件に合うキャンペーンはありません。" : "キャンペーンを選択して対象を設定してください。";
			// 一覧の行は作り直したので、選択は保持するIdの新しい行（なければ未選択）へ合わせる。
			selectedCampaign = keepId == null ? null : Campaigns.FirstOrDefault(x => x.Campaign.Id == keepId);
			OnPropertyChanged(nameof(SelectedCampaign));
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"一覧の取得失敗: {ex.Message}"); }
		finally { IsBusy = wasBusy; }
	}

	async Task LoadCopySourcesAsync(CancellationToken ct) {
		var shopTypes = $"{(int)EnumPointCampaignPriority.Shop},{(int)EnumPointCampaignPriority.ShohinShop}";
		CopySources = await QueryListAsync<MasterPointCampaign>(new QueryListParam(typeof(MasterPointCampaign), $"PriorityType IN ({shopTypes})", "Code, Id"), ct);
		SelectedCopySource = null;
	}

	/// <summary>選択キャンペーンの保存済み対象を読込み、編集対象にする。</summary>
	Task LoadTargetAsync(MasterPointCampaign? campaign) => LoadTargetAsync(campaign, CancellationToken.None);

	async Task LoadTargetAsync(MasterPointCampaign? campaign, CancellationToken ct) {
		var version = ++loadVersion;
		// 読込中は旧対象への保存をさせない。
		Target = null;
		targetVdu = 0;
		OriginalShopIds = [];
		OriginalShohinIds = [];
		hasSavedTargets = false;
		BatchUpdate(() => {
			foreach (var row in Shops) row.IsChecked = false;
			ClearShohins();
		});
		IsDirty = false;
		if (campaign == null) return;
		var wasBusy = IsBusy;
		IsBusy = true;
		try {
			var shopIds = (await QueryListAsync<MasterPointCampaignShop>(new QueryListParam(typeof(MasterPointCampaignShop), $"Id_PointCampaign = {campaign.Id}"), ct))
				.Select(x => x.Id_Tenpo).ToHashSet();
			var shohinIds = (await QueryListAsync<MasterPointCampaignShohin>(new QueryListParam(typeof(MasterPointCampaignShohin), $"Id_PointCampaign = {campaign.Id}"), ct))
				.Select(x => x.Id_Shohin).ToList();
			if (version != loadVersion) return;
			var loadedShohins = await LoadShohinsAsync(shohinIds, ct);
			if (version != loadVersion) return;
			var shown = Shops.Select(x => x.Shop.Id).ToHashSet();
			BatchUpdate(() => { foreach (var row in Shops) row.IsChecked = shopIds.Contains(row.Shop.Id); });
			// 画面に出せない行（店種変更・削除済み）は比較元から除き、読込直後を未保存扱いにしない。
			OriginalShopIds = [.. shopIds.Where(shown.Contains)];
			OriginalShohinIds = [.. loadedShohins];
			hasSavedTargets = shopIds.Count > 0 || shohinIds.Count > 0;
			targetVdu = campaign.Vdu;
			Target = Common.CloneObject(campaign);
			var missingShops = shopIds.Count - OriginalShopIds.Count;
			var missingShohins = shohinIds.Distinct().Count() - OriginalShohinIds.Count;
			var notes = new List<string>();
			if (missingShops > 0) notes.Add($"店舗一覧にない対象店舗 {missingShops:N0} 件");
			if (missingShohins > 0) notes.Add($"商品マスタにない対象商品 {missingShohins:N0} 件");
			Message = notes.Count > 0 ? $"{string.Join("・", notes)}があります。次の登録で外れます。" : string.Empty;
			UpdateDirty();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"対象の取得失敗: {ex.Message}"); }
		finally {
			IsBusy = wasBusy;
			RefreshCommands();
		}
	}

	async Task SaveTargetsAsync(List<long> shopIds, List<long> shohinIds, string actionName, CancellationToken ct) {
		var target = Target;
		if (target == null) return;
		var param = new PointCampaignTargetParameter {
			Id_PointCampaign = target.Id, Vdu = targetVdu, Ids_Tenpo = shopIds, Ids_Shohin = shohinIds, IsPreview = true,
		};
		IsBusy = true;
		try {
			var preview = await SendTargetSaveAsync(param, actionName, ct);
			if (preview == null) return;
			if (preview.Conflicts.Count > 0) {
				var question = $"期間が重なる他キャンペーンと対象が {preview.Conflicts.Count:N0} 件重複しています。\n{actionName}すると、相手キャンペーンから重複分の対象を外します。よろしいですか？";
				if (MessageEx.ShowQuestionDialog(question, FormatConflicts(preview.Conflicts), ActiveWindow) != MessageBoxResult.Yes) {
					Message = $"{actionName}を中止しました。";
					return;
				}
			}
			else if (actionName == "登録" && MessageEx.ShowQuestionDialog($"キャンペーン {target.Code} の対象を登録しますか？（店舗 {shopIds.Count:N0} 件・商品 {shohinIds.Count:N0} 件）", owner: ActiveWindow) != MessageBoxResult.Yes) {
				return;
			}
			param.IsPreview = false;
			param.Confirmed = preview.Conflicts;
			var result = await SendTargetSaveAsync(param, actionName, ct);
			if (result == null) return;
			targetVdu = result.Vdu;
			// 保存済みになったので未保存状態を解消してから、重複先の件数も変わるため一覧ごと再取得し、
			// 同じキャンペーンを選び直して保存済みの対象を表示する（処理中のまま行い、途中の選択操作を受けない）。
			IsDirty = false;
			await ListCoreAsync(target.Id, ct);
			var row = Campaigns.FirstOrDefault(x => x.Campaign.Id == target.Id);
			if (row != null) await LoadTargetAsync(row.Campaign, ct);
			else ResetTarget();
			Message = $"{actionName}しました（店舗 {shopIds.Count:N0} 件・商品 {shohinIds.Count:N0} 件" + (result.Conflicts.Count > 0 ? $"、他キャンペーンから {result.Conflicts.Count:N0} 件を外しました）" : "）");
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"{actionName}エラー: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	/// <summary>Msg064 を送る。エラー時は表示して null を返す。競合時は編集対象を外して再取得を案内する。</summary>
	async Task<PointCampaignTargetResult?> SendTargetSaveAsync(PointCampaignTargetParameter param, string actionName, CancellationToken ct) {
		var reply = await SendMessageAsync(new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg064_PointCampaignTargetSave,
			DataType = typeof(PointCampaignTargetParameter),
			DataMsg = Common.SerializeObject(param),
		}, ct);
		if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
			ResetTarget();
			Message = "他の端末でキャンペーンまたは対象が更新されたため、保存しませんでした。\n［一覧取得（F5）］で最新の状態を再取得してください。";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
			return null;
		}
		if (reply.Code < 0) {
			var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
			if (string.IsNullOrWhiteSpace(detail)) detail = reply.Option ?? reply.DataMsg;
			Message = $"{actionName}エラー: {detail}";
			MessageEx.ShowErrorDialog($"{Message} ({reply.Code})", owner: ActiveWindow);
			return null;
		}
		if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not PointCampaignTargetResult result)
			throw new InvalidOperationException("サーバ応答を読み取れません。");
		return result;
	}

	static string FormatConflicts(List<PointCampaignConflict> conflicts) {
		var sb = new StringBuilder();
		foreach (var c in conflicts.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.TargetKind).ThenBy(x => x.TargetCode, StringComparer.Ordinal)) {
			var kind = c.TargetKind == 1 ? "店舗" : "商品";
			sb.AppendLine(CultureInfo.InvariantCulture, $"{c.Code} {c.Name}  {FormatDay(c.DayFrom)}～{FormatDay(c.DayTo)}  {kind} {c.TargetCode} {c.TargetName}");
		}
		return sb.ToString();
	}

	protected static string FormatDay(string day) =>
		DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : day;

	static bool IsDate(string? value) => DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

	protected static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

	protected static string AddParameter(List<string> parameters, string value) {
		parameters.Add(value);
		return $"@{parameters.Count - 1}";
	}

	protected void ShowError(string message) {
		Message = message;
		MessageEx.ShowErrorDialog(message, owner: ActiveWindow);
	}

	protected virtual ValueTask<CvMsg> SendMessageAsync(CvMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return new ValueTask<CvMsg>(coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct)));
	}

	/// <summary>汎用照会(Msg101)で一覧を取得する。</summary>
	protected async Task<List<T>> QueryListAsync<T>(QueryListParam param, CancellationToken ct) {
		var reply = await SendMessageAsync(new CvMsg {
			Code = 0, Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param),
		}, ct);
		if (reply.Code < 0 && reply.Code != -1) throw new InvalidOperationException(reply.Option ?? reply.DataMsg);
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? list.Cast<T>().ToList() : [];
	}
}
