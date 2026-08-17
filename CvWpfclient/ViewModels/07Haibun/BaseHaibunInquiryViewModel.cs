using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Microsoft.Win32;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分照会（配分問合わせ / 引当問合わせ / 有効在庫問合わせ）の共通基底。
/// <para>
/// 旧CV.net【配分】9〜11に相当する read-only 照会画面。検索条件で商品を絞り、
/// 商品CDのダブルクリックで倉庫×色サイズのマトリクスタブを開き、CSVへ出力できる。
/// 構造は <see cref="ViewModels._08Zaiko.ZaikoQueryViewModel"/>（在庫問合せ）と同一で、
/// ドリルダウンで展開する数量だけがサブクラスで異なる。
/// </para>
/// <para>
/// 引当数・有効在庫は集計テーブル(<see cref="SummaryRealStock"/>)へ materialize 済みのため
/// 再集計せず列を読む。配分数は集計列が無いため <see cref="TranHaibun"/> を直接集計する。
/// 詳細は `Doc/spec/2026-08-18_I9_配分照会3画面_詳細設計.md` を参照する。
/// </para>
/// </summary>
public abstract partial class BaseHaibunInquiryViewModel : Helpers.BaseViewModel {
	readonly HaibunInquirySearchTab searchTab;

	/// <summary>ドリルダウンで展開する数量の名称（"配分数" / "引当数" / "有効在庫"）。CSVファイル名にも使う。</summary>
	protected abstract string DrillLabel { get; }

	/// <summary>
	/// screen固有のドリルダウン数量を倉庫×SKU単位で取得する。数量は <see cref="SummaryRealStock.Su"/> へ射影して返す。
	/// </summary>
	protected abstract Task<List<SummaryRealStock>> LoadDrillRowsAsync(long shohinId, CancellationToken ct);

	[ObservableProperty]
	public partial ObservableCollection<HaibunInquiryTabBase> Tabs { get; set; } = [];

	[ObservableProperty]
	public partial HaibunInquiryTabBase? SelectedTab { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<HaibunInquiryRow> ProductRows { get; set; } = [];

	[ObservableProperty]
	public partial HaibunInquiryRow? SelectedProduct { get; set; }

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

	protected BaseHaibunInquiryViewModel() {
		searchTab = new HaibunInquirySearchTab(this);
		Tabs.Add(searchTab);
		SelectedTab = searchTab;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Search(CancellationToken ct) {
		if (!TryGetMaxCount(out int maxCount)) return;

		StartBusy("検索中...");
		try {
			List<MasterShohin> shohinList = await LoadShohinListAsync(maxCount, ct);
			Dictionary<long, (int Stock, int Reserve)> realMap = await LoadProductRealTotalsAsync(shohinList.Select(x => x.Id), ct);
			Dictionary<long, int> haibunMap = await LoadProductHaibunTotalsAsync(shohinList.Select(x => x.Id), ct);

			ObservableCollection<HaibunInquiryRow> rows = [];
			foreach (MasterShohin shohin in shohinList) {
				(int stock, int reserve) = realMap.GetValueOrDefault(shohin.Id);
				rows.Add(new HaibunInquiryRow(shohin) {
					StockSu = stock,
					ReserveSu = reserve,
					HaibunSu = haibunMap.GetValueOrDefault(shohin.Id)
				});
			}

			ProductRows = rows;
			ProductCount = ProductRows.Count;
			SelectedProduct = ProductRows.FirstOrDefault();
			SelectedTab = searchTab;
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 照会対象を {ProductCount:N0} 件取得しました";
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
	async Task OpenStockTab(HaibunInquiryRow? row) {
		row ??= SelectedProduct;
		if (row == null) {
			MessageEx.ShowWarningDialog("対象商品を選択してください", owner: ActiveWindow);
			return;
		}

		HaibunInquiryStockTab? existing = Tabs.OfType<HaibunInquiryStockTab>().FirstOrDefault(x => x.ShohinId == row.Id);
		if (existing != null) {
			SelectedTab = existing;
			return;
		}

		StartBusy($"{DrillLabel}明細取得中...");
		try {
			HaibunInquiryStockTab tab = await CreateStockTabAsync(row, CancellationToken.None);
			Tabs.Add(tab);
			SelectedTab = tab;
			Message = $"{row.Code} {row.Name} の{DrillLabel}を表示しました";
		}
		catch (Exception ex) {
			Message = $"{DrillLabel}取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void CloseStockTab(HaibunInquiryStockTab? tab) {
		if (tab == null) return;
		if (!Tabs.Contains(tab)) return;

		int index = Tabs.IndexOf(tab);
		Tabs.Remove(tab);
		SelectedTab = Tabs.ElementAtOrDefault(Math.Max(0, index - 1)) ?? searchTab;
	}

	/// <summary>表示中のマトリクスをCSVへ書き出す。ファイル名は「商品CD_数量名.csv」。</summary>
	[RelayCommand]
	void ExportCsv(HaibunInquiryStockTab? tab) {
		tab ??= SelectedTab as HaibunInquiryStockTab;
		if (tab == null || tab.StockTable.Rows.Count == 0) {
			MessageEx.ShowWarningDialog("出力する明細がありません", owner: ActiveWindow);
			return;
		}

		var dialog = new SaveFileDialog {
			Title = $"{DrillLabel}をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"{SanitizeFileName(tab.ProductCode)}_{DrillLabel}.csv"
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;

		try {
			File.WriteAllText(dialog.FileName, BuildCsv(tab.StockTable), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Message = $"CSVを出力しました: {dialog.FileName}";
		}
		catch (Exception ex) {
			Message = $"CSV出力失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	[RelayCommand]
	void SelectShohinCodeFrom() => SelectShohinCode(x => ShohinCodeFrom = x);

	[RelayCommand]
	void SelectShohinCodeTo() => SelectShohinCode(x => ShohinCodeTo = x);

	[RelayCommand]
	void SelectColCodeFrom() => SelectCode<MasterMeisho>("Kubun='COL'", "Code", x => ColCodeFrom = x);

	[RelayCommand]
	void SelectColCodeTo() => SelectCode<MasterMeisho>("Kubun='COL'", "Code", x => ColCodeTo = x);

	[RelayCommand]
	void SelectSokoCodeFrom() => SelectCode<MasterTokui>("TenType=0", "Code", x => SokoCodeFrom = x);

	[RelayCommand]
	void SelectSokoCodeTo() => SelectCode<MasterTokui>("TenType=0", "Code", x => SokoCodeTo = x);

	[RelayCommand]
	void SelectBrandCodeFrom() => SelectCode<MasterMeisho>("Kubun='BRD'", "Code", x => BrandCodeFrom = x);

	[RelayCommand]
	void SelectBrandCodeTo() => SelectCode<MasterMeisho>("Kubun='BRD'", "Code", x => BrandCodeTo = x);

	[RelayCommand]
	void SelectItemCodeFrom() => SelectCode<MasterMeisho>("Kubun='ITM'", "Code", x => ItemCodeFrom = x);

	[RelayCommand]
	void SelectItemCodeTo() => SelectCode<MasterMeisho>("Kubun='ITM'", "Code", x => ItemCodeTo = x);

	async Task<List<MasterShohin>> LoadShohinListAsync(int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildShohinClauses(parameters);
		string where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		// 上代は上代一括変更(DerivedJodai)の適用価格で解決する（在庫問合せと同じ本部基準）
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

	/// <summary>商品単位の 在庫(SUM Su)・引当(SUM ReserveQty) を SummaryRealStock から取得する。</summary>
	async Task<Dictionary<long, (int Stock, int Reserve)>> LoadProductRealTotalsAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		List<long> ids = shohinIds.Distinct().ToList();
		if (ids.Count == 0) return [];

		List<string> parameters = [];
		List<string> clauses = BuildStockClauses("R", "D", "Soko", parameters, ids);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Shohin,
				0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(R.Su), 0) AS Su,
				IFNULL(SUM(R.ReserveQty), 0) AS ReserveQty
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
		return rows.ToDictionary(x => x.Id_Shohin, x => (x.Su, x.ReserveQty));
	}

	/// <summary>商品単位の 配分数(SUM TranHaibun.Su, EndFlag=0) を取得する。</summary>
	async Task<Dictionary<long, int>> LoadProductHaibunTotalsAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		List<long> ids = shohinIds.Distinct().ToList();
		if (ids.Count == 0) return [];

		List<string> parameters = [];
		List<string> clauses = [BuildInClause("h.Id_Shohin", ids, parameters), "h.EndFlag = 0"];
		AddCodeRange(clauses, parameters, "D.Code_Col", ColCodeFrom, ColCodeTo);
		AddCodeRange(clauses, parameters, "Soko.Code", SokoCodeFrom, SokoCodeTo);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				h.Id_Shohin,
				0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(h.Su), 0) AS Su
			FROM TranHaibun h
				LEFT JOIN DerivedShohinColSiz D
					ON D.Id_Shohin = h.Id_Shohin
					AND D.Id_Col = h.Id_Col
					AND D.Id_Siz = h.Id_Siz
				LEFT JOIN MasterTokui Soko ON Soko.Id = h.Id_Soko
			WHERE {string.Join(" AND ", clauses)}
			GROUP BY h.Id_Shohin
			""";

		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	async Task<HaibunInquiryStockTab> CreateStockTabAsync(HaibunInquiryRow row, CancellationToken ct) {
		List<DerivedShohinColSiz> skuList = await LoadShohinSkuListAsync(row.Id, ct);
		List<SummaryRealStock> qtyRows = await LoadDrillRowsAsync(row.Id, ct);
		Dictionary<long, MasterTokui> sokoMap = await LoadSokoMapAsync(qtyRows.Select(x => x.Id_Soko), ct);

		DataTable table = BuildDrillTable(skuList, qtyRows, sokoMap);
		return new HaibunInquiryStockTab(row, table) {
			Message = qtyRows.Count == 0
				? $"{DrillLabel}データがありません"
				: $"{qtyRows.Count:N0} SKUの{DrillLabel}を表示"
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

	/// <summary>倉庫コード範囲・色コード範囲を SummaryRealStock 系のドリルSQLへ付ける共通WHERE句。</summary>
	protected List<string> BuildStockClauses(string stockAlias, string skuAlias, string sokoAlias, List<string> parameters, IReadOnlyCollection<long> shohinIds) {
		List<string> clauses = [BuildInClause($"{stockAlias}.Id_Shohin", shohinIds, parameters)];
		AddCodeRange(clauses, parameters, $"{skuAlias}.Code_Col", ColCodeFrom, ColCodeTo);
		AddCodeRange(clauses, parameters, $"{sokoAlias}.Code", SokoCodeFrom, SokoCodeTo);
		return clauses;
	}

	/// <summary>倉庫コード範囲・色コード範囲を TranHaibun のドリルSQLへ付ける共通WHERE句。</summary>
	protected List<string> BuildHaibunClauses(string haibunAlias, string skuAlias, string sokoAlias, List<string> parameters, long shohinId) {
		List<string> clauses = [$"{haibunAlias}.Id_Shohin = {AddParameter(parameters, shohinId)}", $"{haibunAlias}.EndFlag = 0"];
		AddCodeRange(clauses, parameters, $"{skuAlias}.Code_Col", ColCodeFrom, ColCodeTo);
		AddCodeRange(clauses, parameters, $"{sokoAlias}.Code", SokoCodeFrom, SokoCodeTo);
		return clauses;
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

	DataTable BuildDrillTable(
		IEnumerable<DerivedShohinColSiz> skuList,
		IEnumerable<SummaryRealStock> qtyRows,
		IReadOnlyDictionary<long, MasterTokui> sokoMap) {
		DataTable table = new();
		table.Columns.Add("倉庫", typeof(string));
		table.Columns.Add("倉庫毎Total", typeof(int));

		Dictionary<HaibunSkuKey, string> columnMap = [];
		foreach (DerivedShohinColSiz sku in skuList) {
			AddSkuColumn(table, columnMap, new HaibunSkuKey(sku.Id_Col, sku.Id_Siz), FormatSkuHeader(sku));
		}

		foreach (SummaryRealStock qty in qtyRows) {
			HaibunSkuKey key = new(qty.Id_Col, qty.Id_Siz);
			if (!columnMap.ContainsKey(key)) {
				AddSkuColumn(table, columnMap, key, $"色Id:{qty.Id_Col}\nサイズId:{qty.Id_Siz}");
			}
		}

		foreach (IGrouping<long, SummaryRealStock> sokoGroup in qtyRows.GroupBy(x => x.Id_Soko).OrderBy(x => GetSokoCode(x.Key, sokoMap))) {
			DataRow row = table.NewRow();
			row["倉庫"] = FormatSoko(sokoGroup.Key, sokoMap);
			row["倉庫毎Total"] = sokoGroup.Sum(x => x.Su);

			foreach (IGrouping<HaibunSkuKey, SummaryRealStock> skuGroup in sokoGroup.GroupBy(x => new HaibunSkuKey(x.Id_Col, x.Id_Siz))) {
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

	protected async Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters]))
		};

		CvMsg reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		ct.ThrowIfCancellationRequested();
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}

		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) return [];
		return list.Cast<T>().ToList();
	}

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

	static string BuildCsv(DataTable table) {
		var sb = new StringBuilder();
		sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => CsvField(c.ColumnName))));
		foreach (DataRow row in table.Rows) {
			sb.AppendLine(string.Join(",", row.ItemArray.Select(v => CsvField(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty))));
		}
		return sb.ToString();
	}

	// ヘッダーの改行(色\nサイズ)はCSVでは半角スペースに畳んで1セルに収める
	static string CsvField(string value) {
		string v = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
		if (v.Contains(',') || v.Contains('"')) {
			return $"\"{v.Replace("\"", "\"\"")}\"";
		}
		return v;
	}

	static string SanitizeFileName(string value) {
		string v = Normalize(value);
		foreach (char c in Path.GetInvalidFileNameChars()) {
			v = v.Replace(c, '_');
		}
		return v.Length == 0 ? "商品" : v;
	}

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

	static void AddSkuColumn(DataTable table, Dictionary<HaibunSkuKey, string> columnMap, HaibunSkuKey key, string baseName) {
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

public abstract class HaibunInquiryTabBase(string header) : ObservableObject {
	public string Header { get; } = header;
}

public sealed class HaibunInquirySearchTab(BaseHaibunInquiryViewModel owner) : HaibunInquiryTabBase("検索条件") {
	public BaseHaibunInquiryViewModel Owner { get; } = owner;
}

public sealed partial class HaibunInquiryStockTab : HaibunInquiryTabBase {
	public long ShohinId { get; }
	public string ProductCode { get; }
	public string ProductName { get; }

	[ObservableProperty]
	public partial DataTable StockTable { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	public HaibunInquiryStockTab(HaibunInquiryRow product, DataTable stockTable)
		: base($"{product.Code} {product.Name}") {
		ShohinId = product.Id;
		ProductCode = product.Code;
		ProductName = product.Name;
		StockTable = stockTable;
	}
}

public sealed partial class HaibunInquiryRow(MasterShohin shohin) : ObservableObject {
	public MasterShohin Shohin { get; } = shohin;
	public long Id => Shohin.Id;
	public string Code => Shohin.Code;
	public string Name => Shohin.Name;
	public int TankaJodai => Shohin.TankaJodai;
	public string BrandDisplay => FormatCodeName(Shohin.VBrand);
	public string ItemDisplay => FormatCodeName(Shohin.VItem);

	/// <summary>在庫数（SummaryRealStock.Su の商品合計）</summary>
	[ObservableProperty]
	public partial int StockSu { get; set; }

	/// <summary>引当数（SummaryRealStock.ReserveQty の商品合計）</summary>
	[ObservableProperty]
	public partial int ReserveSu { get; set; }

	/// <summary>配分数（TranHaibun.Su の商品合計、EndFlag=0）</summary>
	[ObservableProperty]
	public partial int HaibunSu { get; set; }

	/// <summary>有効在庫 = 在庫 − 引当</summary>
	public int YukoSu => StockSu - ReserveSu;

	static string FormatCodeName(CodeNameView? value) =>
		value == null ? string.Empty : CodeNameDisplay.Format(value.Sid, value.Cd, value.Mei);
}

public readonly record struct HaibunSkuKey(long IdCol, long IdSiz);
