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
/// 仕入配分入力(商品別)。入荷倉庫と商品を指定し、未完了の発注の入荷予定を、配分先（倉庫・卸先・売仕店・直営店）× SKU で振り分ける。
/// <para>
/// 旧CV.netの初回配分入力に相当し、店舗配分入力(初回)を置き換える。作る配分は <see cref="EnumHaibun.Hatsukai"/>(仕入配分)で、
/// 配分先×SKU の数を、その倉庫×商品の発注へ納品予定日の古い順に割り付け、<c>RelateNo1</c> に発注Idを持たせる
/// （<see cref="HaibunOrderDistributor"/>）。発注数を超える配分は登録しない（区分0は発注Id必須）。
/// 入荷するまで引当に入らず、仕入を計上すると入荷済み数（<see cref="TranHaibun.ArrivedSu"/>）の分だけ引当・確定できる（サーバの ArrivalDb）。
/// 按分（同数・比率）は在庫配分入力と同じ <see cref="AllocationCalculator"/>。
/// 発注に対して入荷前の振り分けを作り、供給済みのArrivedSuだけを引当・確定に使う。保存後はサーバで入荷割当を再計算する。
/// </para>
/// </summary>
public partial class PurchaseReceiptAllocationInputViewModel : BaseViewModel {
	const int KubunShiire = (int)EnumHaibun.Hatsukai;
	const string DestinationTenTypes = "0,1,3,6";
	const int MaxOrderCount = 500;

	public const string TotalKano = "配分可能";
	public const string TotalInput = "入力した数";

	long idSoko;
	MasterShohin? targetShohin;
	List<TranHaibun> loadedEditableRows = [];
	/// <summary>SKU ごとの割り付け先発注（納品予定日の古い順に並べる前の一覧）</summary>
	Dictionary<(long Col, long Siz), List<HaibunOrderZan>> orderZans = [];

	[ObservableProperty]
	public partial string Message { get; set; } = "入荷倉庫と商品を指定して［検索］を押してください。";

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== 条件 =====

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? NouhinDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? NouhinDayTo { get; set; }

	[ObservableProperty]
	public partial string ShiireCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? ShijiDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? NouhinDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial string Memo { get; set; } = string.Empty;

	// ===== 一覧 =====

	[ObservableProperty]
	public partial ObservableCollection<PurchaseReceiptAllocationSku> SkuColumns { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<PurchaseReceiptAllocationRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial PurchaseReceiptAllocationRow? SelectedRow { get; set; }

	/// <summary>マトリクスで選択中のSKU列（-1 は未選択）。Viewが設定する</summary>
	[ObservableProperty]
	public partial int SelectedSkuIndex { get; set; } = -1;

	[ObservableProperty]
	public partial int GrandTotalSu { get; set; }

	[ObservableProperty]
	public partial int OrderCount { get; set; }

	// ===== 按分パネル（在庫配分入力と同じ） =====

	public IReadOnlyList<string> Modes { get; } = [InventoryAllocationInputViewModel.ModeRatio, InventoryAllocationInputViewModel.ModeEqual];
	public IReadOnlyList<string> Bases { get; } = [InventoryAllocationInputViewModel.BasisPrevious, InventoryAllocationInputViewModel.BasisManual];
	public IReadOnlyList<string> Roundings { get; } = ["切捨", "四捨五入"];
	public IReadOnlyList<string> TotalModes { get; } = [TotalKano, TotalInput];
	public IReadOnlyList<string> Targets { get; } = ["全SKU", "選択中の列"];

	[ObservableProperty]
	public partial string Mode { get; set; } = InventoryAllocationInputViewModel.ModeRatio;

	[ObservableProperty]
	public partial int SameQty { get; set; } = 1;

	[ObservableProperty]
	public partial string Basis { get; set; } = InventoryAllocationInputViewModel.BasisPrevious;

	[ObservableProperty]
	public partial string Rounding { get; set; } = "四捨五入";

	[ObservableProperty]
	public partial string TotalMode { get; set; } = TotalKano;

	[ObservableProperty]
	public partial int TotalQty { get; set; }

	[ObservableProperty]
	public partial string Target { get; set; } = "全SKU";

	bool HasTarget() => targetShohin != null && !IsBusy;

	partial void OnIsBusyChanged(bool value) => NotifyEditCommands();

	void NotifyEditCommands() {
		CalcRatioCommand.NotifyCanExecuteChanged();
		ApplyAllocationCommand.NotifyCanExecuteChanged();
		AddDestinationsCommand.NotifyCanExecuteChanged();
		LoadPreviousDestinationsCommand.NotifyCanExecuteChanged();
		ClearAllCommand.NotifyCanExecuteChanged();
		DoRegisterCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	void SelectSoko() {
		var soko = ShowSelect<MasterTokui>(typeof(MasterTokui), "TenType IN (0,3,6)", "Code");
		if (soko == null) return;
		SokoCode = soko.Code;
		SokoName = soko.Name;
	}

	[RelayCommand]
	void SelectShohin() {
		var shohin = ShowSelect<MasterShohin>(typeof(MasterShohin), string.Empty, "Code");
		if (shohin == null) return;
		ShohinCode = shohin.Code;
		ShohinName = shohin.Name;
	}

	/// <summary>検索(F5)。倉庫×商品の未完了の発注と既存の仕入配分を読み込み、配分先×SKU のマトリクスを作る</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(SokoCode) || string.IsNullOrWhiteSpace(ShohinCode)) {
			MessageEx.ShowWarningDialog("入荷倉庫と商品を指定してください。", owner: ActiveWindow);
			return;
		}
		StartBusy("発注を取得中...");
		try {
			await LoadMatrixAsync(ct);
		}
		catch (OperationCanceledException) { Message = "検索を中断しました"; }
		catch (Exception ex) {
			Message = $"検索失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	[RelayCommand(CanExecute = nameof(HasTarget))]
	void AddDestinations() {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return;
		vm.SetParam(typeof(MasterTokui), $"TenType IN ({DestinationTenTypes}) AND Id <> {idSoko}", "Code",
			selectedIds: Rows.Select(x => x.Id_Tenpo));
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var selected = vm.GetSelectedItems<MasterTokui>();
		if (Rows.Where(r => selected.All(s => s.Id != r.Id_Tenpo)).Any(r => r.TotalSu > 0) && MessageEx.ShowQuestionDialog(
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

	/// <summary>比率を計算（前回配分）。手入力は何もしない</summary>
	[RelayCommand(CanExecute = nameof(HasTarget), IncludeCancelCommand = true)]
	async Task CalcRatio(CancellationToken ct) {
		if (Rows.Count == 0) {
			MessageEx.ShowWarningDialog("配分先を追加してください。", owner: ActiveWindow);
			return;
		}
		if (Basis == InventoryAllocationInputViewModel.BasisManual) {
			Message = "比率を手入力してください。";
			return;
		}
		StartBusy("比率を計算中...");
		try {
			var basis = await LoadPreviousAllocationAsync(ct);
			var total = Rows.Sum(r => basis.GetValueOrDefault(r.Id_Tenpo));
			foreach (var row in Rows) {
				row.Ratio = total > 0 ? Math.Round(basis.GetValueOrDefault(row.Id_Tenpo) * 100m / total, 1) : 0m;
			}
			Message = total > 0 ? $"前回配分から比率を計算しました（合計 {total:N0} 点）。" : "前回配分がありません。按分すると均等になります。";
		}
		catch (OperationCanceledException) { Message = "中断しました"; }
		catch (Exception ex) {
			Message = $"比率の計算失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>按分実行。SKUごとに同数または比率で配分数を入れる（既存の入力は上書き）。総数の既定は配分可能数</summary>
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
		var rounding = Rounding == "切捨" ? AllocationRounding.Floor : AllocationRounding.Round;
		var weights = Rows.Select(r => r.Ratio).ToList();
		foreach (var i in targets) {
			var total = TotalMode == TotalKano ? Math.Max(SkuColumns[i].KanoSu, 0) : Math.Max(TotalQty, 0);
			var values = Mode == InventoryAllocationInputViewModel.ModeEqual
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

	/// <summary>登録(F2)。配分先×SKU の数を発注へ割り付け、既存の仕入配分を洗い替える。発注数を超える配分は登録しない</summary>
	[RelayCommand(CanExecute = nameof(HasTarget), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (targetShohin == null) return;
		if (ShijiDay == null || NouhinDay == null) {
			MessageEx.ShowWarningDialog("指示日と納品日を入力してください。", owner: ActiveWindow);
			return;
		}
		var over = SkuColumns.Where(s => s.RemainSu < 0).ToList();
		if (over.Count > 0) {
			MessageEx.ShowWarningDialog(
				$"発注の配分可能数を超えるSKUが {over.Count:N0} 件あります（{string.Join("、", over.Take(5).Select(s => $"{s.Code_Col}/{s.Code_Siz} 超過{-s.RemainSu:N0}"))}）。"
				+ "\n仕入配分は発注に紐付けて登録するため、発注数を超えては登録できません。", owner: ActiveWindow);
			return;
		}
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
			await CoreServiceClient.SaveHaibunAsync(loadedEditableRows, newRecords, "仕入配分", ct);
			await LoadMatrixAsync(ct);
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

	async Task LoadMatrixAsync(CancellationToken ct) {
		var soko = (await QuerySqlListAsync<MasterTokui>($"SELECT * FROM {nameof(MasterTokui)} WHERE Code = @0", [SokoCode.Trim()], ct)).FirstOrDefault()
			?? throw new InvalidOperationException($"倉庫コード {SokoCode} が見つかりません。");
		var shohin = (await QuerySqlListAsync<MasterShohin>($"SELECT * FROM {nameof(MasterShohin)} WHERE Code = @0", [ShohinCode.Trim()], ct)).FirstOrDefault()
			?? throw new InvalidOperationException($"商品コード {ShohinCode} が見つかりません。");
		idSoko = soko.Id;
		targetShohin = shohin;
		SokoName = soko.Name;
		ShohinName = shohin.Name;

		var orders = await LoadOrdersAsync(ct);
		OrderCount = orders.Count;
		var orderIds = orders.Select(x => x.Id).ToList();
		var received = await LoadReceivedAsync(orderIds, ct);
		var haibun = orderIds.Count == 0 ? [] : await QuerySqlListAsync<TranHaibun>(
			$"SELECT * FROM {nameof(TranHaibun)} WHERE Kubun = {KubunShiire} AND Id_Shohin = @0 AND RelateNo1 IN ({string.Join(",", orderIds)}) ORDER BY Id",
			[Num(shohin.Id)], ct);
		loadedEditableRows = [.. haibun.Where(IsEditable)];
		// 洗い替え対象以外の仕入配分が発注の枠を使っている数（完了分は確定数、未完了は指示数）
		var usedByOthers = haibun.Where(h => !IsEditable(h))
			.GroupBy(h => ((long)h.RelateNo1, h.Id_Col, h.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(h => h.EndFlag != 0 ? h.JitsuSu : h.Su));

		// 発注×SKU の発注数と配分可能数
		orderZans = [];
		var hachuBySku = new Dictionary<(long, long), int>();
		foreach (var order in orders) {
			foreach (var line in (order.Jmeisai ?? []).Where(m => m.Id_Shohin == shohin.Id).GroupBy(m => (m.Id_Col, m.Id_Siz))) {
				var su = line.Sum(m => m.Su);
				hachuBySku[line.Key] = hachuBySku.GetValueOrDefault(line.Key) + su;
				var kano = su - usedByOthers.GetValueOrDefault((order.Id, line.Key.Id_Col, line.Key.Id_Siz));
				if (!orderZans.TryGetValue(line.Key, out var list)) orderZans[line.Key] = list = [];
				var day = string.IsNullOrEmpty(order.NouhinDay) ? order.DenDay : order.NouhinDay;
				list.Add(new HaibunOrderZan(order.Id, day, kano, 0, 0, 0));
			}
		}
		var colsiz = await QuerySqlListAsync<DerivedShohinColSiz>(
			$"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin = @0 ORDER BY RowIdx", [Num(shohin.Id)], ct);
		var used = hachuBySku.Keys.Concat(loadedEditableRows.Select(h => (h.Id_Col, h.Id_Siz))).ToHashSet();
		SkuColumns = [.. colsiz.Where(c => used.Contains((c.Id_Col, c.Id_Siz))).Select(c => new PurchaseReceiptAllocationSku(c,
			hachuSu: hachuBySku.GetValueOrDefault((c.Id_Col, c.Id_Siz)),
			nyukaSu: received.GetValueOrDefault((c.Id_Col, c.Id_Siz)),
			kanoSu: (orderZans.GetValueOrDefault((c.Id_Col, c.Id_Siz)) ?? []).Sum(z => Math.Max(z.ZanSu, 0))))];

		Rows = [];
		SyncRows(await LoadTokuiAsync(loadedEditableRows.Select(x => x.Id_Tenpo), ct));
		foreach (var h in loadedEditableRows) {
			var cell = Rows.FirstOrDefault(r => r.Id_Tenpo == h.Id_Tenpo)?.Cells.FirstOrDefault(c => c.Sku.Id_Col == h.Id_Col && c.Sku.Id_Siz == h.Id_Siz);
			if (cell != null) cell.Su += h.Su;
		}
		var first = loadedEditableRows.FirstOrDefault();
		ShijiDay = FromYmd8(first?.DenDay) ?? ShijiDay ?? DateTime.Today;
		NouhinDay = FromYmd8(first?.NouhinDay) ?? NouhinDay ?? DateTime.Today;
		GrandTotalSu = Rows.Sum(r => r.TotalSu);
		NotifyEditCommands();
		var limited = orders.Count >= MaxOrderCount ? $"（発注が {MaxOrderCount:N0} 件を超えたため打ち切りました）" : string.Empty;
		Message = orders.Count == 0
			? "未完了の発注がありません。"
			: $"発注 {orders.Count:N0} 件・既存の仕入配分 {loadedEditableRows.Count:N0} 件を読み込みました。{limited}";
	}

	/// <summary>入荷倉庫×商品の未完了の発注（仕入返品などのマイナス伝票は除く）</summary>
	async Task<List<Tran13Hachu>> LoadOrdersAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [
			"H.CalcFlag > 0", "H.EndFlag = 0",
			$"H.Id_Soko = {AddParameter(parameters, idSoko)}",
			$"EXISTS (SELECT 1 FROM json_each({SafeJmeisai("H")}) AS m WHERE {MeisaiNum("m", "Id_Shohin")} = {AddParameter(parameters, targetShohin!.Id)})",
		];
		if (NouhinDayFrom is DateTime from) clauses.Add($"H.NouhinDay >= {AddParameter(parameters, from.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}");
		if (NouhinDayTo is DateTime to) clauses.Add($"H.NouhinDay <= {AddParameter(parameters, to.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}");
		if (!string.IsNullOrWhiteSpace(ShiireCode)) {
			clauses.Add($"H.Id_Shiire IN (SELECT Id FROM {nameof(MasterShiire)} WHERE Code = {AddParameter(parameters, ShiireCode.Trim())})");
		}
		return await QuerySqlListAsync<Tran13Hachu>($"""
			SELECT H.* FROM {nameof(Tran13Hachu)} H
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY H.NouhinDay, H.Id
			LIMIT {MaxOrderCount}
			""", parameters, ct);
	}

	/// <summary>対象発注に紐付く、入荷倉庫への仕入のSKU別入荷数（仕入返品は差し引く）</summary>
	async Task<Dictionary<(long, long), int>> LoadReceivedAsync(IReadOnlyCollection<long> orderIds, CancellationToken ct) {
		if (orderIds.Count == 0 || targetShohin == null) return [];
		var rows = await QuerySqlListAsync<SummaryRealStock>($"""
			SELECT 0 AS Id, 0 AS Vdc, 0 AS Vdu, 0 AS Id_Soko, 0 AS Id_Shohin,
				{MeisaiNum("m", "Id_Col")} AS Id_Col, {MeisaiNum("m", "Id_Siz")} AS Id_Siz,
				SUM({MeisaiNum("m", "Su")} * S.CalcFlag) AS Su
			FROM Tran03Shiire S, json_each({SafeJmeisai("S")}) AS m
			WHERE S.CalcFlag <> 0 AND S.Id_Soko = @0 AND S.RelateNo1 IN ({string.Join(",", orderIds)})
				AND {MeisaiNum("m", "Id_Shohin")} = @1
			GROUP BY 6, 7
			""", [Num(idSoko), Num(targetShohin.Id)], ct);
		return rows.ToDictionary(x => (x.Id_Col, x.Id_Siz), x => x.Su);
	}

	/// <summary>前回の配分（区分0/1）の配分先別数量。この商品、無ければ同じブランドの直近の指示日</summary>
	async Task<Dictionary<long, int>> LoadPreviousAllocationAsync(CancellationToken ct) {
		if (targetShohin == null) return [];
		var exclude = loadedEditableRows.Count == 0 ? "0" : string.Join(",", loadedEditableRows.Select(x => x.Id));
		var kubun = $"{(int)EnumHaibun.Hatsukai},{(int)EnumHaibun.Zaiko}";
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

	/// <summary>配分先ごとの (単価, 上代)。在庫配分入力と同じ規則（上代の系統は店種で選び、卸先は上代×掛率の1円未満切捨）</summary>
	async Task<Dictionary<long, (int Tanka, int Jodai)>> LoadPriceByDestinationAsync(MasterShohin shohin, IEnumerable<MasterTokui> destinations, CancellationToken ct) {
		var list = destinations.ToList();
		if (list.Count == 0) return [];
		List<string> parameters = [];
		var shohinParam = AddParameter(parameters, shohin.Id);
		var day = ToYmd8(ShijiDay);
		var dayExpr = day.Length == 8 ? AddParameter(parameters, day) : DerivedJodai.TodaySql;
		var taisho = $"CASE WHEN T.TenType IN (1,3) THEN {(int)EnumJodaiTaisho.Honbu} ELSE {(int)EnumJodaiTaisho.Tenpo} END";
		var jodai = (await QuerySqlListAsync<MasterShohin>($"""
			SELECT T.Id AS Id, {DerivedJodai.FinalJodaiSql(shohinParam, taisho, "T.Id", dayExpr, "M")} AS TankaJodai
			FROM MasterTokui T, MasterShohin M
			WHERE M.Id = {shohinParam} AND T.Id IN ({string.Join(",", list.Select(x => x.Id))})
			""", parameters, ct)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().TankaJodai);
		return list.ToDictionary(t => t.Id, t => {
			var j = jodai.GetValueOrDefault(t.Id, shohin.TankaJodai);
			return (t.TenType == 1 && t.RateProper > 0 ? (int)Math.Floor(j * (decimal)t.RateProper / 100m) : j, j);
		});
	}

	async Task<List<MasterTokui>> LoadTokuiAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		return await QuerySqlListAsync<MasterTokui>(
			$"SELECT * FROM {nameof(MasterTokui)} WHERE Id IN ({string.Join(",", list)}) ORDER BY Code", [], ct);
	}

	void SyncRows(IReadOnlyList<MasterTokui> selected) {
		foreach (var row in Rows.Where(r => selected.All(s => s.Id != r.Id_Tenpo)).ToList()) {
			foreach (var cell in row.Cells) cell.Su = 0;
			Rows.Remove(row);
		}
		foreach (var tokui in selected.Where(t => Rows.All(r => r.Id_Tenpo != t.Id))) {
			var row = new PurchaseReceiptAllocationRow(tokui);
			row.SetCells([.. SkuColumns.Select(sku => new PurchaseReceiptAllocationCell(sku, row) { Changed = (_, d) => GrandTotalSu += d })]);
			Rows.Add(row);
		}
		SelectedRow ??= Rows.FirstOrDefault();
		GrandTotalSu = Rows.Sum(r => r.TotalSu);
		NotifyEditCommands();
	}

	/// <summary>
	/// 配分先×SKU の数を発注へ割り付けて <see cref="TranHaibun"/> にする。
	/// <para>
	/// まず、その配分先×SKU が既に紐付いていた発注へ、元の数を上限に割り付ける（保存し直しで入荷済みの発注から
	/// 未入荷の発注へ付け替わり、入荷済み数が失われるのを防ぐ。Step 4 レビュー指摘）。
	/// 残りは納品予定日の古い発注から割り付ける。
	/// </para>
	/// </summary>
	List<TranHaibun> BuildNewRecords(IReadOnlyDictionary<long, (int Tanka, int Jodai)> prices) {
		var records = new List<TranHaibun>();
		if (targetShohin == null) return records;
		// 発注ごとの残り枠を、配分先をまたいで減らしていく（同じ発注を複数の配分先で分け合う）
		var remaining = orderZans.ToDictionary(kv => kv.Key, kv => kv.Value.Select(z => z).ToList());
		var previousLinks = loadedEditableRows.Where(h => h.RelateNo1 > 0)
			.GroupBy(h => (h.Id_Tenpo, h.Id_Col, h.Id_Siz))
			.ToDictionary(g => g.Key, g => g.GroupBy(h => (long)h.RelateNo1).Select(x => (Hachu: x.Key, Su: x.Sum(h => h.Su))).ToList());
		foreach (var row in Rows) {
			var (tanka, jodai) = prices.TryGetValue(row.Id_Tenpo, out var p) ? p : (targetShohin.TankaJodai, targetShohin.TankaJodai);
			foreach (var cell in row.Cells.Where(c => c.Su > 0)) {
				var key = (cell.Sku.Id_Col, cell.Sku.Id_Siz);
				var zans = remaining.GetValueOrDefault(key) ?? [];
				var shares = new List<HaibunOrderShare>();
				var rest = cell.Su;
				foreach (var (hachu, prevSu) in previousLinks.GetValueOrDefault((row.Id_Tenpo, cell.Sku.Id_Col, cell.Sku.Id_Siz)) ?? []) {
					var index = zans.FindIndex(z => z.Id_Juchu == hachu);
					if (index < 0 || rest == 0) continue;
					var take = Math.Min(rest, Math.Min(prevSu, Math.Max(zans[index].ZanSu, 0)));
					if (take <= 0) continue;
					shares.Add(new HaibunOrderShare(hachu, take, 0, 0, 0));
					zans[index] = zans[index] with { ZanSu = zans[index].ZanSu - take };
					rest -= take;
				}
				foreach (var share in HaibunOrderDistributor.Distribute(rest, zans).Where(s => s.Id_Juchu > 0)) {
					shares.Add(share);
					var index = zans.FindIndex(z => z.Id_Juchu == share.Id_Juchu);
					zans[index] = zans[index] with { ZanSu = zans[index].ZanSu - share.Su };
				}
				foreach (var share in shares) {
					records.Add(new TranHaibun {
						DenDay = ToYmd8(ShijiDay),
						NouhinDay = ToYmd8(NouhinDay),
						Id_Soko = idSoko,
						Id_Tenpo = row.Id_Tenpo,
						Kubun = KubunShiire,
						Id_Shohin = targetShohin.Id,
						JanCode = cell.Sku.JanCode,
						Id_Col = cell.Sku.Id_Col,
						Id_Siz = cell.Sku.Id_Siz,
						Su = share.Su,
						Tanka = tanka,
						Kingaku = share.Su * tanka,
						Jodai = jodai,
						Gedai = targetShohin.TankaGenka,
						RelateNo1 = (int)share.Id_Juchu,
						Memo = Memo,
					});
				}
			}
		}
		return records;
	}

	static bool IsEditable(TranHaibun h) => h.SendFlg == 0 && h.EndFlag == 0 && string.IsNullOrEmpty(h.KakuteiDay);

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

/// <summary>SKU 列。見出しに発注・入荷・配分可能・配分・残を出す</summary>
public sealed partial class PurchaseReceiptAllocationSku(DerivedShohinColSiz colsiz, int hachuSu, int nyukaSu, int kanoSu) : ObservableObject {
	public long Id_Col { get; } = colsiz.Id_Col;
	public long Id_Siz { get; } = colsiz.Id_Siz;
	public string Code_Col { get; } = colsiz.Code_Col;
	public string Code_Siz { get; } = colsiz.Code_Siz;
	public string ColDisplay { get; } = $"{colsiz.Code_Col} {colsiz.Mei_Col}".Trim();
	public string SizDisplay { get; } = $"{colsiz.Code_Siz} {colsiz.Mei_Siz}".Trim();
	public string JanCode { get; } = colsiz.Jan1;
	/// <summary>対象発注の発注数</summary>
	public int HachuSu { get; } = hachuSu;
	/// <summary>入荷済み数（仕入数）</summary>
	public int NyukaSu { get; } = nyukaSu;
	/// <summary>配分可能数（発注数 − 洗い替え対象以外の仕入配分）</summary>
	public int KanoSu { get; } = kanoSu;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(RemainSu))]
	[NotifyPropertyChangedFor(nameof(IsOver))]
	public partial int HaibunTotalSu { get; set; }

	/// <summary>残 = 配分可能 − 配分。マイナスは発注数超過で登録できない</summary>
	public int RemainSu => KanoSu - HaibunTotalSu;
	public bool IsOver => RemainSu < 0;
}

/// <summary>マトリクスの行（配分先1件）</summary>
public sealed partial class PurchaseReceiptAllocationRow(MasterTokui tokui) : ObservableObject {
	public MasterTokui Tokui { get; } = tokui;
	public long Id_Tenpo => Tokui.Id;
	public string TenpoDisplay => CodeNameDisplay.Format(Tokui.Id, Tokui.Code, Tokui.Name);
	public string KindDisplay => Tokui.TenType is 1 or 3 ? "出荷売上" : "移動";
	public IReadOnlyList<PurchaseReceiptAllocationCell> Cells { get; private set; } = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(RatioText))]
	public partial decimal Ratio { get; set; }

	public string RatioText {
		get => Ratio == 0 ? string.Empty : Ratio.ToString("0.#", CultureInfo.InvariantCulture);
		set {
			Ratio = decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(parsed, 0) : 0;
			OnPropertyChanged();
		}
	}

	[ObservableProperty]
	public partial int TotalSu { get; set; }

	public void SetCells(IReadOnlyList<PurchaseReceiptAllocationCell> cells) => Cells = cells;
}

/// <summary>マトリクスのセル（配分先 × SKU）</summary>
public sealed partial class PurchaseReceiptAllocationCell(PurchaseReceiptAllocationSku sku, PurchaseReceiptAllocationRow owner) : ObservableObject {
	public PurchaseReceiptAllocationSku Sku { get; } = sku;
	public Action<PurchaseReceiptAllocationCell, int>? Changed { get; init; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SuText))]
	public partial int Su { get; set; }

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
