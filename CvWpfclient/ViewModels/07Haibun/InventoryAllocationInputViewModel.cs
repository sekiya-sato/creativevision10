using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 在庫配分入力。倉庫の有効在庫を、配分先（倉庫・卸先・売仕店・直営店）× SKU のマトリクスで振り分ける。
/// <para>
/// タブ1は商品一覧（在庫あり・滞留の抽出）、タブ2は配分先×SKU の入力と按分（同数・比率）。
/// 旧CV.netの在庫配分入力に相当し、在庫品配分・店舗出荷依頼・移動指示もこの画面で扱う（決定 D2）。
/// 作る配分は <see cref="EnumHaibun.Zaiko"/>（<c>RelateNo1=0</c>）で、保存は既存配分の洗い替えを1往復で行う。
/// 按分の計算は <see cref="AllocationCalculator"/>。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step3_在庫配分入力_詳細設計.md`。
/// </para>
/// </summary>
public partial class InventoryAllocationInputViewModel : BaseViewModel {
	const int KubunZaiko = (int)EnumHaibun.Zaiko;
	/// <summary>配分先にできる店種区分（倉庫・卸先・売仕店・直営店）</summary>
	const string DestinationTenTypes = "0,1,3,6";
	/// <summary>
	/// 最終売上日を探す範囲（日）。売上明細はJSONなので全履歴を展開すると遅い（開発DBで約15秒）。
	/// 伝票日付の索引が効くよう直近1年に絞り、それより前しか売上が無い商品は「1年以上売上なし」とする。
	/// </summary>
	const int SalesLookbackDays = 365;

	public const string ModeEqual = "同数";
	public const string ModeRatio = "比率";
	public const string BasisSales = "売上実績";
	public const string BasisPrevious = "前回配分";
	public const string BasisManual = "手入力";
	public const string ScopeShohin = "同一商品";
	public const string ScopeBrand = "同一ブランド";
	public const string TotalYuko = "有効在庫";
	public const string TotalInput = "入力した数";

	long idSoko;
	MasterShohin? targetShohin;
	List<TranHaibun> loadedEditableRows = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = "配分元倉庫を指定して［検索］を押してください。";

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== タブ1: 条件 =====

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ItemFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ItemTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinName { get; set; } = string.Empty;

	/// <summary>有効在庫がある商品だけを出す</summary>
	[ObservableProperty]
	public partial bool ZaikoOnly { get; set; } = true;

	/// <summary>滞留日数。入力があれば最終売上日からこの日数以上経った商品だけを出す（在庫品配分の代わり）</summary>
	[ObservableProperty]
	public partial string StagnationDaysText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int MaxCount { get; set; } = AppGlobal.Limit;

	[ObservableProperty]
	public partial ObservableCollection<InventoryAllocationShohinRow> SearchRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	public partial InventoryAllocationShohinRow? SelectedSearchRow { get; set; }

	// ===== タブ2: 配分入力 =====

	[ObservableProperty]
	public partial string TargetDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? ShijiDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? NouhinDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial long Id_Shain { get; set; }

	[ObservableProperty]
	public partial string ShainDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Memo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<InventoryAllocationSku> SkuColumns { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<InventoryAllocationRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial InventoryAllocationRow? SelectedRow { get; set; }

	/// <summary>マトリクスで選択中のSKU列（-1 は未選択）。「選択中の列だけ」按分に使う。Viewが設定する</summary>
	[ObservableProperty]
	public partial int SelectedSkuIndex { get; set; } = -1;

	[ObservableProperty]
	public partial int GrandTotalSu { get; set; }

	// ===== 按分パネル =====

	public IReadOnlyList<string> Modes { get; } = [ModeRatio, ModeEqual];
	public IReadOnlyList<string> Bases { get; } = [BasisSales, BasisPrevious, BasisManual];
	public IReadOnlyList<string> Scopes { get; } = [ScopeShohin, ScopeBrand];
	public IReadOnlyList<string> Roundings { get; } = ["切捨", "四捨五入"];
	public IReadOnlyList<string> TotalModes { get; } = [TotalYuko, TotalInput];
	public IReadOnlyList<string> Targets { get; } = ["全SKU", "選択中の列"];

	[ObservableProperty]
	public partial string Mode { get; set; } = ModeRatio;

	[ObservableProperty]
	public partial int SameQty { get; set; } = 1;

	[ObservableProperty]
	public partial string Basis { get; set; } = BasisSales;

	[ObservableProperty]
	public partial DateTime? SalesFrom { get; set; } = DateTime.Today.AddDays(-28);

	[ObservableProperty]
	public partial DateTime? SalesTo { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial string Scope { get; set; } = ScopeShohin;

	[ObservableProperty]
	public partial string Rounding { get; set; } = "四捨五入";

	[ObservableProperty]
	public partial string TotalMode { get; set; } = TotalYuko;

	[ObservableProperty]
	public partial int TotalQty { get; set; }

	[ObservableProperty]
	public partial string Target { get; set; } = "全SKU";

	bool CanGoToEdit() => SelectedTabIndex == 0 && SelectedSearchRow != null;
	bool HasTarget() => targetShohin != null && !IsBusy;

	partial void OnIsBusyChanged(bool value) => NotifyEditCommands();
	partial void OnRowsChanged(ObservableCollection<InventoryAllocationRow> value) => NotifyEditCommands();

	void NotifyEditCommands() {
		CalcRatioCommand.NotifyCanExecuteChanged();
		ApplyAllocationCommand.NotifyCanExecuteChanged();
		AddDestinationsCommand.NotifyCanExecuteChanged();
		LoadPreviousDestinationsCommand.NotifyCanExecuteChanged();
		ClearAllCommand.NotifyCanExecuteChanged();
		DoRegisterCommand.NotifyCanExecuteChanged();
	}

	// ===== タブ1 =====

	[RelayCommand]
	void SelectSoko() {
		var soko = ShowSelect<MasterTokui>(typeof(MasterTokui), "TenType IN (0,3,6)", "Code");
		if (soko == null) return;
		SokoCode = soko.Code;
		SokoName = soko.Name;
	}

	/// <summary>検索(F5)。配分元倉庫の商品を在庫・未確定配分・売上つきで一覧する</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(SokoCode)) {
			MessageEx.ShowWarningDialog("配分元倉庫を指定してください。", owner: ActiveWindow);
			return;
		}
		int? stagnationDays = null;
		if (!string.IsNullOrWhiteSpace(StagnationDaysText)) {
			if (!int.TryParse(StagnationDaysText.Trim(), out var days) || days < 0 || days > SalesLookbackDays) {
				MessageEx.ShowWarningDialog($"滞留日数は0〜{SalesLookbackDays}の数で入力してください。", owner: ActiveWindow);
				return;
			}
			stagnationDays = days;
		}
		StartBusy("一覧取得中...");
		try {
			var soko = await ResolveTokuiAsync(SokoCode, ct) ?? throw new InvalidOperationException($"倉庫コード {SokoCode} が見つかりません。");
			idSoko = soko.Id;
			SokoName = soko.Name;
			var shohinList = await LoadShohinListAsync(ct);
			var ids = shohinList.Select(x => x.Id).ToList();
			var stock = await LoadStockByShohinAsync(ids, ct);
			var haibun = await LoadZaikoHaibunByShohinAsync(ids, ct);
			var sales = await LoadSalesByShohinAsync(ids, ct);
			var today = DateTime.Today;
			var rows = shohinList.Select(m => {
				var s = stock.GetValueOrDefault(m.Id);
				var u = sales.GetValueOrDefault(m.Id);
				return new InventoryAllocationShohinRow(m, s.Su, s.Reserve, haibun.GetValueOrDefault(m.Id), u.Recent, u.LastDay);
			})
			// 滞留: 最終売上日から N 日以上（売上なしも含む）
			.Where(r => stagnationDays is not int n || r.LastSalesDate is not DateTime last || (today - last).TotalDays >= n)
			.ToList();
			SearchRows = [.. rows];
			SelectedSearchRow = SearchRows.FirstOrDefault();
			var limited = shohinList.Count >= MaxCount && MaxCount > 0 ? $"（上限 {MaxCount:N0} 件で打ち切り）" : string.Empty;
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 商品を {SearchRows.Count:N0} 件取得しました{limited}";
		}
		catch (OperationCanceledException) { Message = "一覧取得を中断しました"; }
		catch (Exception ex) {
			Message = $"一覧取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>配分入力へ(F6)。選んだ商品の既存配分を読み込んでタブ2を作る</summary>
	[RelayCommand(CanExecute = nameof(CanGoToEdit), IncludeCancelCommand = true)]
	async Task GoToEdit(CancellationToken ct) {
		if (SelectedSearchRow == null) return;
		StartBusy("配分データ取得中...");
		try {
			await LoadEntryAsync(SelectedSearchRow.Shohin, ct);
			SelectedTabIndex = 1;
		}
		catch (OperationCanceledException) { Message = "配分データ取得を中断しました"; }
		catch (Exception ex) {
			Message = $"配分データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	[RelayCommand]
	void GoToSearch() => SelectedTabIndex = 0;

	// ===== タブ2: 配分先 =====

	/// <summary>配分先の追加・削除（複数選択）。配分元の倉庫自身は選べない</summary>
	[RelayCommand(CanExecute = nameof(HasTarget))]
	void AddDestinations() {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return;
		vm.SetParam(typeof(MasterTokui), $"TenType IN ({DestinationTenTypes}) AND Id <> {idSoko}", "Code",
			selectedIds: Rows.Select(x => x.Id_Tenpo));
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var selected = vm.GetSelectedItems<MasterTokui>();
		var removed = Rows.Where(r => selected.All(s => s.Id != r.Id_Tenpo)).ToList();
		if (removed.Any(r => r.TotalSu > 0) && MessageEx.ShowQuestionDialog(
			"選択から外した配分先に配分数が入力されています。外してよろしいですか？", owner: ActiveWindow) != MessageBoxResult.Yes) return;
		SyncRows(selected);
	}

	/// <summary>前回の配分先を読込。この商品（無ければ同じブランド）の直近の配分の配分先を追加する</summary>
	[RelayCommand(CanExecute = nameof(HasTarget), IncludeCancelCommand = true)]
	async Task LoadPreviousDestinations(CancellationToken ct) {
		StartBusy("前回の配分先を取得中...");
		try {
			var previous = await LoadPreviousAllocationAsync(ct);
			if (previous.Count == 0) {
				Message = "前回の配分が見つかりません。";
				return;
			}
			var tokui = await LoadTokuiAsync(previous.Keys.Where(id => id != idSoko), ct);
			SyncRows([.. Rows.Select(r => r.Tokui).Concat(tokui.Where(t => Rows.All(r => r.Id_Tenpo != t.Id)))]);
			Message = $"前回の配分先 {tokui.Count:N0} 件を読み込みました。";
		}
		catch (OperationCanceledException) { Message = "中断しました"; }
		catch (Exception ex) {
			Message = $"前回の配分先の取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	[RelayCommand]
	void SelectShain() {
		var shain = ShowSelect<MasterShain>(typeof(MasterShain), string.Empty, "Code");
		if (shain == null) return;
		Id_Shain = shain.Id;
		ShainDisplay = CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	// ===== タブ2: 按分 =====

	/// <summary>比率を計算。比率の基準（売上実績・前回配分）から各行の比率(%)を入れる。手入力は何もしない</summary>
	[RelayCommand(CanExecute = nameof(HasTarget), IncludeCancelCommand = true)]
	async Task CalcRatio(CancellationToken ct) {
		if (Rows.Count == 0) {
			MessageEx.ShowWarningDialog("配分先を追加してください。", owner: ActiveWindow);
			return;
		}
		if (Basis == BasisManual) {
			Message = "比率を手入力してください。";
			return;
		}
		StartBusy("比率を計算中...");
		try {
			var basis = Basis == BasisSales ? await LoadSalesByDestinationAsync(ct) : await LoadPreviousAllocationAsync(ct);
			var total = Rows.Sum(r => basis.GetValueOrDefault(r.Id_Tenpo));
			foreach (var row in Rows) {
				row.Ratio = total > 0 ? Math.Round(basis.GetValueOrDefault(row.Id_Tenpo) * 100m / total, 1) : 0m;
			}
			Message = total > 0
				? $"{Basis}から比率を計算しました（合計 {total:N0} 点）。"
				: $"{Basis}がありません。按分すると均等になります。";
		}
		catch (OperationCanceledException) { Message = "中断しました"; }
		catch (Exception ex) {
			Message = $"比率の計算失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>按分実行。対象SKUごとに同数または比率で配分数を入れる（既存の入力は上書き）</summary>
	[RelayCommand(CanExecute = nameof(HasTarget))]
	void ApplyAllocation() {
		if (Rows.Count == 0) {
			MessageEx.ShowWarningDialog("配分先を追加してください。", owner: ActiveWindow);
			return;
		}
		List<int> targets;
		if (Target == "選択中の列") {
			if (SelectedSkuIndex < 0 || SelectedSkuIndex >= SkuColumns.Count) {
				MessageEx.ShowWarningDialog("按分するSKUの列を選択してください。", owner: ActiveWindow);
				return;
			}
			targets = [SelectedSkuIndex];
		}
		else {
			targets = [.. Enumerable.Range(0, SkuColumns.Count)];
		}
		if (TotalMode == TotalInput && targets.Any(i => TotalQty > SkuColumns[i].YukoSu)) {
			if (MessageEx.ShowQuestionDialog("入力した数が有効在庫を超えるSKUがあります。確定時に在庫不足になります。続けますか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) return;
		}
		var rounding = Rounding == "切捨" ? AllocationRounding.Floor : AllocationRounding.Round;
		var weights = Rows.Select(r => r.Ratio).ToList();
		foreach (var i in targets) {
			var total = TotalMode == TotalYuko ? Math.Max(SkuColumns[i].YukoSu, 0) : Math.Max(TotalQty, 0);
			var values = Mode == ModeEqual
				? AllocationCalculator.Equal(SameQty, total, Rows.Count)
				: AllocationCalculator.ByRatio(total, weights, rounding);
			for (int k = 0; k < Rows.Count; k++) Rows[k].Cells[i].Su = values[k];
		}
		Message = $"{Mode}で {targets.Count:N0} SKU を按分しました。";
	}

	[RelayCommand(CanExecute = nameof(HasTarget))]
	void ClearAll() {
		foreach (var cell in Rows.SelectMany(r => r.Cells)) cell.Su = 0;
	}

	// ===== タブ2: 登録 =====

	/// <summary>登録(F2)。既存の在庫配分を洗い替えし、配分数>0 の配分先×SKU を一括登録する</summary>
	[RelayCommand(CanExecute = nameof(HasTarget), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (targetShohin == null) return;
		if (ShijiDay == null || NouhinDay == null) {
			MessageEx.ShowWarningDialog("指示日と納品日を入力してください。", owner: ActiveWindow);
			return;
		}
		var overStock = SkuColumns.Count(s => s.AfterSu < 0);
		if (overStock > 0 && MessageEx.ShowQuestionDialog(
			$"有効在庫を超えるSKUが {overStock:N0} 件あります。確定時に在庫不足でエラーになります。登録しますか？",
			owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("配分データ登録中...");
		try {
			var prices = await LoadPriceByDestinationAsync(targetShohin, Rows.Select(r => r.Tokui), ct);
			var newRecords = BuildNewRecords(prices);
			if (newRecords.Count == 0 && loadedEditableRows.Count == 0) {
				MessageEx.ShowWarningDialog("配分数を入力してください。", owner: ActiveWindow);
				return;
			}
			var confirm = newRecords.Count == 0
				? $"配分数が全て0のため、既存の配分 {loadedEditableRows.Count:N0} 件を削除します。よろしいですか？"
				: $"配分 {newRecords.Count:N0} 件（合計 {newRecords.Sum(x => x.Su):N0} 点）を登録します。よろしいですか？";
			if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;
			await CoreServiceClient.SaveHaibunAsync(loadedEditableRows, newRecords, "在庫配分", ct);
			await LoadEntryAsync(targetShohin, ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 配分を {newRecords.Count:N0} 件登録しました";
			MessageEx.ShowInformationDialog("登録完了しました。", owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "登録を中断しました"; }
		catch (Exception ex) {
			Message = $"登録失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	// ===== 読込 =====

	async Task LoadEntryAsync(MasterShohin shohin, CancellationToken ct) {
		targetShohin = shohin;
		TargetDisplay = $"{CodeNameDisplay.Format(shohin.Id, shohin.Code, shohin.Name)}　配分元: {SokoCode} {SokoName}";
		var skuList = await QuerySqlListAsync<DerivedShohinColSiz>(
			$"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin = @0 ORDER BY RowIdx", [Num(shohin.Id)], ct);
		var stock = (await QuerySqlListAsync<SummaryRealStock>(
			$"SELECT * FROM {nameof(SummaryRealStock)} WHERE Id_Soko = @0 AND Id_Shohin = @1", [Num(idSoko), Num(shohin.Id)], ct))
			.GroupBy(x => (x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su) - g.Sum(x => x.ReserveQty));
		loadedEditableRows = await QuerySqlListAsync<TranHaibun>(
			$"SELECT * FROM {nameof(TranHaibun)} WHERE Kubun = {KubunZaiko} AND Id_Soko = @0 AND Id_Shohin = @1 AND {TranHaibun.EditableWhereSql} ORDER BY Id",
			[Num(idSoko), Num(shohin.Id)], ct);
		var editingBySku = loadedEditableRows.GroupBy(h => (h.Id_Col, h.Id_Siz)).ToDictionary(g => g.Key, g => g.Sum(x => x.Su));
		// 有効在庫 = 実在庫 − 引当 ＋ 洗い替えで入れ直す自分の配分（在庫配分は引当対象）
		SkuColumns = [.. skuList.Select(c => new InventoryAllocationSku(c,
			stock.GetValueOrDefault((c.Id_Col, c.Id_Siz)) + editingBySku.GetValueOrDefault((c.Id_Col, c.Id_Siz))))];

		Rows = [];
		var tokui = await LoadTokuiAsync(loadedEditableRows.Select(x => x.Id_Tenpo), ct);
		SyncRows(tokui);
		foreach (var h in loadedEditableRows) {
			var row = Rows.FirstOrDefault(r => r.Id_Tenpo == h.Id_Tenpo);
			var cell = row?.Cells.FirstOrDefault(c => c.Sku.Id_Col == h.Id_Col && c.Sku.Id_Siz == h.Id_Siz);
			if (cell != null) cell.Su += h.Su;
		}
		var first = loadedEditableRows.FirstOrDefault();
		ShijiDay = FromYmd8(first?.DenDay) ?? DateTime.Today;
		NouhinDay = FromYmd8(first?.NouhinDay) ?? DateTime.Today;
		Memo = first?.Memo ?? string.Empty;
		if (first is { Id_Shain: > 0 }) {
			Id_Shain = first.Id_Shain;
			var shain = (await QuerySqlListAsync<MasterShain>($"SELECT * FROM {nameof(MasterShain)} WHERE Id = @0", [Num(first.Id_Shain)], ct)).FirstOrDefault();
			ShainDisplay = shain == null ? $"Id:{first.Id_Shain}" : CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
		}
		RefreshTotal();
		NotifyEditCommands();
		Message = $"既存の配分 {loadedEditableRows.Count:N0} 件を読み込みました。";
	}

	/// <summary>配分先の行を選択結果に合わせる（既存行の入力値は維持する）</summary>
	void SyncRows(IReadOnlyList<MasterTokui> selected) {
		foreach (var row in Rows.Where(r => selected.All(s => s.Id != r.Id_Tenpo)).ToList()) {
			foreach (var cell in row.Cells) cell.Su = 0;
			Rows.Remove(row);
		}
		foreach (var tokui in selected.Where(t => Rows.All(r => r.Id_Tenpo != t.Id))) {
			var row = new InventoryAllocationRow(tokui);
			row.SetCells([.. SkuColumns.Select(sku => new InventoryAllocationCell(sku, row) { Changed = (_, d) => GrandTotalSu += d })]);
			Rows.Add(row);
		}
		SelectedRow ??= Rows.FirstOrDefault();
		RefreshTotal();
		NotifyEditCommands();
	}

	void RefreshTotal() => GrandTotalSu = Rows.Sum(r => r.TotalSu);

	List<TranHaibun> BuildNewRecords(IReadOnlyDictionary<long, (int Tanka, int Jodai)> priceByTenpo) {
		var records = new List<TranHaibun>();
		if (targetShohin == null) return records;
		foreach (var row in Rows) {
			var (tanka, jodai) = priceByTenpo.TryGetValue(row.Id_Tenpo, out var price) ? price : (targetShohin.TankaJodai, targetShohin.TankaJodai);
			foreach (var cell in row.Cells.Where(c => c.Su > 0)) {
				records.Add(new TranHaibun {
					DenDay = ToYmd8(ShijiDay),
					NouhinDay = ToYmd8(NouhinDay),
					Id_Soko = idSoko,
					Id_Tenpo = row.Id_Tenpo,
					Kubun = KubunZaiko,
					Id_Shohin = targetShohin.Id,
					JanCode = cell.Sku.JanCode,
					Id_Col = cell.Sku.Id_Col,
					Id_Siz = cell.Sku.Id_Siz,
					Su = cell.Su,
					Tanka = tanka,
					Kingaku = cell.Su * tanka,
					Jodai = jodai,
					Gedai = targetShohin.TankaGenka,
					Memo = Memo,
					Id_Shain = Id_Shain,
				});
			}
		}
		return records;
	}

	/// <summary>
	/// 配分先ごとの単価。上代は配分先の店種で上代の系統（直営店・倉庫は店舗用、卸先・売仕店は本部売上用）を選んで解決する。
	/// 卸先(1)は 上代 × 掛率(RateProper) ÷ 100 の1円未満切捨（掛率0は上代のまま）。
	/// 戻り値は 配分先Id → (単価, 上代)。卸先も明細の上代には掛率前の上代を残す。
	/// </summary>
	async Task<Dictionary<long, (int Tanka, int Jodai)>> LoadPriceByDestinationAsync(MasterShohin shohin, IEnumerable<MasterTokui> destinations, CancellationToken ct) {
		var list = destinations.ToList();
		if (list.Count == 0) return [];
		List<string> parameters = [];
		var shohinParam = AddParameter(parameters, shohin.Id);
		var day = ToYmd8(ShijiDay);
		var dayExpr = day.Length == 8 ? AddParameter(parameters, day) : DerivedJodai.TodaySql;
		var taisho = $"CASE WHEN T.TenType IN (1,3) THEN {(int)EnumJodaiTaisho.Honbu} ELSE {(int)EnumJodaiTaisho.Tenpo} END";
		var sql = $"""
			SELECT T.Id AS Id, {DerivedJodai.FinalJodaiSql(shohinParam, taisho, "T.Id", dayExpr, "M")} AS TankaJodai
			FROM MasterTokui T, MasterShohin M
			WHERE M.Id = {shohinParam} AND T.Id IN ({string.Join(",", list.Select(x => x.Id))})
			""";
		var jodai = (await QuerySqlListAsync<MasterShohin>(sql, parameters, ct)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().TankaJodai);
		return list.ToDictionary(t => t.Id, t => {
			var j = jodai.GetValueOrDefault(t.Id, shohin.TankaJodai);
			var tanka = t.TenType == 1 && t.RateProper > 0 ? (int)Math.Floor(j * (decimal)t.RateProper / 100m) : j;
			return (tanka, j);
		});
	}

	/// <summary>商品一覧。在庫ありのみなら有効在庫が正の商品だけを SQL で絞る</summary>
	async Task<List<MasterShohin>> LoadShohinListAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [];
		AddCodeRange(clauses, parameters, "M.Code", ShohinCodeFrom, ShohinCodeTo);
		if (!string.IsNullOrWhiteSpace(ShohinName)) clauses.Add($"M.Name LIKE {AddParameter(parameters, $"%{ShohinName.Trim()}%")}");
		AddCodeRange(clauses, parameters, JsonCd("M.VBrand"), BrandFrom, BrandTo);
		AddCodeRange(clauses, parameters, JsonCd("M.VItem"), ItemFrom, ItemTo);
		if (ZaikoOnly) {
			clauses.Add($"""
				M.Id IN (SELECT R.Id_Shohin FROM {nameof(SummaryRealStock)} R WHERE R.Id_Soko = {AddParameter(parameters, idSoko)}
					GROUP BY R.Id_Shohin HAVING SUM(R.Su - R.ReserveQty) > 0)
				""");
		}
		var where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		var limit = MaxCount > 0 ? $"LIMIT {MaxCount}" : string.Empty;
		var sql = $"""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code, M.Name, M.TankaJodai, M.TankaGenka, M.VBrand, M.VItem, M.VSeason
			FROM {nameof(MasterShohin)} M
			{where}
			ORDER BY M.Code
			{limit}
			""";
		return await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
	}

	async Task<Dictionary<long, (int Su, int Reserve)>> LoadStockByShohinAsync(IReadOnlyCollection<long> ids, CancellationToken ct) {
		if (ids.Count == 0) return [];
		var rows = await QuerySqlListAsync<SummaryRealStock>($"""
			SELECT 0 AS Id, 0 AS Vdc, 0 AS Vdu, R.Id_Shohin, 0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(R.Su), 0) AS Su, IFNULL(SUM(R.ReserveQty), 0) AS ReserveQty
			FROM {nameof(SummaryRealStock)} R
			WHERE R.Id_Soko = @0 AND R.Id_Shohin IN ({string.Join(",", ids)})
			GROUP BY R.Id_Shohin
			""", [Num(idSoko)], ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => (x.Su, x.ReserveQty));
	}

	async Task<Dictionary<long, int>> LoadZaikoHaibunByShohinAsync(IReadOnlyCollection<long> ids, CancellationToken ct) {
		if (ids.Count == 0) return [];
		var rows = await QuerySqlListAsync<SummaryRealStock>($"""
			SELECT 0 AS Id, 0 AS Vdc, 0 AS Vdu, T.Id_Shohin, 0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz, IFNULL(SUM(T.Su), 0) AS Su
			FROM {nameof(TranHaibun)} T
			WHERE T.Kubun = {KubunZaiko} AND T.EndFlag = 0 AND T.Id_Soko = @0 AND T.Id_Shohin IN ({string.Join(",", ids)})
			GROUP BY T.Id_Shohin
			""", [Num(idSoko)], ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	/// <summary>商品別の直近4週売上と最終売上日（出荷売上＋店舗売上。直近 <see cref="SalesLookbackDays"/> 日だけを見る）</summary>
	async Task<Dictionary<long, (int Recent, DateTime? LastDay)>> LoadSalesByShohinAsync(IReadOnlyCollection<long> ids, CancellationToken ct) {
		if (ids.Count == 0) return [];
		var from = DateTime.Today.AddDays(-28).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var lookback = DateTime.Today.AddDays(-SalesLookbackDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var rows = await QuerySqlListAsync<TranHaibun>($"""
			SELECT 0 AS Id, 0 AS Vdc, 0 AS Vdu, X.Id_Shohin AS Id_Shohin,
				IFNULL(SUM(CASE WHEN X.DenDay >= @0 THEN X.Su ELSE 0 END), 0) AS Su,
				MAX(X.DenDay) AS DenDay
			FROM (
				SELECT {MeisaiNum("m", "Id_Shohin")} AS Id_Shohin, H.DenDay, {MeisaiNum("m", "Su")} * H.CalcFlag AS Su
				FROM Tran00Uriage H, json_each({SafeJmeisai("H")}) AS m WHERE H.CalcFlag <> 0 AND H.DenDay >= @1
				UNION ALL
				SELECT {MeisaiNum("m", "Id_Shohin")}, H.DenDay, {MeisaiNum("m", "Su")} * H.CalcFlag
				FROM Tran01Tenuri H, json_each({SafeJmeisai("H")}) AS m WHERE H.CalcFlag <> 0 AND H.DenDay >= @1
			) X
			WHERE X.Id_Shohin IN ({string.Join(",", ids)})
			GROUP BY X.Id_Shohin
			""", [from, lookback], ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => (x.Su, FromYmd8(x.DenDay)));
	}

	/// <summary>配分先別の売上数量（比率の基準）。店舗は店舗売上、卸先・売仕店は出荷売上。対象は同一商品／同一ブランド</summary>
	async Task<Dictionary<long, int>> LoadSalesByDestinationAsync(CancellationToken ct) {
		if (targetShohin == null || Rows.Count == 0) return [];
		List<string> parameters = [];
		var from = AddParameter(parameters, (SalesFrom ?? DateTime.Today.AddDays(-28)).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
		var to = AddParameter(parameters, (SalesTo ?? DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
		string shohinFilter;
		if (Scope == ScopeBrand) {
			var brand = AddParameter(parameters, targetShohin.VBrand?.Cd ?? string.Empty);
			shohinFilter = $"X.Id_Shohin IN (SELECT S.Id FROM {nameof(MasterShohin)} S WHERE {JsonCd("S.VBrand")} = {brand})";
		}
		else {
			shohinFilter = $"X.Id_Shohin = {AddParameter(parameters, targetShohin.Id)}";
		}
		var ids = string.Join(",", Rows.Select(r => r.Id_Tenpo));
		var rows = await QuerySqlListAsync<TranHaibun>($"""
			SELECT 0 AS Id, 0 AS Vdc, 0 AS Vdu, X.Id_Tenpo AS Id_Tenpo, IFNULL(SUM(X.Su), 0) AS Su
			FROM (
				SELECT H.Id_Tenpo AS Id_Tenpo, {MeisaiNum("m", "Id_Shohin")} AS Id_Shohin, {MeisaiNum("m", "Su")} * H.CalcFlag AS Su
				FROM Tran01Tenuri H, json_each({SafeJmeisai("H")}) AS m
				WHERE H.CalcFlag <> 0 AND H.DenDay BETWEEN {from} AND {to} AND H.Id_Tenpo IN ({ids})
				UNION ALL
				SELECT H.Id_Tokui, {MeisaiNum("m", "Id_Shohin")}, {MeisaiNum("m", "Su")} * H.CalcFlag
				FROM Tran00Uriage H, json_each({SafeJmeisai("H")}) AS m
				WHERE H.CalcFlag <> 0 AND H.DenDay BETWEEN {from} AND {to} AND H.Id_Tokui IN ({ids})
			) X
			WHERE {shohinFilter}
			GROUP BY X.Id_Tenpo
			""", parameters, ct);
		return rows.ToDictionary(x => x.Id_Tenpo, x => Math.Max(x.Su, 0));
	}

	/// <summary>
	/// 前回の配分（区分0/1、状態は問わない）の配分先別数量。この商品の直近の指示日の配分を使い、
	/// 無ければ同じブランドの直近の指示日の配分を使う。今回洗い替えで入れ直す行は除く。
	/// </summary>
	async Task<Dictionary<long, int>> LoadPreviousAllocationAsync(CancellationToken ct) {
		if (targetShohin == null) return [];
		var exclude = loadedEditableRows.Count == 0 ? "0" : string.Join(",", loadedEditableRows.Select(x => x.Id));
		var kubun = $"{(int)EnumHaibun.Hatsukai},{KubunZaiko}";
		async Task<Dictionary<long, int>> Query(string shohinFilter, List<string> parameters) {
			var rows = await QuerySqlListAsync<TranHaibun>($"""
				SELECT 0 AS Id, 0 AS Vdc, 0 AS Vdu, T.Id_Tenpo AS Id_Tenpo, IFNULL(SUM(T.Su), 0) AS Su
				FROM {nameof(TranHaibun)} T
				WHERE T.Kubun IN ({kubun}) AND T.Id NOT IN ({exclude}) AND {shohinFilter}
					AND T.DenDay = (SELECT MAX(P.DenDay) FROM {nameof(TranHaibun)} P
						WHERE P.Kubun IN ({kubun}) AND P.Id NOT IN ({exclude}) AND {shohinFilter.Replace("T.", "P.")})
				GROUP BY T.Id_Tenpo
				""", parameters, ct);
			return rows.ToDictionary(x => x.Id_Tenpo, x => x.Su);
		}
		var byShohin = await Query("T.Id_Shohin = @0", [Num(targetShohin.Id)]);
		if (byShohin.Count > 0) return byShohin;
		return await Query($"T.Id_Shohin IN (SELECT S.Id FROM {nameof(MasterShohin)} S WHERE {JsonCd("S.VBrand")} = @0)",
			[targetShohin.VBrand?.Cd ?? string.Empty]);
	}

	async Task<MasterTokui?> ResolveTokuiAsync(string code, CancellationToken ct) =>
		(await QuerySqlListAsync<MasterTokui>($"SELECT * FROM {nameof(MasterTokui)} WHERE Code = @0", [code.Trim()], ct)).FirstOrDefault();

	async Task<List<MasterTokui>> LoadTokuiAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		return await QuerySqlListAsync<MasterTokui>(
			$"SELECT * FROM {nameof(MasterTokui)} WHERE Id IN ({string.Join(",", list)}) ORDER BY Code", [], ct);
	}

	// ===== 共通ヘルパー =====

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	TResult? ShowSelect<TResult>(Type tableType, string where, string order) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	void StartBusy(string message) {
		IsBusy = true;
		Message = message;
		ClientLib.Cursor2Wait();
	}

	void FinishBusy() {
		IsBusy = false;
		ClientLib.Cursor2Normal();
	}

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

	static void AddCodeRange(List<string> clauses, List<string> parameters, string column, string? from, string? to) {
		var f = from?.Trim() ?? string.Empty;
		var t = to?.Trim() ?? string.Empty;
		if (f.Length > 0) clauses.Add($"{column} >= {AddParameter(parameters, f)}");
		if (t.Length > 0) clauses.Add($"{column} <= {AddParameter(parameters, t)}");
	}

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	static string JsonCd(string column) =>
		$"IFNULL(json_extract(CASE WHEN json_valid({column}) THEN {column} ELSE '{{}}' END, '$.Cd'), '')";

	static string SafeJmeisai(string alias) =>
		$"CASE WHEN json_valid({alias}.Jmeisai) THEN {alias}.Jmeisai ELSE '[]' END";

	static string MeisaiNum(string alias, string property) =>
		$"CAST(IFNULL(json_extract({alias}.value, '$.{property}'), 0) AS INTEGER)";

	static string ToYmd8(DateTime? value) => value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;

	static DateTime? FromYmd8(string? value) =>
		DateTime.TryParseExact(value, "yyyyMMdd", null, DateTimeStyles.None, out DateTime result) ? result : null;
}

/// <summary>タブ1の商品一覧行</summary>
public sealed class InventoryAllocationShohinRow(MasterShohin shohin, int realSu, int reserveQty, int zaikoHaibunSu, int recentSales, DateTime? lastSalesDate) {
	public MasterShohin Shohin { get; } = shohin;
	public string Code => Shohin.Code;
	public string Name => Shohin.Name;
	public string BrandDisplay => Shohin.VBrand == null ? string.Empty : $"{Shohin.VBrand.Cd} {Shohin.VBrand.Mei}".Trim();
	public int Jodai => Shohin.TankaJodai;
	public int RealSu { get; } = realSu;
	public int ReserveQty { get; } = reserveQty;
	/// <summary>有効在庫 = 実在庫 − 引当</summary>
	public int YukoSu => RealSu - ReserveQty;
	/// <summary>未確定の在庫配分</summary>
	public int ZaikoHaibunSu { get; } = zaikoHaibunSu;
	/// <summary>直近4週の売上数量</summary>
	public int RecentSales { get; } = recentSales;
	public DateTime? LastSalesDate { get; } = lastSalesDate;
	public string LastSalesDisplay => LastSalesDate?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "1年以上なし";
}

/// <summary>SKU 列。列見出しに有効在庫・配分計・配分後在庫を出す</summary>
public sealed partial class InventoryAllocationSku(DerivedShohinColSiz colsiz, int yukoSu) : ObservableObject {
	public long Id_Col { get; } = colsiz.Id_Col;
	public long Id_Siz { get; } = colsiz.Id_Siz;
	public string Code_Col { get; } = colsiz.Code_Col;
	public string Code_Siz { get; } = colsiz.Code_Siz;
	public string ColDisplay { get; } = $"{colsiz.Code_Col} {colsiz.Mei_Col}".Trim();
	public string SizDisplay { get; } = $"{colsiz.Code_Siz} {colsiz.Mei_Siz}".Trim();
	public string JanCode { get; } = colsiz.Jan1;

	/// <summary>有効在庫（実在庫 − 引当 ＋ 洗い替えで入れ直す自分の配分）</summary>
	public int YukoSu { get; } = yukoSu;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(AfterSu))]
	[NotifyPropertyChangedFor(nameof(IsShort))]
	public partial int HaibunTotalSu { get; set; }

	public int AfterSu => YukoSu - HaibunTotalSu;
	public bool IsShort => AfterSu < 0;
}

/// <summary>マトリクスの行（配分先1件）</summary>
public sealed partial class InventoryAllocationRow(MasterTokui tokui) : ObservableObject {
	public MasterTokui Tokui { get; } = tokui;
	public long Id_Tenpo => Tokui.Id;
	public string TenpoDisplay => CodeNameDisplay.Format(Tokui.Id, Tokui.Code, Tokui.Name);
	/// <summary>確定で作られる伝票。卸先・売仕店は出荷売上、倉庫・直営店は移動（決定 I4）</summary>
	public string KindDisplay => Tokui.TenType is 1 or 3 ? "出荷売上" : "移動";

	public IReadOnlyList<InventoryAllocationCell> Cells { get; private set; } = [];

	/// <summary>比率(%)。比率按分の重み</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(RatioText))]
	public partial decimal Ratio { get; set; }

	/// <summary>比率の表示・編集用。空は0</summary>
	public string RatioText {
		get => Ratio == 0 ? string.Empty : Ratio.ToString("0.#", CultureInfo.InvariantCulture);
		set {
			Ratio = decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(parsed, 0) : 0;
			OnPropertyChanged();
		}
	}

	[ObservableProperty]
	public partial int TotalSu { get; set; }

	public void SetCells(IReadOnlyList<InventoryAllocationCell> cells) => Cells = cells;
}

/// <summary>マトリクスのセル（配分先 × SKU）</summary>
public sealed partial class InventoryAllocationCell(InventoryAllocationSku sku, InventoryAllocationRow owner) : ObservableObject {
	public InventoryAllocationSku Sku { get; } = sku;

	public Action<InventoryAllocationCell, int>? Changed { get; init; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SuText))]
	public partial int Su { get; set; }

	/// <summary>セル表示・編集用。0 は空白で見せる</summary>
	public string SuText {
		get => Su == 0 ? string.Empty : Su.ToString("#,##0", CultureInfo.InvariantCulture);
		set {
			string text = (value ?? string.Empty).Replace(",", string.Empty).Trim();
			Su = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? Math.Max(parsed, 0) : 0;
			OnPropertyChanged();
		}
	}

	partial void OnSuChanged(int oldValue, int newValue) {
		int delta = newValue - oldValue;
		Sku.HaibunTotalSu += delta;
		owner.TotalSu += delta;
		Changed?.Invoke(this, delta);
	}
}
