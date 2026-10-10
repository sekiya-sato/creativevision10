using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 受注配分入力(商品別)。倉庫と商品を指定し、行＝得意先・列＝SKU のマトリクスで、複数の得意先の受注残へまとめて配分する。
/// <para>
/// 旧CV.netの受注配分入力（品番を選び得意先×SKUへ受注残を読み込んで配分）に相当する。受注伝票1件ずつ配分する
/// 受注配分入力(伝票別)（<see cref="JuchuHaibunInputViewModel"/>）と同じ <see cref="EnumHaibun.Juchu"/> の配分を作る。
/// 得意先×SKU の配分数は、その得意先の受注へ受注日の古い順に割り付け、受注残の超過分は受注に紐付かない配分にする
/// （<see cref="HaibunOrderDistributor"/>）。保存は既存配分の洗い替えを1往復で行う（<c>HaibunSaveParam</c>）。
/// 受注への割付規則は CvBase/HaibunOrderDistributor.cs を参照する。
/// </para>
/// </summary>
public partial class SalesOrderAllocationInputViewModel : BaseViewModel {
	const int KubunJuchu = (int)EnumHaibun.Juchu;
	/// <summary>対象とする受注区分（受注・追加受注）。返品・値引は配分しない</summary>
	static readonly int[] TargetJuchuKubun = [(int)EnumJuchu.Juchu, (int)EnumJuchu.FollowUpJuchu];
	const int MaxOrderCount = 2000;

	long idSoko;
	long idShohin;
	/// <summary>修正対象として読み込んだ既存配分（洗い替えで削除する。Id/Vdu 保持）</summary>
	List<TranHaibun> loadedEditableRows = [];
	/// <summary>得意先×SKU ごとの割り付け先受注</summary>
	Dictionary<(long Tokui, AllocSkuKey Sku), List<HaibunOrderZan>> orderZans = [];
	/// <summary>得意先ごとの入力社員（最も新しい受注の担当者）</summary>
	Dictionary<long, long> shainByTokui = [];

	[ObservableProperty]
	public partial string Message { get; set; } = "倉庫と商品を指定して［検索］を押してください。";

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
	public partial DateTime? JuchuDayFrom { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

	[ObservableProperty]
	public partial DateTime? JuchuDayTo { get; set; }

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	/// <summary>登録する配分の指示日</summary>
	[ObservableProperty]
	public partial DateTime? ShijiDay { get; set; } = DateTime.Today;

	/// <summary>登録する配分の納品日</summary>
	[ObservableProperty]
	public partial DateTime? NouhinDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial string Memo { get; set; } = string.Empty;

	// ===== 一覧 =====

	/// <summary>SKU 列（列見出しに有効在庫・受注残計・配分計・配分後在庫を出す）</summary>
	[ObservableProperty]
	public partial ObservableCollection<SalesOrderAllocationSku> SkuColumns { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<SalesOrderAllocationRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial int GrandTotalSu { get; set; }

	[ObservableProperty]
	public partial int ZanTotalSu { get; set; }

	bool HasRows() => Rows.Count > 0 && !IsBusy;

	partial void OnRowsChanged(ObservableCollection<SalesOrderAllocationRow> value) => NotifyEditCommands();
	partial void OnIsBusyChanged(bool value) => NotifyEditCommands();

	void NotifyEditCommands() {
		LoadZanCommand.NotifyCanExecuteChanged();
		FillByStockCommand.NotifyCanExecuteChanged();
		ClearAllCommand.NotifyCanExecuteChanged();
		DoRegisterCommand.NotifyCanExecuteChanged();
	}

	// 倉庫・商品を変えたら旧条件のマトリクスを無効化する（旧対象の配分を洗い替えさせない。AGENTS 7.3）
	partial void OnSokoCodeChanged(string value) => InvalidateMatrix("倉庫");
	partial void OnShohinCodeChanged(string value) => InvalidateMatrix("商品");

	void InvalidateMatrix(string label) {
		if (Rows.Count == 0 && loadedEditableRows.Count == 0) return;
		ClearMatrix();
		Message = $"{label}が変わったため一覧をクリアしました。［検索］を押してください。";
	}

	/// <summary>読み込んだマトリクスと洗い替え対象を破棄し、登録できない状態に戻す。</summary>
	void ClearMatrix() {
		idSoko = 0;
		idShohin = 0;
		loadedEditableRows = [];
		orderZans = [];
		shainByTokui = [];
		Rows = [];
		SkuColumns = [];
		RefreshTotals();
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

	/// <summary>検索(F5)。倉庫・商品の受注残と既存配分を読み込み、得意先×SKU のマトリクスを作る</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		if (IsBusy) return;
		if (string.IsNullOrWhiteSpace(SokoCode) || string.IsNullOrWhiteSpace(ShohinCode)) {
			MessageEx.ShowWarningDialog("倉庫と商品を指定してください。", owner: ActiveWindow);
			return;
		}
		if (JuchuDayFrom != null && JuchuDayTo != null && JuchuDayFrom > JuchuDayTo) {
			MessageEx.ShowWarningDialog("受注日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		StartBusy("受注残を取得中...");
		try {
			await LoadMatrixAsync(ct);
		}
		catch (OperationCanceledException) {
			// 読込途中（対象Idと既存配分だけ新しい等）の状態で登録させない
			ClearMatrix();
			Message = "検索を中断しました";
		}
		catch (Exception ex) {
			ClearMatrix();
			Message = $"検索失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>受注残読込。受注のあるセルを受注残で上書きする（在庫は見ない）</summary>
	[RelayCommand(CanExecute = nameof(HasRows))]
	void LoadZan() {
		foreach (var cell in Rows.SelectMany(r => r.Cells).Where(c => c.HasOrder)) cell.Su = Math.Max(cell.ZanSu, 0);
		Message = "受注残を読み込みました。";
	}

	/// <summary>在庫内で受注日順に読込。SKUごとに有効在庫の範囲で、受注日の古い得意先から受注残を割り当てる</summary>
	[RelayCommand(CanExecute = nameof(HasRows))]
	void FillByStock() {
		for (int i = 0; i < SkuColumns.Count; i++) {
			// Rows は「最も古い受注日 → 得意先コード」の順に並んでいる（優先順）
			var cells = Rows.Select(r => r.Cells[i]).ToList();
			var zans = cells.Select(c => c.HasOrder ? c.ZanSu : 0).ToList();
			var filled = HaibunOrderDistributor.FillByStock(SkuColumns[i].YukoSu, zans);
			for (int k = 0; k < cells.Count; k++) cells[k].Su = filled[k];
		}
		Message = "有効在庫の範囲で、受注日の古い得意先から受注残を割り当てました。";
	}

	[RelayCommand(CanExecute = nameof(HasRows))]
	void ClearAll() {
		foreach (var cell in Rows.SelectMany(r => r.Cells)) cell.Su = 0;
	}

	/// <summary>登録(F6)。得意先×SKU の配分数を受注へ割り付け、既存配分を洗い替える</summary>
	[RelayCommand(CanExecute = nameof(HasRows), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (ShijiDay == null || NouhinDay == null) {
			MessageEx.ShowWarningDialog("指示日と納品日を入力してください。", owner: ActiveWindow);
			return;
		}
		var overStock = SkuColumns.Where(s => s.AfterSu < 0).ToList();
		if (overStock.Count > 0 && MessageEx.ShowQuestionDialog(
			$"有効在庫を超えるSKUが {overStock.Count:N0} 件あります。確定時に在庫不足でエラーになります。登録しますか？",
			owner: ActiveWindow) != MessageBoxResult.Yes) return;
		var overZan = Rows.SelectMany(r => r.Cells).Where(c => c.IsOver).Sum(c => c.Su - Math.Max(c.ZanSu, 0));
		if (overZan > 0 && MessageEx.ShowQuestionDialog(
			$"受注残を超える配分が {overZan:N0} 点あります。超過分は受注に紐付かない配分として登録されます。よろしいですか？",
			owner: ActiveWindow) != MessageBoxResult.Yes) return;

		var newRecords = BuildNewRecords();
		if (newRecords.Count == 0 && loadedEditableRows.Count == 0) {
			MessageEx.ShowWarningDialog("配分数を入力してください。", owner: ActiveWindow);
			return;
		}
		var confirm = newRecords.Count == 0
			? $"配分数が全て0のため、既存の配分 {loadedEditableRows.Count:N0} 件を削除します。よろしいですか？"
			: $"配分 {newRecords.Count:N0} 件（合計 {newRecords.Sum(x => x.Su):N0} 点）を登録します。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("配分データ登録中...");
		try {
			await CoreServiceClient.SaveHaibunAsync(loadedEditableRows, newRecords, "受注配分", ct);
			// 保存は完了している。再読込の失敗を「登録失敗」と誤表示しないよう分けて扱う
			try {
				await LoadMatrixAsync(ct);
			}
			catch (Exception reloadEx) {
				// 再読込に失敗した状態のまま再登録すると古い既存配分で洗い替えるため、一覧を破棄する
				ClearMatrix();
				Message = $"配分は登録済みですが、再読込に失敗しました: {reloadEx.Message}";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 配分を {newRecords.Count:N0} 件登録しました";
			MessageEx.ShowInformationDialog("登録完了しました。", owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "登録を中断しました";
		}
		catch (Exception ex) {
			Message = $"登録失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	// ===== 読込 =====

	async Task LoadMatrixAsync(CancellationToken ct) {
		var soko = (await QuerySqlListAsync<MasterTokui>($"SELECT * FROM {nameof(MasterTokui)} WHERE Code = @0", [SokoCode.Trim()], ct)).FirstOrDefault()
			?? throw new InvalidOperationException($"倉庫コード {SokoCode} が見つかりません。");
		var shohin = (await QuerySqlListAsync<MasterShohin>($"SELECT * FROM {nameof(MasterShohin)} WHERE Code = @0", [ShohinCode.Trim()], ct)).FirstOrDefault()
			?? throw new InvalidOperationException($"商品コード {ShohinCode} が見つかりません。");
		idSoko = soko.Id;
		idShohin = shohin.Id;
		SokoName = soko.Name;
		ShohinName = shohin.Name;

		var orders = await LoadOrdersAsync(ct);
		var orderIds = orders.Select(x => x.Id).ToList();
		var tokuiIds = orders.Select(x => x.Id_Tokui).Distinct().ToList();
		var shukka = await LoadShukkaAsync(orderIds, ct);
		var haibun = await LoadHaibunAsync(orderIds, tokuiIds, ct);
		// 修正できる配分は洗い替えの対象、それ以外（確定済み・送信済み）は受注残から差し引く
		loadedEditableRows = [.. haibun.Where(IsEditable)];
		var fixedHaibun = haibun.Where(h => !IsEditable(h) && h.RelateNo1 > 0)
			.GroupBy(h => (Juchu: (long)h.RelateNo1, Sku: new AllocSkuKey(h.Id_Col, h.Id_Siz)))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		// 受注×SKU の受注残と単価
		var lines = new Dictionary<(long Juchu, AllocSkuKey Sku), (int Su, Tran99Meisai Meisai)>();
		foreach (var order in orders) {
			foreach (var m in order.Jmeisai ?? []) {
				if (m.Id_Shohin != idShohin) continue;
				var key = (order.Id, new AllocSkuKey(m.Id_Col, m.Id_Siz));
				lines[key] = lines.TryGetValue(key, out var found)
					? (found.Su + m.Su * order.CalcFlag, found.Meisai)
					: (m.Su * order.CalcFlag, m);
			}
		}
		orderZans = [];
		var orderById = orders.ToDictionary(x => x.Id);
		foreach (var ((juchuId, sku), line) in lines) {
			var order = orderById[juchuId];
			var zan = line.Su - shukka.GetValueOrDefault((juchuId, sku)) - fixedHaibun.GetValueOrDefault((juchuId, sku));
			var listKey = (order.Id_Tokui, sku);
			if (!orderZans.TryGetValue(listKey, out var list)) orderZans[listKey] = list = [];
			list.Add(new HaibunOrderZan(juchuId, order.DenDay, zan, line.Meisai.Tanka, line.Meisai.Jodai, line.Meisai.Gedai));
		}
		var editingByCell = loadedEditableRows
			.GroupBy(h => (Tokui: h.Id_Tenpo, Sku: new AllocSkuKey(h.Id_Col, h.Id_Siz)))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		// SKU 列：受注明細か既存配分にある SKU を、商品の色サイズ並びで出す
		var usedSkus = lines.Keys.Select(k => k.Sku).Concat(editingByCell.Keys.Select(k => k.Sku)).ToHashSet();
		var colsiz = await QuerySqlListAsync<DerivedShohinColSiz>(
			$"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin = @0 ORDER BY RowIdx",
			[idShohin.ToString(CultureInfo.InvariantCulture)], ct);
		var stock = (await QuerySqlListAsync<SummaryRealStock>(
			$"SELECT * FROM {nameof(SummaryRealStock)} WHERE Id_Soko = @0 AND Id_Shohin = @1",
			[idSoko.ToString(CultureInfo.InvariantCulture), idShohin.ToString(CultureInfo.InvariantCulture)], ct))
			.GroupBy(x => new AllocSkuKey(x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => (Su: g.Sum(x => x.Su), Reserve: g.Sum(x => x.ReserveQty)));
		var editingBySku = loadedEditableRows.GroupBy(h => new AllocSkuKey(h.Id_Col, h.Id_Siz)).ToDictionary(g => g.Key, g => g.Sum(x => x.Su));
		var skuList = colsiz.Where(c => usedSkus.Contains(new AllocSkuKey(c.Id_Col, c.Id_Siz)))
			.Select(c => {
				var key = new AllocSkuKey(c.Id_Col, c.Id_Siz);
				var s = stock.GetValueOrDefault(key);
				// 有効在庫 = 実在庫 − 引当 ＋ 洗い替えで入れ直す自分の配分（受注配分は引当対象）
				return new SalesOrderAllocationSku(c, s.Su - s.Reserve + editingBySku.GetValueOrDefault(key));
			}).ToList();
		// 色サイズ派生に無いSKU（明細側の不整合）も落とさない
		foreach (var key in usedSkus.Where(k => skuList.All(s => s.Key != k))) {
			var s = stock.GetValueOrDefault(key);
			skuList.Add(new SalesOrderAllocationSku(new DerivedShohinColSiz { Id_Col = key.Id_Col, Id_Siz = key.Id_Siz, Code_Col = $"{key.Id_Col}", Code_Siz = $"{key.Id_Siz}" },
				s.Su - s.Reserve + editingBySku.GetValueOrDefault(key)));
		}

		// 行：得意先ごと。最も古い受注日 → 得意先コード の順（「在庫内で受注日順に読込」の優先順）
		shainByTokui = orders.GroupBy(o => o.Id_Tokui)
			.ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.DenDay, StringComparer.Ordinal).ThenByDescending(o => o.Id).First().Id_Shain);
		var rows = orders.GroupBy(o => o.Id_Tokui)
			.Select(g => (Tokui: g.Key, First: g.Min(o => o.DenDay) ?? string.Empty, Code: g.First().VTokui?.Cd ?? string.Empty,
				Display: FormatCodeName(g.First().VTokui), Count: g.Count()))
			.OrderBy(x => x.First, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal)
			.Select(x => {
				var row = new SalesOrderAllocationRow(x.Tokui, x.Display, x.Count);
				var cells = skuList.Select(sku => {
					var zans = orderZans.GetValueOrDefault((x.Tokui, sku.Key)) ?? [];
					return new SalesOrderAllocationCell(sku, row) {
						HasOrder = zans.Count > 0,
						ZanSu = zans.Sum(z => Math.Max(z.ZanSu, 0)),
						Changed = OnCellChanged,
					};
				}).ToList();
				row.SetCells(cells);
				foreach (var cell in cells) cell.Su = editingByCell.GetValueOrDefault((x.Tokui, cell.Sku.Key));
				return row;
			})
			// 受注残も既存配分も無い得意先は出さない
			.Where(r => r.ZanTotalSu > 0 || r.TotalSu > 0)
			.ToList();

		SkuColumns = [.. skuList];
		Rows = [.. rows];
		RefreshTotals();
		var limited = orders.Count >= MaxOrderCount ? $"（受注が {MaxOrderCount:N0} 件を超えたため打ち切りました。条件を絞ってください）" : string.Empty;
		Message = Rows.Count == 0
			? "受注残のある得意先がありません。"
			: $"得意先 {Rows.Count:N0} 件・受注 {orders.Count:N0} 件を読み込みました。{limited}";
		if (loadedEditableRows.Count > 0 && ShijiDay != null) {
			// 既存配分があれば、その指示日・納品日を初期値にする（伝票別画面と同じ）
			ShijiDay = FromYmd8(loadedEditableRows[0].DenDay) ?? ShijiDay;
			NouhinDay = FromYmd8(loadedEditableRows[0].NouhinDay) ?? NouhinDay;
		}
	}

	/// <summary>倉庫・商品・受注日・得意先で絞った未完了の受注（明細JSONごと）</summary>
	async Task<List<Tran12Jyuchu>> LoadOrdersAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [
			"H.CalcFlag <> 0", "H.EndFlag = 0",
			$"H.Kubun IN ({string.Join(",", TargetJuchuKubun)})",
			$"H.Id_Soko = {AddParameter(parameters, idSoko)}",
			$"EXISTS (SELECT 1 FROM json_each({SafeJmeisai("H")}) AS m WHERE {MeisaiNum("m", "Id_Shohin")} = {AddParameter(parameters, idShohin)})",
		];
		if (JuchuDayFrom is DateTime from) clauses.Add($"H.DenDay >= {AddParameter(parameters, from.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}");
		if (JuchuDayTo is DateTime to) clauses.Add($"H.DenDay <= {AddParameter(parameters, to.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}");
		List<string> tokuiClauses = [];
		if (!string.IsNullOrWhiteSpace(TokuiCodeFrom)) tokuiClauses.Add($"Code >= {AddParameter(parameters, TokuiCodeFrom.Trim())}");
		if (!string.IsNullOrWhiteSpace(TokuiCodeTo)) tokuiClauses.Add($"Code <= {AddParameter(parameters, TokuiCodeTo.Trim())}");
		if (tokuiClauses.Count > 0) clauses.Add($"H.Id_Tokui IN (SELECT Id FROM {nameof(MasterTokui)} WHERE {string.Join(" AND ", tokuiClauses)})");
		var sql = $"""
			SELECT H.*
			FROM {nameof(Tran12Jyuchu)} H
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY H.DenDay, H.Id
			LIMIT {MaxOrderCount}
			""";
		return await QuerySqlListAsync<Tran12Jyuchu>(sql, parameters, ct);
	}

	/// <summary>受注×SKU の出荷済数（卸先・売仕店向けの出荷売上。受注残の算式は伝票別画面と同じ）</summary>
	async Task<Dictionary<(long Juchu, AllocSkuKey Sku), int>> LoadShukkaAsync(IReadOnlyCollection<long> orderIds, CancellationToken ct) {
		if (orderIds.Count == 0) return [];
		List<string> parameters = [];
		var shohin = AddParameter(parameters, idShohin);
		var sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				U.RelateNo1 AS RelateNo1,
				{MeisaiNum("um", "Id_Col")} AS Id_Col,
				{MeisaiNum("um", "Id_Siz")} AS Id_Siz,
				SUM({MeisaiNum("um", "Su")} * U.CalcFlag) AS JitsuSu
			FROM Tran00Uriage U, json_each({SafeJmeisai("U")}) AS um
				INNER JOIN MasterTokui UT ON UT.Id = U.Id_Tokui
					AND UT.TenType IN ({TranCalcBase.ShukkaTenTypes})
			WHERE U.CalcFlag <> 0 AND U.RelateNo1 IN ({string.Join(",", orderIds)})
				AND {MeisaiNum("um", "Id_Shohin")} = {shohin}
			GROUP BY 4, 5, 6
			""";
		var rows = await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
		return rows.ToDictionary(x => ((long)x.RelateNo1, new AllocSkuKey(x.Id_Col, x.Id_Siz)), x => x.JitsuSu);
	}

	/// <summary>
	/// 対象受注×商品の未完了の受注配分と、同じ得意先×倉庫×商品で受注に紐付かない受注配分（受注残超過分）
	/// </summary>
	async Task<List<TranHaibun>> LoadHaibunAsync(IReadOnlyCollection<long> orderIds, IReadOnlyCollection<long> tokuiIds, CancellationToken ct) {
		if (orderIds.Count == 0) return [];
		List<string> parameters = [];
		var sql = $"""
			SELECT * FROM {nameof(TranHaibun)}
			WHERE Kubun = {AddParameter(parameters, KubunJuchu)} AND EndFlag = 0
				AND Id_Shohin = {AddParameter(parameters, idShohin)}
				AND (RelateNo1 IN ({string.Join(",", orderIds)})
					OR (RelateNo1 = 0 AND Id_Soko = {AddParameter(parameters, idSoko)} AND Id_Tenpo IN ({string.Join(",", tokuiIds)})))
			ORDER BY Id
			""";
		return await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
	}

	// ===== 登録データの組み立て =====

	List<TranHaibun> BuildNewRecords() {
		var records = new List<TranHaibun>();
		var denDay = ToYmd8(ShijiDay);
		var nouhinDay = ToYmd8(NouhinDay);
		foreach (var row in Rows) {
			foreach (var cell in row.Cells.Where(c => c.Su > 0)) {
				var zans = orderZans.GetValueOrDefault((row.Id_Tokui, cell.Sku.Key)) ?? [];
				if (zans.Count == 0) {
					// 対象受注が無いSKUに残っていた受注に紐付かない配分は、既存行の単価を引き継いで入れ直す
					var existing = loadedEditableRows.LastOrDefault(h => h.Id_Tenpo == row.Id_Tokui && h.Id_Col == cell.Sku.Key.Id_Col && h.Id_Siz == cell.Sku.Key.Id_Siz);
					if (existing != null) zans = [new HaibunOrderZan(0, string.Empty, 0, existing.Tanka, existing.Jodai, existing.Gedai)];
				}
				foreach (var share in HaibunOrderDistributor.Distribute(cell.Su, zans)) {
					records.Add(new TranHaibun {
						DenDay = denDay,
						NouhinDay = nouhinDay,
						Id_Soko = idSoko,
						Id_Tenpo = row.Id_Tokui,
						Kubun = KubunJuchu,
						Id_Shohin = idShohin,
						JanCode = cell.Sku.JanCode,
						Id_Col = cell.Sku.Key.Id_Col,
						Id_Siz = cell.Sku.Key.Id_Siz,
						Su = share.Su,
						Tanka = share.Tanka,
						Kingaku = share.Su * share.Tanka,
						Jodai = share.Jodai,
						Gedai = share.Gedai,
						RelateNo1 = (int)share.Id_Juchu,
						Memo = Memo,
						Id_Shain = shainByTokui.GetValueOrDefault(row.Id_Tokui),
					});
				}
			}
		}
		return records;
	}

	void OnCellChanged(SalesOrderAllocationCell cell, int delta) => GrandTotalSu += delta;

	void RefreshTotals() {
		GrandTotalSu = Rows.Sum(r => r.TotalSu);
		ZanTotalSu = Rows.Sum(r => r.ZanTotalSu);
	}

	/// <summary><see cref="TranHaibun.EditableWhereSql"/> と同じ条件（EndFlag=0 は読込SQLで絞り済み）</summary>
	static bool IsEditable(TranHaibun h) => h.SendFlg == 0 && string.IsNullOrEmpty(h.KakuteiDay) && h.EndFlag == 0;

	// ===== 通信・共通ヘルパー =====

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	TResult? ShowSelect<TResult>(Type tableType, string where, string order) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	void StartBusy(string message) {
		IsBusy = true;
		Message = message;
		ClientLib.Cursor2Wait();
		OnRowsChanged(Rows);
	}

	void FinishBusy() {
		IsBusy = false;
		ClientLib.Cursor2Normal();
		OnRowsChanged(Rows);
	}

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	/// <summary>不正JSONを空配列として扱う <c>Jmeisai</c> の SQL 式（json_validで検査し、不正値は空配列へ置換）。</summary>
	static string SafeJmeisai(string alias) =>
		$"CASE WHEN json_valid({alias}.Jmeisai) THEN {alias}.Jmeisai ELSE '[]' END";

	/// <summary>明細JSONの数値項目を取り出すSQL式。</summary>
	static string MeisaiNum(string alias, string property) =>
		$"CAST(IFNULL(json_extract({alias}.value, '$.{property}'), 0) AS INTEGER)";

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	static string ToYmd8(DateTime? value) => value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;

	static DateTime? FromYmd8(string? value) =>
		DateTime.TryParseExact(value, "yyyyMMdd", null, DateTimeStyles.None, out DateTime result) ? result : null;

	static string FormatCodeName(CodeNameView? value) =>
		value == null ? string.Empty : CodeNameDisplay.Format(value.Sid, value.Cd, value.Mei);
}

/// <summary>色×サイズのキー（商品は画面で1つに固定）</summary>
public readonly record struct AllocSkuKey(long Id_Col, long Id_Siz);

/// <summary>SKU 列。列見出しに有効在庫・受注残計・配分計・配分後在庫を出し、全得意先の入力に追従する</summary>
public sealed partial class SalesOrderAllocationSku(DerivedShohinColSiz colsiz, int yukoSu) : ObservableObject {
	public AllocSkuKey Key { get; } = new(colsiz.Id_Col, colsiz.Id_Siz);
	public string Code_Col { get; } = colsiz.Code_Col;
	public string Code_Siz { get; } = colsiz.Code_Siz;
	public string ColDisplay { get; } = $"{colsiz.Code_Col} {colsiz.Mei_Col}".Trim();
	public string SizDisplay { get; } = $"{colsiz.Code_Siz} {colsiz.Mei_Siz}".Trim();
	public string JanCode { get; } = colsiz.Jan1;

	/// <summary>有効在庫（実在庫 − 引当 ＋ 洗い替えで入れ直す自分の配分）</summary>
	public int YukoSu { get; } = yukoSu;

	/// <summary>全得意先の受注残合計</summary>
	[ObservableProperty]
	public partial int ZanTotalSu { get; set; }

	/// <summary>全得意先の配分合計</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(AfterSu))]
	[NotifyPropertyChangedFor(nameof(IsShort))]
	public partial int HaibunTotalSu { get; set; }

	/// <summary>配分後在庫 = 有効在庫 − 配分合計</summary>
	public int AfterSu => YukoSu - HaibunTotalSu;

	public bool IsShort => AfterSu < 0;
}

/// <summary>マトリクスの行（得意先1件）</summary>
public sealed partial class SalesOrderAllocationRow(long idTokui, string tokuiDisplay, int orderCount) : ObservableObject {
	public long Id_Tokui { get; } = idTokui;
	public string TokuiDisplay { get; } = tokuiDisplay;
	/// <summary>対象受注の件数</summary>
	public int OrderCount { get; } = orderCount;

	/// <summary>SKU 列と同じ並びのセル。動的列は <c>Cells[i].SuText</c> をバインドする</summary>
	public IReadOnlyList<SalesOrderAllocationCell> Cells { get; private set; } = [];

	/// <summary>この得意先の受注残合計</summary>
	public int ZanTotalSu => Cells.Sum(c => Math.Max(c.ZanSu, 0));

	/// <summary>この得意先の配分合計</summary>
	[ObservableProperty]
	public partial int TotalSu { get; set; }

	public void SetCells(IReadOnlyList<SalesOrderAllocationCell> cells) {
		Cells = cells;
		foreach (var cell in cells) cell.Sku.ZanTotalSu += Math.Max(cell.ZanSu, 0);
	}
}

/// <summary>マトリクスのセル（得意先 × SKU）</summary>
public sealed partial class SalesOrderAllocationCell(SalesOrderAllocationSku sku, SalesOrderAllocationRow owner) : ObservableObject {
	public SalesOrderAllocationSku Sku { get; } = sku;

	/// <summary>この得意先がこのSKUを受注しているか。受注の無いセルは入力しない</summary>
	public bool HasOrder { get; init; }

	/// <summary>この得意先×SKU の受注残（全受注の合計）</summary>
	public int ZanSu { get; init; }

	public Action<SalesOrderAllocationCell, int>? Changed { get; init; }

	/// <summary>配分数</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SuText))]
	[NotifyPropertyChangedFor(nameof(IsOver))]
	public partial int Su { get; set; }

	/// <summary>受注残を超えているか（超過分は受注に紐付かない配分になる）</summary>
	public bool IsOver => Su > Math.Max(ZanSu, 0);

	/// <summary>セル下段の受注残表示</summary>
	public string ZanText => HasOrder ? $"残 {ZanSu:N0}" : "受注なし";

	/// <summary>セル表示・編集用。0 は空白で見せ、受注の無いセルは入力を受け付けない</summary>
	public string SuText {
		get => Su == 0 ? string.Empty : Su.ToString("#,##0", CultureInfo.InvariantCulture);
		set {
			if (HasOrder) {
				string text = (value ?? string.Empty).Replace(",", string.Empty).Trim();
				Su = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? Math.Max(parsed, 0) : 0;
			}
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
