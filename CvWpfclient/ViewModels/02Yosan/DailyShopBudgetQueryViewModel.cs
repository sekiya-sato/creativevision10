using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._02Yosan;

/// <summary>
/// 店別売上表。指定年月の「日付→店舗」順で予算・売上・差異・達成率と累計を画面照会する。
/// 同じ元データを扱う ShopBudgetReport(店舗予算表) は「店舗→日付」順で前年比を主眼にするのに対し、
/// こちらは日単位で全店を並べて当日の進捗を見るための画面。
/// 予算は MasterYosanBrand(店舗×ブランド×日)をブランド横断で合計し、実績は Tran01Tenuri のヘッダ合計を使う。
/// </summary>
public partial class DailyShopBudgetQueryViewModel : Helpers.BaseQueryViewModel {
	protected override string QueryTitle => "店別売上表";

	/// <summary>年月入力で受け付ける書式</summary>
	static readonly string[] YearMonthFormats = ["yyyy/MM", "yyyy/M", "yyyyMM", "yyyy-MM", "yyyy-M"];

	[ObservableProperty]
	public partial DateTime SelectedYearMonth { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial string SelectedYearMonthString { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>出力区分。true=店舗別明細 / false=日計(全店合計)。</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	/// <summary>結果グリッド表示中かどうか。条件パネルとの切替に使う。</summary>
	[ObservableProperty]
	public partial bool IsResultVisible { get; set; }

	/// <summary>結果ヘッダに表示する条件サマリ（例:「2019/09 店別」）。表示専用でロジックには使わない。</summary>
	[ObservableProperty]
	public partial string ConditionSummary { get; set; } = string.Empty;

	/// <summary>明細（日付×店舗）。列は BuildResultTable/BuildTotalTable で完全に揃える。</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
	public partial DataTable? ResultTable { get; set; }

	/// <summary>合計表（売上計/予算計/予算比/前年売上計/前年売上比の5行）</summary>
	[ObservableProperty]
	public partial DataTable? TotalTable { get; set; }

	/// <summary>曜日名。SQLite strftime('%w') は 0=日だが、ここでは DateTime.DayOfWeek(0=日)をそのまま使う</summary>
	static readonly string[] YoubiNames = ["日", "月", "火", "水", "木", "金", "土"];

	partial void OnSelectedYearMonthChanged(DateTime value) {
		SelectedYearMonthString = value.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	}

	[RelayCommand]
	void SelectShopCodeFrom() {
		ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;
	}

	[RelayCommand]
	void SelectShopCodeTo() {
		ShopCodeTo = SelectShopCode() ?? ShopCodeTo;
	}

	/// <summary>結果グリッドから条件パネルへ戻る</summary>
	[RelayCommand]
	void BackToConditions() => IsResultVisible = false;

	/// <summary>前月 (F6)。年月を1ヶ月戻して即再検索する。</summary>
	[RelayCommand(CanExecute = nameof(CanNavigateMonth))]
	async Task PrevMonth() {
		SelectedYearMonth = SelectedYearMonth.AddMonths(-1);
		await SearchCommand.ExecuteAsync(null);
	}

	/// <summary>今月 (F5)。年月を当月へ戻して即再検索する。</summary>
	[RelayCommand(CanExecute = nameof(CanNavigateMonth))]
	async Task ThisMonth() {
		SelectedYearMonth = DateTime.Now;
		await SearchCommand.ExecuteAsync(null);
	}

	/// <summary>翌月 (F7)。年月を1ヶ月進めて即再検索する。</summary>
	[RelayCommand(CanExecute = nameof(CanNavigateMonth))]
	async Task NextMonth() {
		SelectedYearMonth = SelectedYearMonth.AddMonths(1);
		await SearchCommand.ExecuteAsync(null);
	}

	/// <summary>検索中(IsBusy)は年月移動を禁止する。</summary>
	bool CanNavigateMonth() => !IsBusy;

	/// <summary>
	/// IsBusy は基底(BaseQueryViewModel)側のプロパティのため、この派生クラスから
	/// [NotifyCanExecuteChangedFor] を付けられない。OnPropertyChanged を横取りして
	/// 年月移動コマンドのCanExecuteを再評価する。
	/// </summary>
	protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
		base.OnPropertyChanged(e);
		if (e.PropertyName != nameof(IsBusy)) return;
		PrevMonthCommand.NotifyCanExecuteChanged();
		ThisMonthCommand.NotifyCanExecuteChanged();
		NextMonthCommand.NotifyCanExecuteChanged();
	}

	/// <summary>結果パネルの明細+合計をCSVへ出力する。列ヘッダは共通なので明細側だけ出力する。</summary>
	[RelayCommand(CanExecute = nameof(CanExportCsv))]
	void ExportCsv() {
		if (ResultTable == null || TotalTable == null) return;

		var dialog = new SaveFileDialog {
			Title = "店別売上表をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"{SanitizeFileName($"店別売上表_{SelectedYearMonth:yyyyMM}")}.csv"
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;

		try {
			File.WriteAllText(dialog.FileName, BuildCsv(ResultTable, TotalTable), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Message = $"CSVを出力しました: {dialog.FileName}";
		}
		catch (Exception ex) {
			Message = $"CSV出力失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	bool CanExportCsv() => ResultTable != null;

	protected override void OnClearConditions() {
		SelectedYearMonth = DateTime.Now;
		ShopCodeFrom = string.Empty;
		ShopCodeTo = string.Empty;
		IsByShop = true;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		// StartBusy/FinishBusy は基底の Search() コマンドが既に呼んでいるため、ここでは呼ばない。
		if (!TryParseYearMonth(SelectedYearMonthString, out var yearMonth)) {
			Message = "年月は yyyy/MM 形式で入力してください。";
			return;
		}
		SelectedYearMonth = yearMonth;
		ct.ThrowIfCancellationRequested();

		var (dateFrom, dateTo) = GetMonthRange(yearMonth);
		var daysInMonth = DateTime.DaysInMonth(yearMonth.Year, yearMonth.Month);

		// 前年同月同日。2/29 のように前年に存在しない日はそのまま前年側の範囲に含めておけば、
		// 該当日のデータが無いだけで PrevSales=0 になる(前年に2/29が無ければ自然に該当なし)。
		var prevYearMonth = new DateTime(yearMonth.Year - 1, yearMonth.Month, 1);
		var (prevDateFrom, prevDateTo) = GetMonthRange(prevYearMonth);

		List<MasterTokui> shops = await LoadTargetShopsAsync(dateFrom, dateTo, ct);
		if (shops.Count == 0) {
			Message = "対象データがありません。";
			ResultTable = null;
			TotalTable = null;
			return;
		}
		ct.ThrowIfCancellationRequested();

		Dictionary<(long Id_Tenpo, int Day), (long Sales, long Budget, long PrevSales)> facts =
			await LoadFactsAsync(dateFrom, dateTo, prevDateFrom, prevDateTo, ct);

		ResultTable = BuildResultTable(shops, facts, yearMonth, daysInMonth);
		TotalTable = BuildTotalTable(shops, facts, daysInMonth);
		Message = $"店舗 {shops.Count} 件 / {daysInMonth} 日";
		ConditionSummary = $"{yearMonth:yyyy/MM} {(IsByShop ? "店別" : "日計")}";
		IsResultVisible = true;
	}

	/// <summary>
	/// 対象店舗(直営店・コード範囲内・当月に売上データがある店舗のみ)を取得する。
	/// 予算だけあって売上がゼロの店舗は対象外にするため、Tran01Tenuri の存在を EXISTS で確認する。
	/// </summary>
	async Task<List<MasterTokui>> LoadTargetShopsAsync(string dateFrom, string dateTo, CancellationToken ct) {
		List<string> parameters = [];
		var shopWhere = BuildCodeRangeWhere(parameters, "M.Code", ShopCodeFrom, ShopCodeTo);

		string sql = $"""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code, M.Name
			FROM MasterTokui M
			WHERE M.TenType = 6 {shopWhere}
				AND EXISTS (
					SELECT 1 FROM Tran01Tenuri T
					WHERE T.Id_Tenpo = M.Id
						AND T.DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
				)
			ORDER BY M.Code
			""";

		return await QuerySqlListAsync<MasterTokui>(sql, parameters, ct);
	}

	/// <summary>
	/// 売上(当年)・予算・前年売上を UNION ALL で同じ形(Id_Tenpo, Day, Sales, Budget, PrevSales)にまとめて取得し、
	/// (Id_Tenpo, Day)をキーに合算する。対象店舗の絞り込み条件は3ブロックとも同じ(TenType=6 + コード範囲)。
	/// 該当月の日付はユーザ入力を含まない導出値(GetMonthRange)なので直接埋め込む。
	/// </summary>
	async Task<Dictionary<(long Id_Tenpo, int Day), (long Sales, long Budget, long PrevSales)>> LoadFactsAsync(
		string dateFrom, string dateTo, string prevDateFrom, string prevDateTo, CancellationToken ct) {
		List<string> parameters = [];
		var shopWhere = BuildCodeRangeWhere(parameters, "M.Code", ShopCodeFrom, ShopCodeTo);

		string sql = $"""
			SELECT T.Id_Tenpo AS Id_Tenpo, CAST(substr(T.DenDay, 7, 2) AS INTEGER) AS Day,
				SUM(T.KingakuTotal) AS Sales, 0 AS Budget, 0 AS PrevSales
			FROM Tran01Tenuri T
				JOIN MasterTokui M ON M.Id = T.Id_Tenpo AND M.TenType = 6 {shopWhere}
			WHERE T.DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
			GROUP BY T.Id_Tenpo, Day

			UNION ALL

			SELECT Y.Id_Tenpo AS Id_Tenpo, CAST(substr(Y.DenDay, 7, 2) AS INTEGER) AS Day,
				0 AS Sales, SUM(Y.UriYosan) AS Budget, 0 AS PrevSales
			FROM MasterYosanBrand Y
				JOIN MasterTokui M ON M.Id = Y.Id_Tenpo AND M.TenType = 6 {shopWhere}
			WHERE Y.DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
			GROUP BY Y.Id_Tenpo, Day

			UNION ALL

			SELECT P.Id_Tenpo AS Id_Tenpo, CAST(substr(P.DenDay, 7, 2) AS INTEGER) AS Day,
				0 AS Sales, 0 AS Budget, SUM(P.KingakuTotal) AS PrevSales
			FROM Tran01Tenuri P
				JOIN MasterTokui M ON M.Id = P.Id_Tenpo AND M.TenType = 6 {shopWhere}
			WHERE P.DenDay BETWEEN '{prevDateFrom}' AND '{prevDateTo}'
			GROUP BY P.Id_Tenpo, Day
			""";

		List<ShopDailySalesRow> rows = await QuerySqlListAsync<ShopDailySalesRow>(sql, parameters, ct);

		// UNION ALL の3ブロックはそれぞれ Sales/Budget/PrevSales のどれか1つだけが非0なので、
		// 同じキーの行を単純加算すれば3種類の値が1レコードにまとまる。
		Dictionary<(long, int), (long Sales, long Budget, long PrevSales)> facts = [];
		foreach (ShopDailySalesRow row in rows) {
			var key = (row.Id_Tenpo, row.Day);
			var current = facts.GetValueOrDefault(key);
			facts[key] = (current.Sales + row.Sales, current.Budget + row.Budget, current.PrevSales + row.PrevSales);
		}
		return facts;
	}

	/// <summary>明細 DataTable(日付×店舗)を構築する。列名・列順は BuildTotalTable と完全に一致させること。</summary>
	DataTable BuildResultTable(
		List<MasterTokui> shops,
		Dictionary<(long Id_Tenpo, int Day), (long Sales, long Budget, long PrevSales)> facts,
		DateTime yearMonth,
		int daysInMonth) {
		DataTable table = new();
		table.Columns.Add("日付", typeof(string));
		table.Columns.Add("曜日", typeof(string));
		if (IsByShop) {
			foreach (MasterTokui shop in shops) {
				table.Columns.Add(ShopColumnName(shop), typeof(long));
			}
		}
		table.Columns.Add("売上計", typeof(long));
		table.Columns.Add("予算計", typeof(long));
		table.Columns.Add("予算比", typeof(double));
		table.Columns.Add("前年売上計", typeof(long));
		table.Columns.Add("前年売上比", typeof(double));

		for (int day = 1; day <= daysInMonth; day++) {
			DateTime date = new(yearMonth.Year, yearMonth.Month, day);
			DataRow row = table.NewRow();
			row["日付"] = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
			row["曜日"] = YoubiNames[(int)date.DayOfWeek];

			long salesTotal = 0, budgetTotal = 0, prevTotal = 0;
			foreach (MasterTokui shop in shops) {
				var f = facts.GetValueOrDefault((shop.Id, day));
				if (IsByShop) row[ShopColumnName(shop)] = f.Sales;
				salesTotal += f.Sales;
				budgetTotal += f.Budget;
				prevTotal += f.PrevSales;
			}

			row["売上計"] = salesTotal;
			row["予算計"] = budgetTotal;
			row["予算比"] = budgetTotal != 0 ? Math.Round(salesTotal * 100.0 / budgetTotal, 1) : 0.0;
			row["前年売上計"] = prevTotal;
			row["前年売上比"] = prevTotal != 0 ? Math.Round(salesTotal * 100.0 / prevTotal, 1) : 0.0;
			table.Rows.Add(row);
		}

		return table;
	}

	/// <summary>
	/// 合計表(5行)を構築する。明細と列名・列順を完全一致させつつ、行ごとに意味の異なる値(金額/比率)が
	/// 同じ列に混在するため、店舗列と集計列はすべて typeof(double) にして XAML 側の書式切替に委ねる。
	/// </summary>
	DataTable BuildTotalTable(
		List<MasterTokui> shops,
		Dictionary<(long Id_Tenpo, int Day), (long Sales, long Budget, long PrevSales)> facts,
		int daysInMonth) {
		DataTable table = new();
		table.Columns.Add("日付", typeof(string));
		table.Columns.Add("曜日", typeof(string));
		if (IsByShop) {
			foreach (MasterTokui shop in shops) {
				table.Columns.Add(ShopColumnName(shop), typeof(double));
			}
		}
		table.Columns.Add("売上計", typeof(double));
		table.Columns.Add("予算計", typeof(double));
		table.Columns.Add("予算比", typeof(double));
		table.Columns.Add("前年売上計", typeof(double));
		table.Columns.Add("前年売上比", typeof(double));

		Dictionary<long, long> shopSales = [];
		Dictionary<long, long> shopBudget = [];
		Dictionary<long, long> shopPrev = [];
		foreach (MasterTokui shop in shops) {
			long sales = 0, budget = 0, prev = 0;
			for (int day = 1; day <= daysInMonth; day++) {
				var f = facts.GetValueOrDefault((shop.Id, day));
				sales += f.Sales;
				budget += f.Budget;
				prev += f.PrevSales;
			}
			shopSales[shop.Id] = sales;
			shopBudget[shop.Id] = budget;
			shopPrev[shop.Id] = prev;
		}

		long totalSales = shopSales.Values.Sum();
		long totalBudget = shopBudget.Values.Sum();
		long totalPrev = shopPrev.Values.Sum();

		AddTotalRow(table, shops, "売上計", shop => shopSales[shop.Id], "売上計", totalSales);
		AddTotalRow(table, shops, "予算計", shop => shopBudget[shop.Id], "予算計", totalBudget);
		AddTotalRow(table, shops, "予算比",
			shop => shopBudget[shop.Id] != 0 ? Math.Round(shopSales[shop.Id] * 100.0 / shopBudget[shop.Id], 1) : 0.0,
			"予算比",
			totalBudget != 0 ? Math.Round(totalSales * 100.0 / totalBudget, 1) : 0.0);
		AddTotalRow(table, shops, "前年売上計", shop => shopPrev[shop.Id], "前年売上計", totalPrev);
		AddTotalRow(table, shops, "前年売上比",
			shop => shopPrev[shop.Id] != 0 ? Math.Round(shopSales[shop.Id] * 100.0 / shopPrev[shop.Id], 1) : 0.0,
			"前年売上比",
			totalPrev != 0 ? Math.Round(totalSales * 100.0 / totalPrev, 1) : 0.0);

		return table;
	}

	/// <summary>合計表の1行を追加する。行ラベル列以外は一旦 DBNull にしてから、店舗列と集計列だけ値を入れる。</summary>
	void AddTotalRow(DataTable table, List<MasterTokui> shops, string label,
		Func<MasterTokui, double> shopValue, string aggregateColumn, double aggregateValue) {
		DataRow row = table.NewRow();
		foreach (DataColumn column in table.Columns) {
			row[column] = DBNull.Value;
		}
		row["日付"] = label;

		if (IsByShop) {
			foreach (MasterTokui shop in shops) {
				row[ShopColumnName(shop)] = shopValue(shop);
			}
		}
		row[aggregateColumn] = aggregateValue;
		table.Rows.Add(row);
	}

	static string ShopColumnName(MasterTokui shop) => $"{shop.Code} {shop.Name}";

	/// <summary>年月文字列を検証する。不正なら false。</summary>
	bool TryParseYearMonth(string? text, out DateTime yearMonth) {
		yearMonth = default;
		var value = (text ?? string.Empty).Trim();
		if (!DateTime.TryParseExact(value, YearMonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
			return false;
		}
		yearMonth = new DateTime(parsed.Year, parsed.Month, 1);
		return true;
	}

	/// <summary>指定年月の月初〜月末を yyyyMMdd で返す</summary>
	static (string dateFrom, string dateTo) GetMonthRange(DateTime yearMonth) {
		var days = DateTime.DaysInMonth(yearMonth.Year, yearMonth.Month);
		return (
			new DateTime(yearMonth.Year, yearMonth.Month, 1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			new DateTime(yearMonth.Year, yearMonth.Month, days).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
		);
	}

	// CSV出力(数値書式)は画面の書式(DetailGrid_AutoGeneratingColumn / ShopBudgetTotalCellConverter の
	// "#,##0" / "0.0")に合わせる。両クラスへ跨る共通ヘルパが無いため、書式文字列だけ揃えて重複させている。
	static readonly HashSet<string> CsvPercentColumns = ["予算比", "前年売上比"];

	/// <summary>
	/// 明細(ResultTable)→合計(TotalTable)の順に連結してCSV文字列を作る。
	/// 列名・列順は両テーブルで完全一致しているため、ヘッダ行は明細側の1回だけ出力する。
	/// </summary>
	static string BuildCsv(DataTable detail, DataTable total) {
		var sb = new StringBuilder();
		sb.AppendLine(string.Join(",", detail.Columns.Cast<DataColumn>().Select(c => CsvField(c.ColumnName))));
		foreach (DataRow row in detail.Rows) AppendCsvRow(sb, row);
		foreach (DataRow row in total.Rows) AppendCsvRow(sb, row);
		return sb.ToString();
	}

	static void AppendCsvRow(StringBuilder sb, DataRow row) {
		sb.AppendLine(string.Join(",",
			row.Table.Columns.Cast<DataColumn>().Select(c => CsvField(FormatCsvValue(row, c.ColumnName)))));
	}

	/// <summary>
	/// セルを画面表示と同じ書式の文字列にする。DBNull(合計表で自行以外の列)は空文字にする
	/// (ShopBudgetTotalCellConverter と同じ扱い)。日付/曜日はそのまま、それ以外は
	/// 予算比/前年売上比のみ小数1桁、他は金額としてカンマ区切りにする。
	/// </summary>
	static string FormatCsvValue(DataRow row, string columnName) {
		object cell = row[columnName];
		if (cell is DBNull) return string.Empty;
		if (columnName is "日付" or "曜日") return Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty;

		double number = Convert.ToDouble(cell, CultureInfo.InvariantCulture);
		return CsvPercentColumns.Contains(columnName)
			? number.ToString("0.0", CultureInfo.InvariantCulture)
			: number.ToString("#,##0", CultureInfo.InvariantCulture);
	}

	// カンマを含む金額はダブルクォートで囲む。ヘッダーの改行は無いが、値に","を含み得るため統一的にエスケープする。
	static string CsvField(string value) {
		string v = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
		if (v.Contains(',') || v.Contains('"')) {
			return $"\"{v.Replace("\"", "\"\"")}\"";
		}
		return v;
	}

	static string SanitizeFileName(string value) {
		string v = value.Trim();
		foreach (char c in Path.GetInvalidFileNameChars()) {
			v = v.Replace(c, '_');
		}
		return v.Length == 0 ? "店別売上表" : v;
	}
}
