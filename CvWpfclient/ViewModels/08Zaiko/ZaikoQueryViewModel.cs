using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._08Zaiko;

public partial class ZaikoQueryViewModel : Helpers.BaseViewModel {
	readonly ZaikoQuerySearchTab searchTab;

	[ObservableProperty]
	public partial ObservableCollection<ZaikoQueryTabBase> Tabs { get; set; } = [];

	[ObservableProperty]
	public partial ZaikoQueryTabBase? SelectedTab { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<ZaikoQueryShohinRow> ProductRows { get; set; } = [];

	[ObservableProperty]
	public partial ZaikoQueryShohinRow? SelectedProduct { get; set; }

	[ObservableProperty]
	public partial int ProductCount { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ColCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ColCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ItemCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ItemCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string MaxCountText { get; set; } = "500";

	[ObservableProperty]
	public partial bool IncludeZeroStock { get; set; } = true;

	public ZaikoQueryViewModel() {
		searchTab = new ZaikoQuerySearchTab(this);
		Tabs.Add(searchTab);
		SelectedTab = searchTab;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Search(CancellationToken ct) {
		if (!TryGetMaxCount(out int maxCount)) return;

		StartBusy("検索中...");
		try {
			List<MasterShohin> shohinList = await LoadShohinListAsync(maxCount, ct);
			Dictionary<long, int> stockMap = await LoadProductStockTotalsAsync(shohinList.Select(x => x.Id), ct);
			Dictionary<long, int> transitMap = await LoadProductTransitTotalsAsync(shohinList.Select(x => x.Id), ct);

			ObservableCollection<ZaikoQueryShohinRow> rows = [];
			foreach (MasterShohin shohin in shohinList) {
				rows.Add(new ZaikoQueryShohinRow(shohin) {
					StockSu = stockMap.GetValueOrDefault(shohin.Id),
					TransitQty = transitMap.GetValueOrDefault(shohin.Id)
				});
			}

			ProductRows = rows;
			ProductCount = ProductRows.Count;
			SelectedProduct = ProductRows.FirstOrDefault();
			SelectedTab = searchTab;
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 在庫問合せ対象を {ProductCount:N0} 件取得しました";
		}
		catch (OperationCanceledException ex) {
			Message = $"検索を中断しました: {ex.Message}";
		}
		catch (Exception ex) {
			Message = $"検索失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void ClearConditions() {
		ShohinCodeFrom = string.Empty;
		ShohinCodeTo = string.Empty;
		ColCodeFrom = string.Empty;
		ColCodeTo = string.Empty;
		ShohinName = string.Empty;
		SokoCodeFrom = string.Empty;
		SokoCodeTo = string.Empty;
		BrandCodeFrom = string.Empty;
		BrandCodeTo = string.Empty;
		ItemCodeFrom = string.Empty;
		ItemCodeTo = string.Empty;
		MaxCountText = "500";
		IncludeZeroStock = true;
		Message = "検索条件をクリアしました";
	}

	[RelayCommand]
	async Task OpenStockTab(ZaikoQueryShohinRow? row) {
		row ??= SelectedProduct;
		if (row == null) {
			MessageEx.ShowWarningDialog("対象商品を選択してください", owner: ActiveWindow);
			return;
		}

		ZaikoQueryStockTab? existing = Tabs.OfType<ZaikoQueryStockTab>().FirstOrDefault(x => x.ShohinId == row.Id);
		if (existing != null) {
			SelectedTab = existing;
			return;
		}

		StartBusy("在庫明細取得中...");
		try {
			ZaikoQueryStockTab tab = await CreateStockTabAsync(row, CancellationToken.None);
			Tabs.Add(tab);
			SelectedTab = tab;
			Message = $"{row.Code} {row.Name} の在庫データを表示しました";
		}
		catch (Exception ex) {
			Message = $"在庫データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void CloseStockTab(ZaikoQueryStockTab? tab) {
		if (tab == null) return;
		if (!Tabs.Contains(tab)) return;

		int index = Tabs.IndexOf(tab);
		Tabs.Remove(tab);
		SelectedTab = Tabs.ElementAtOrDefault(Math.Max(0, index - 1)) ?? searchTab;
	}

	[RelayCommand]
	void SelectShohinCodeFrom() => SelectShohinCode(x => ShohinCodeFrom = x);

	[RelayCommand]
	void SelectShohinCodeTo() => SelectShohinCode(x => ShohinCodeTo = x);

	[RelayCommand]
	void SelectColCodeFrom() => SelectCode<MasterMeisho>($"Kubun='{MasterMeisho.KubunColor}'", "Code", x => ColCodeFrom = x);

	[RelayCommand]
	void SelectColCodeTo() => SelectCode<MasterMeisho>($"Kubun='{MasterMeisho.KubunColor}'", "Code", x => ColCodeTo = x);

	[RelayCommand]
	void SelectSokoCodeFrom() => SelectCode<MasterTokui>("TenType=0", "Code", x => SokoCodeFrom = x);

	[RelayCommand]
	void SelectSokoCodeTo() => SelectCode<MasterTokui>("TenType=0", "Code", x => SokoCodeTo = x);

	[RelayCommand]
	void SelectBrandCodeFrom() => SelectCode<MasterMeisho>($"Kubun='{MasterMeisho.KubunBrand}'", "Code", x => BrandCodeFrom = x);

	[RelayCommand]
	void SelectBrandCodeTo() => SelectCode<MasterMeisho>($"Kubun='{MasterMeisho.KubunBrand}'", "Code", x => BrandCodeTo = x);

	[RelayCommand]
	void SelectItemCodeFrom() => SelectCode<MasterMeisho>($"Kubun='{MasterMeisho.KubunItem}'", "Code", x => ItemCodeFrom = x);

	[RelayCommand]
	void SelectItemCodeTo() => SelectCode<MasterMeisho>($"Kubun='{MasterMeisho.KubunItem}'", "Code", x => ItemCodeTo = x);

	async Task<List<MasterShohin>> LoadShohinListAsync(int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildShohinClauses(parameters);
		string where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		// 上代は上代一括変更(DerivedJodai)の適用価格で解決する。倉庫はコード範囲(SokoCodeFrom/To)で
		// 絞るため単一の倉庫Idが取れないので、本部売上系の全件行(Id_Tenpo=0)を基準にする。
		// 評価金額を出す画面なので、在庫評価SQL(StockSql.TankaJodai)と軸が食い違わないよう本部基準で統一する。
		string jodai = DerivedJodai.FinalJodaiSql(
			"M.Id",
			((int)EnumJodaiTaisho.Honbu).ToString(CultureInfo.InvariantCulture),
			"0",
			DerivedJodai.TodaySql,
			"M");
		string sql = $"""
			SELECT
				M.Id, M.Vdc, M.Vdu, M.Code, M.Name, M.Ryaku, M.Kana,
				{jodai} AS TankaJodai,
				M.VTenji, M.VBrand, M.VMaterial, M.VItem, M.VSeason
			FROM MasterShohin M
			{where}
			ORDER BY M.Code
			LIMIT {maxCount}
			""";

		return await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
	}

	async Task<Dictionary<long, int>> LoadProductStockTotalsAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		List<long> ids = shohinIds.Distinct().ToList();
		if (ids.Count == 0) return [];

		List<string> parameters = [];
		List<string> clauses = BuildStockClauses("R", "D", "Soko", parameters, ids);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Shohin,
				0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(R.Su), 0) AS Su
			FROM SummaryRealStock R
				LEFT JOIN DerivedShohinColSiz D
					ON D.Id_Shohin = R.Id_Shohin
					AND D.Id_Col = R.Id_Col
					AND D.Id_Siz = R.Id_Siz
				LEFT JOIN MasterTokui Soko ON Soko.Id = R.Id_Soko
			WHERE {string.Join(" AND ", clauses)}
			GROUP BY R.Id_Shohin
			""";

		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	async Task<Dictionary<long, int>> LoadProductTransitTotalsAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		List<long> ids = shohinIds.Distinct().ToList();
		if (ids.Count == 0) return [];

		List<string> parameters = [];
		List<string> clauses = BuildStockClauses("T", "D", "Soko", parameters, ids);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				0 AS Id_Soko, T.Id_Shohin, 0 AS Id_Col, 0 AS Id_Siz,
				0 AS Su,
				IFNULL(SUM(T.TransitQty), 0) AS TransitQty
			FROM SummaryStock T
				LEFT JOIN DerivedShohinColSiz D
					ON D.Id_Shohin = T.Id_Shohin
					AND D.Id_Col = T.Id_Col
					AND D.Id_Siz = T.Id_Siz
				LEFT JOIN MasterTokui Soko ON Soko.Id = T.Id_Soko
			WHERE {string.Join(" AND ", clauses)}
			GROUP BY T.Id_Shohin
			""";

		List<SummaryStock> rows = await QuerySqlListAsync<SummaryStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.TransitQty);
	}

	async Task<ZaikoQueryStockTab> CreateStockTabAsync(ZaikoQueryShohinRow row, CancellationToken ct) {
		List<DerivedShohinColSiz> skuList = await LoadShohinSkuListAsync(row.Id, ct);
		List<SummaryRealStock> stockRows = await LoadStockRowsAsync(row.Id, ct);
		Dictionary<long, MasterTokui> sokoMap = await LoadSokoMapAsync(stockRows.Select(x => x.Id_Soko), ct);

		DataTable table = BuildStockTable(skuList, stockRows, sokoMap);
		return new ZaikoQueryStockTab(row, table) {
			Message = stockRows.Count == 0
				? "在庫データがありません"
				: $"{stockRows.Count:N0} SKU在庫を表示"
		};
	}

	async Task<List<DerivedShohinColSiz>> LoadShohinSkuListAsync(long shohinId, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [$"D.Id_Shohin = {AddParameter(parameters, shohinId)}"];
		AddCodeRange(clauses, parameters, "D.Code_Col", ColCodeFrom, ColCodeTo);

		string sql = $"""
			SELECT
				D.Id, D.Id_Shohin, D.RowIdx, D.Code,
				D.Id_Col, D.Code_Col, D.Mei_Col,
				D.Id_Siz, D.Code_Siz, D.Mei_Siz,
				D.Jan1, D.Jan2, D.Jan3
			FROM DerivedShohinColSiz D
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY D.Code_Col, D.Code_Siz, D.RowIdx
			""";

		return await QuerySqlListAsync<DerivedShohinColSiz>(sql, parameters, ct);
	}

	async Task<List<SummaryRealStock>> LoadStockRowsAsync(long shohinId, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildStockClauses("R", "D", "Soko", parameters, [shohinId]);

		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Soko, R.Id_Shohin, R.Id_Col, R.Id_Siz,
				R.Su
			FROM SummaryRealStock R
				LEFT JOIN DerivedShohinColSiz D
					ON D.Id_Shohin = R.Id_Shohin
					AND D.Id_Col = R.Id_Col
					AND D.Id_Siz = R.Id_Siz
				LEFT JOIN MasterTokui Soko ON Soko.Id = R.Id_Soko
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY Soko.Code, D.Code_Col, D.Code_Siz
			""";

		return await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
	}

	async Task<Dictionary<long, MasterTokui>> LoadSokoMapAsync(IEnumerable<long> sokoIds, CancellationToken ct) {
		List<long> ids = sokoIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) return [];

		List<string> parameters = [];
		string inClause = BuildInClause("S.Id", ids, parameters);
		string sql = $"""
			SELECT
				S.Id, S.Vdc, S.Vdu, S.Code, S.Name, S.Ryaku, S.Kana, S.TenType
			FROM MasterTokui S
			WHERE {inClause}
			ORDER BY S.Code
			""";

		List<MasterTokui> rows = await QuerySqlListAsync<MasterTokui>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id);
	}

	DataTable BuildStockTable(
		IEnumerable<DerivedShohinColSiz> skuList,
		IEnumerable<SummaryRealStock> stockRows,
		IReadOnlyDictionary<long, MasterTokui> sokoMap) {
		DataTable table = new();
		table.Columns.Add("倉庫", typeof(string));
		table.Columns.Add("倉庫毎Total", typeof(int));

		Dictionary<ZaikoSkuKey, string> columnMap = [];
		foreach (DerivedShohinColSiz sku in skuList) {
			AddSkuColumn(table, columnMap, new ZaikoSkuKey(sku.Id_Col, sku.Id_Siz), FormatSkuHeader(sku));
		}

		foreach (SummaryRealStock stock in stockRows) {
			ZaikoSkuKey key = new(stock.Id_Col, stock.Id_Siz);
			if (!columnMap.ContainsKey(key)) {
				AddSkuColumn(table, columnMap, key, $"色Id:{stock.Id_Col}\nサイズId:{stock.Id_Siz}");
			}
		}

		foreach (IGrouping<long, SummaryRealStock> sokoGroup in stockRows.GroupBy(x => x.Id_Soko).OrderBy(x => GetSokoCode(x.Key, sokoMap))) {
			DataRow row = table.NewRow();
			row["倉庫"] = FormatSoko(x: sokoGroup.Key, sokoMap);
			row["倉庫毎Total"] = sokoGroup.Sum(x => x.Su);

			foreach (IGrouping<ZaikoSkuKey, SummaryRealStock> skuGroup in sokoGroup.GroupBy(x => new ZaikoSkuKey(x.Id_Col, x.Id_Siz))) {
				if (columnMap.TryGetValue(skuGroup.Key, out string? columnName)) {
					row[columnName] = skuGroup.Sum(x => x.Su);
				}
			}

			table.Rows.Add(row);
		}

		return table;
	}

	List<string> BuildShohinClauses(List<string> parameters) {
		List<string> clauses = [];
		AddCodeRange(clauses, parameters, "M.Code", ShohinCodeFrom, ShohinCodeTo);
		AddLike(clauses, parameters, "M.Name", ShohinName);
		AddCodeRange(clauses, parameters, JsonCd("M.VBrand"), BrandCodeFrom, BrandCodeTo);
		AddCodeRange(clauses, parameters, JsonCd("M.VItem"), ItemCodeFrom, ItemCodeTo);

		if (!IncludeZeroStock) {
			// 在庫0除外は商品単位の SummaryStock 集計で判定する
			List<string> stockClauses = ["T.Id_Shohin = M.Id"];
			AddCodeRange(stockClauses, parameters, "Soko.Code", SokoCodeFrom, SokoCodeTo);
			AddCodeRange(stockClauses, parameters, "D.Code_Col", ColCodeFrom, ColCodeTo);

			clauses.Add($"""
				EXISTS (
					SELECT 1
					FROM SummaryStock T
						LEFT JOIN DerivedShohinColSiz D
							ON D.Id_Shohin = T.Id_Shohin
							AND D.Id_Col = T.Id_Col
							AND D.Id_Siz = T.Id_Siz
						LEFT JOIN MasterTokui Soko ON Soko.Id = T.Id_Soko
					WHERE {string.Join(" AND ", stockClauses)}
					GROUP BY T.Id_Shohin
					HAVING IFNULL(SUM(T.Su), 0) <> 0
				)
				""");
		}

		List<string> colClauses = [];
		AddCodeRange(colClauses, parameters, "D.Code_Col", ColCodeFrom, ColCodeTo);
		if (colClauses.Count > 0) {
			clauses.Add($"""
				EXISTS (
					SELECT 1
					FROM DerivedShohinColSiz D
					WHERE D.Id_Shohin = M.Id
						AND {string.Join(" AND ", colClauses)}
				)
				""");
		}

		return clauses;
	}

	List<string> BuildStockClauses(string stockAlias, string skuAlias, string sokoAlias, List<string> parameters, IReadOnlyCollection<long> shohinIds) {
		List<string> clauses = [BuildInClause($"{stockAlias}.Id_Shohin", shohinIds, parameters)];
		AddCodeRange(clauses, parameters, $"{skuAlias}.Code_Col", ColCodeFrom, ColCodeTo);
		AddCodeRange(clauses, parameters, $"{sokoAlias}.Code", SokoCodeFrom, SokoCodeTo);
		return clauses;
	}

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	void SelectCode<T>(string where, string order, Action<string> setCode)
		where T : BaseDbClass, IBaseCodeName {
		var view = new Views.Sub.SelectWinView();
		if (view.DataContext is not SelectWinViewModel vm) return;

		vm.SetParam(typeof(T), where, order);
		if (ClientLib.ShowDialogView(view, this) != true) return;
		if (vm.Current is not T selected) return;

		setCode(selected.Code ?? string.Empty);
	}

	void SelectShohinCode(Action<string> setCode) {
		var view = new Views.Sub.SelectShohinView();
		if (view.DataContext is not SelectShohinViewModel vm) return;

		vm.ShohinCodeFrom = ShohinCodeFrom;
		vm.ShohinCodeTo = ShohinCodeTo;
		vm.ShohinName = ShohinName;

		if (ClientLib.ShowDialogView(view, this) != true) return;
		MasterShohin? selected = vm.SelectedShohin;
		if (selected == null) return;

		setCode(selected.Code ?? string.Empty);
	}

	bool TryGetMaxCount(out int maxCount) {
		string text = Normalize(MaxCountText);
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxCount)) {
			MessageEx.ShowWarningDialog("最大件数は数値で入力してください", owner: ActiveWindow);
			return false;
		}

		if (maxCount <= 0) {
			MessageEx.ShowWarningDialog("最大件数は1以上で入力してください", owner: ActiveWindow);
			return false;
		}

		maxCount = Math.Min(maxCount, 10000);
		MaxCountText = maxCount.ToString(CultureInfo.InvariantCulture);
		return true;
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

	static void AddCodeRange(List<string> clauses, List<string> parameters, string column, string? from, string? to) {
		string normalizedFrom = Normalize(from);
		string normalizedTo = Normalize(to);

		if (!string.IsNullOrEmpty(normalizedFrom)) {
			clauses.Add($"{column} >= {AddParameter(parameters, normalizedFrom)}");
		}

		if (!string.IsNullOrEmpty(normalizedTo)) {
			clauses.Add($"{column} <= {AddParameter(parameters, normalizedTo)}");
		}
	}

	static void AddLike(List<string> clauses, List<string> parameters, string column, string? value) {
		string normalized = Normalize(value);
		if (string.IsNullOrEmpty(normalized)) return;

		clauses.Add($"{column} LIKE {AddParameter(parameters, $"%{normalized}%")}");
	}

	static string BuildInClause(string column, IEnumerable<long> values, List<string> parameters) {
		string[] parameterNames = values
			.Select(x => AddParameter(parameters, x))
			.ToArray();

		return $"{column} IN ({string.Join(",", parameterNames)})";
	}

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	static string Normalize(string? value) => value?.Trim() ?? string.Empty;

	static string JsonCd(string column) =>
		$"IFNULL(json_extract(CASE WHEN json_valid({column}) THEN {column} ELSE '{{}}' END, '$.Cd'), '')";

	static void AddSkuColumn(DataTable table, Dictionary<ZaikoSkuKey, string> columnMap, ZaikoSkuKey key, string baseName) {
		string columnName = baseName;
		int suffix = 2;
		while (table.Columns.Contains(columnName)) {
			columnName = $"{baseName}({suffix})";
			suffix++;
		}

		table.Columns.Add(columnName, typeof(int));
		columnMap[key] = columnName;
	}

	static string FormatSkuHeader(DerivedShohinColSiz sku) {
		string col = JoinCodeName(sku.Code_Col, sku.Mei_Col);
		string siz = JoinCodeName(sku.Code_Siz, sku.Mei_Siz);
		return $"{col}\n{siz}";
	}

	static string FormatSoko(long x, IReadOnlyDictionary<long, MasterTokui> sokoMap) =>
		sokoMap.TryGetValue(x, out MasterTokui? soko)
			? CodeNameDisplay.Format(soko.Id, soko.Code, soko.Name)
			: $"倉庫Id:{x}";

	static string GetSokoCode(long id, IReadOnlyDictionary<long, MasterTokui> sokoMap) =>
		sokoMap.TryGetValue(id, out MasterTokui? soko) ? soko.Code : id.ToString(CultureInfo.InvariantCulture);

	static string JoinCodeName(string? code, string? name) {
		string cd = Normalize(code);
		string mei = Normalize(name);
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}
}

public abstract class ZaikoQueryTabBase(string header) : ObservableObject {
	public string Header { get; } = header;
}

public sealed class ZaikoQuerySearchTab(ZaikoQueryViewModel owner) : ZaikoQueryTabBase("検索条件") {
	public ZaikoQueryViewModel Owner { get; } = owner;
}

public sealed partial class ZaikoQueryStockTab : ZaikoQueryTabBase {
	public long ShohinId { get; }
	public string ProductCode { get; }
	public string ProductName { get; }

	[ObservableProperty]
	public partial DataTable StockTable { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	public ZaikoQueryStockTab(ZaikoQueryShohinRow product, DataTable stockTable)
		: base($"{product.Code} {product.Name}") {
		ShohinId = product.Id;
		ProductCode = product.Code;
		ProductName = product.Name;
		StockTable = stockTable;
	}
}

public sealed partial class ZaikoQueryShohinRow(MasterShohin shohin) : ObservableObject {
	public MasterShohin Shohin { get; } = shohin;
	public long Id => Shohin.Id;
	public string Code => Shohin.Code;
	public string Name => Shohin.Name;
	public int TankaJodai => Shohin.TankaJodai;
	public string TenjiDisplay => FormatCodeName(Shohin.VTenji);
	public string BrandDisplay => FormatCodeName(Shohin.VBrand);
	public string MaterialDisplay => FormatCodeName(Shohin.VMaterial);
	public string ItemDisplay => FormatCodeName(Shohin.VItem);
	public string SeasonDisplay => FormatCodeName(Shohin.VSeason);

	[ObservableProperty]
	public partial int StockSu { get; set; }

	[ObservableProperty]
	public partial int TransitQty { get; set; }

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	static string FormatCodeName(CodeNameView? value) =>
		value == null ? string.Empty : CodeNameDisplay.Format(value.Sid, value.Cd, value.Mei);
}

readonly record struct ZaikoSkuKey(long IdCol, long IdSiz);
