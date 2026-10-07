using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>RFMの軸</summary>
public enum RfmAxis {
	R,
	F,
	M,
}

/// <summary>
/// RFMクロス分析表。基準日・期間・店舗で顧客をR(最終購入からの経過日数)/F(購入回数)/M(購入額)の5段階に分け、
/// 選んだ2軸のクロス表で人数・構成比・金額・平均を表示する。セル選択で該当顧客一覧を取得する。
/// 集計はサーバ(Msg065)、明細はサーバ(Msg066)。軸切替は再検索せず、取得済み Cells を残り1軸で合算して再構築する。
/// </summary>
public partial class RfmCrossAnalysisTableViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "RFMクロス分析表";

	/// <summary>表示軸の選択肢</summary>
	public IReadOnlyList<RfmAxis> AxisItems { get; } = [RfmAxis.R, RfmAxis.F, RfmAxis.M];

	/// <summary>期間の最大月数（開始月・終了月を含む）</summary>
	public const int MaxMonths = 12;
	static readonly string[] YearMonthFormats = ["yyyy/MM", "yyyy/M", "yyyyMM"];

	/// <summary>開始年月(yyyy/MM)。既定は当月-11</summary>
	[ObservableProperty]
	public partial string MonthFrom { get; set; } = DefaultMonthFrom();

	/// <summary>終了年月(yyyy/MM)。既定は当月。R の基準日は終了月の月末（今日以降なら今日）</summary>
	[ObservableProperty]
	public partial string MonthTo { get; set; } = DefaultMonthTo();

	/// <summary>購入店舗コード。空欄=全店</summary>
	[ObservableProperty]
	public partial string ShopCode { get; set; } = string.Empty;

	/// <summary>選択ダイアログで選んだ店舗名（表示用）</summary>
	[ObservableProperty]
	public partial string ShopName { get; set; } = "全店";

	[ObservableProperty]
	public partial bool IncludeWithdrawn { get; set; }

	/// <summary>R/F/M の閾値入力行</summary>
	public ObservableCollection<RfmThresholdRow> Thresholds { get; } = [];

	[ObservableProperty]
	public partial RfmAxis VerticalAxis { get; set; } = RfmAxis.R;

	[ObservableProperty]
	public partial RfmAxis HorizontalAxis { get; set; } = RfmAxis.F;

	/// <summary>クロス表(見出し含む 7×7)。UniformGrid に行優先で並べる</summary>
	[ObservableProperty]
	public partial ObservableCollection<RfmGridCell> GridCells { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
	public partial bool HasResult { get; set; }

	[ObservableProperty]
	public partial string SummaryText { get; set; } = string.Empty;

	/// <summary>セル明細の顧客一覧</summary>
	[ObservableProperty]
	public partial ObservableCollection<RfmCustomerRowView> CustomerRows { get; set; } = [];

	[ObservableProperty]
	public partial string CustomerCaption { get; set; } = "表のセルを選択すると該当顧客を表示します。";

	/// <summary>表示軸の列数（見出し列＋ランク5列＋合計列）</summary>
	public int GridColumns => RfmRank.Levels + 2;

	RfmAnalysisResult? _result;
	/// <summary>結果を取得した時点の条件。明細取得と見出し・CSVはこの条件を使う</summary>
	RfmAnalysisParameter? _resultParam;
	string _resultShopLabel = string.Empty;
	/// <summary>条件変更・再検索のたびに進める。非同期応答が古い条件のものなら破棄する</summary>
	int _version;
	bool _suppressInvalidate;

	public RfmCrossAnalysisTableViewModel() {
		Thresholds.Add(new RfmThresholdRow(RfmAxis.R, "R 経過日数", "日以内", RfmRank.DefaultRBounds.Select(x => (long)x)));
		Thresholds.Add(new RfmThresholdRow(RfmAxis.F, "F 購入回数", "回以上", RfmRank.DefaultFBounds));
		Thresholds.Add(new RfmThresholdRow(RfmAxis.M, "M 購入金額", "円以上", RfmRank.DefaultMBounds));
		foreach (var row in Thresholds) {
			row.PropertyChanged += (_, _) => InvalidateResult();
		}
	}

	partial void OnMonthFromChanged(string value) => InvalidateResult();
	partial void OnMonthToChanged(string value) => InvalidateResult();
	partial void OnShopCodeChanged(string value) {
		// 手入力でコードを変えた場合は名称を検索時に解決し直す
		if (!_suppressInvalidate) ShopName = string.IsNullOrWhiteSpace(value) ? "全店" : string.Empty;
		InvalidateResult();
	}
	partial void OnIncludeWithdrawnChanged(bool value) => InvalidateResult();

	/// <summary>縦軸と横軸が同じにならないよう、ぶつかった側を入れ替える</summary>
	partial void OnVerticalAxisChanged(RfmAxis oldValue, RfmAxis newValue) {
		if (newValue == HorizontalAxis) HorizontalAxis = oldValue;
		RebuildGrid();
	}

	partial void OnHorizontalAxisChanged(RfmAxis oldValue, RfmAxis newValue) {
		if (newValue == VerticalAxis) VerticalAxis = oldValue;
		RebuildGrid();
	}

	/// <summary>
	/// 条件変更時は旧結果(表・明細)を破棄し、旧条件でセル明細を取得できないようにする。
	/// </summary>
	void InvalidateResult() {
		_version++;
		if (_result == null && CustomerRows.Count == 0) return;
		_result = null;
		_resultParam = null;
		HasResult = false;
		GridCells = [];
		SummaryText = string.Empty;
		ClearCustomers("条件が変更されました。再度検索してください。");
	}

	void ClearCustomers(string caption) {
		CustomerRows = [];
		CustomerCaption = caption;
		foreach (var cell in GridCells) cell.IsSelected = false;
	}

	[RelayCommand]
	void SelectShop() {
		var shop = PrintPdfHelper.ShowSelectDialog<MasterTokui>(this, typeof(MasterTokui), "TenType=6", "Code");
		if (shop == null) return;
		_suppressInvalidate = true;
		try {
			ShopCode = shop.Code;
		}
		finally {
			_suppressInvalidate = false;
		}
		ShopName = shop.Name;
	}

	[RelayCommand]
	void ResetThresholds() {
		Thresholds[0].SetValues(RfmRank.DefaultRBounds.Select(x => (long)x));
		Thresholds[1].SetValues(RfmRank.DefaultFBounds);
		Thresholds[2].SetValues(RfmRank.DefaultMBounds);
		Message = "閾値を既定に戻しました";
	}

	protected override void OnClearConditions() {
		MonthFrom = DefaultMonthFrom();
		MonthTo = DefaultMonthTo();
		ShopCode = string.Empty;
		IncludeWithdrawn = false;
		ResetThresholds();
	}

	/// <summary>検索中はセル選択・CSVを止めるため、IsBusy 変化でコマンドを再評価する</summary>
	protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
		base.OnPropertyChanged(e);
		if (e.PropertyName != nameof(IsBusy)) return;
		SelectCellCommand.NotifyCanExecuteChanged();
		ExportCsvCommand.NotifyCanExecuteChanged();
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		// StartBusy/FinishBusy と二重実行防止は基底の Search() が行う
		if (!TryParseYearMonth(MonthFrom, out var monthFrom) || !TryParseYearMonth(MonthTo, out var monthTo)) {
			MessageEx.ShowWarningDialog("対象年月は yyyy/MM 形式で入力してください。", owner: ActiveWindow);
			Message = string.Empty;
			return;
		}
		var months = (monthTo.Year - monthFrom.Year) * 12 + monthTo.Month - monthFrom.Month + 1;
		if (months < 1) {
			MessageEx.ShowWarningDialog("対象年月の開始は終了以前を指定してください。", owner: ActiveWindow);
			Message = string.Empty;
			return;
		}
		if (months > MaxMonths) {
			MessageEx.ShowWarningDialog($"対象年月の範囲は{MaxMonths}ヶ月以内で指定してください（指定 {months}ヶ月）。", owner: ActiveWindow);
			Message = string.Empty;
			return;
		}
		// 終了月の月末を基準日とする。未来日は基準にしない（今日以降なら今日）
		var monthEnd = monthTo.AddMonths(1).AddDays(-1);
		var baseDay = monthEnd > DateTime.Today ? DateTime.Today : monthEnd;
		if (baseDay < monthFrom) {
			MessageEx.ShowWarningDialog("対象年月の開始が未来です。", owner: ActiveWindow);
			Message = string.Empty;
			return;
		}
		var bounds = new List<long>[3];
		for (var i = 0; i < Thresholds.Count; i++) {
			if (!Thresholds[i].TryGetValues(out var values)) {
				MessageEx.ShowWarningDialog($"{Thresholds[i].Label}の閾値は数値で入力してください。", owner: ActiveWindow);
				Message = string.Empty;
				return;
			}
			bounds[i] = values;
		}
		if (bounds[0].Any(x => x > int.MaxValue)) {
			MessageEx.ShowWarningDialog("R閾値が大きすぎます。", owner: ActiveWindow);
			Message = string.Empty;
			return;
		}

		var param = new RfmAnalysisParameter {
			BaseDay = ToDenDay(baseDay),
			DayFrom = ToDenDay(monthFrom),
			IncludeWithdrawn = IncludeWithdrawn,
			RBounds = [.. bounds[0].Select(x => (int)x)],
			FBounds = bounds[1],
			MBounds = bounds[2],
		};
		var error = RfmRank.Validate(param);
		if (error != null) {
			MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
			Message = string.Empty;
			return;
		}

		// 旧結果を消してから取得する。取得中に条件が変わったら応答を捨てる
		InvalidateResult();
		var version = _version;

		var shopLabel = "全店";
		var code = ShopCode.Trim();
		if (code.Length > 0) {
			List<string> parameters = [];
			var sql = $"SELECT Id, Vdc, Vdu, Code, Name FROM MasterTokui WHERE TenType = 6 AND Code = {AddSqlParameter(parameters, code)}";
			var shops = await QuerySqlListAsync<MasterTokui>(sql, parameters, ct);
			if (version != _version) return;
			if (shops.Count == 0) {
				Message = $"店舗コード {code} が見つかりません。";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}
			param.Id_Tenpo = shops[0].Id;
			shopLabel = $"{shops[0].Code} {shops[0].Name}";
			ShopName = shops[0].Name;
		}

		var result = await QueryMsgAsync<RfmAnalysisResult>(CvFlag.Msg065_RfmCrossAnalysis, param, "RFM集計", ct);
		if (version != _version) return;

		_result = result;
		_resultParam = param;
		_resultShopLabel = shopLabel;
		HasResult = true;
		var purchase = result.Cells.Sum(x => x.Count);
		SummaryText = $"期間 {FormatDay(param.DayFrom)}〜{FormatDay(param.BaseDay)}（{months}ヶ月） / 店舗 {shopLabel}"
			+ $" / 対象顧客 {result.TargetCount:N0}人 / 購入顧客 {purchase:N0}人 / 購入なし {result.NoPurchaseCount:N0}人";
		RebuildGrid();
		ClearCustomers("表のセルを選択すると該当顧客を表示します。");
		Message = purchase > 0 ? $"購入顧客 {purchase:N0}人を集計しました" : "期間内に購入した顧客がありません";
	}

	/// <summary>セル明細を取得する。合計行・合計列は該当軸だけ指定（0=指定なし）</summary>
	[RelayCommand(CanExecute = nameof(CanSelectCell))]
	async Task SelectCell(RfmGridCell? cell) {
		if (cell == null || !cell.IsSelectable || _resultParam == null || IsBusy) return;
		var version = _version;
		var src = _resultParam;
		var param = new RfmAnalysisParameter {
			DayFrom = src.DayFrom,
			BaseDay = src.BaseDay,
			Id_Tenpo = src.Id_Tenpo,
			IncludeWithdrawn = src.IncludeWithdrawn,
			RBounds = src.RBounds,
			FBounds = src.FBounds,
			MBounds = src.MBounds,
			Limit = RfmRank.DefaultLimit,
		};
		SetRank(param, VerticalAxis, cell.RowRank);
		SetRank(param, HorizontalAxis, cell.ColRank);

		foreach (var c in GridCells) c.IsSelected = ReferenceEquals(c, cell);
		var label = CellLabel(cell);
		StartBusy("顧客一覧を取得中...");
		try {
			var result = await QueryMsgAsync<RfmCustomerListResult>(CvFlag.Msg066_RfmCustomerList, param, "顧客一覧取得", CancellationToken.None);
			if (version != _version) return;
			CustomerRows = [.. result.Rows.Select(RfmCustomerRowView.From)];
			CustomerCaption = result.TotalCount > result.Rows.Count
				? $"{label} の顧客（金額上位 {result.Rows.Count:N0}件 / 全 {result.TotalCount:N0}件）"
				: $"{label} の顧客（全 {result.TotalCount:N0}件）";
			Message = string.Empty;
		}
		catch (Exception ex) {
			Message = $"顧客一覧の取得に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	bool CanSelectCell(RfmGridCell? cell) => !IsBusy;

	static void SetRank(RfmAnalysisParameter p, RfmAxis axis, int rank) {
		switch (axis) {
			case RfmAxis.R: p.RRank = rank; break;
			case RfmAxis.F: p.FRank = rank; break;
			default: p.MRank = rank; break;
		}
	}

	string CellLabel(RfmGridCell cell) {
		var parts = new List<string>();
		if (cell.RowRank > 0) parts.Add($"{VerticalAxis}{cell.RowRank}");
		if (cell.ColRank > 0) parts.Add($"{HorizontalAxis}{cell.ColRank}");
		return parts.Count == 0 ? "全体" : string.Join("×", parts);
	}

	async Task<T> QueryMsgAsync<T>(CvFlag flag, RfmAnalysisParameter param, string action, CancellationToken ct) where T : class {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = flag,
			DataType = typeof(RfmAnalysisParameter),
			DataMsg = Common.SerializeObject(param),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? $"{action}に失敗しました。");
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as T
			?? throw new InvalidOperationException($"{action}の結果を解析できませんでした。");
	}

	/// <summary>取得済み Cells を縦軸×横軸のランクへ合算した集計値</summary>
	sealed record CrossData(long[,] Count, long[,] Amount, long Purchase);

	/// <summary>
	/// Cells を表示軸で合算する。添字 [縦ランク, 横ランク]、0 は合計（全ランク）。
	/// </summary>
	CrossData? Aggregate() {
		if (_result == null) return null;
		var n = RfmRank.Levels + 1;
		var count = new long[n, n];
		var amount = new long[n, n];
		foreach (var cell in _result.Cells) {
			var v = RankOf(cell, VerticalAxis);
			var h = RankOf(cell, HorizontalAxis);
			if (v < 1 || v > RfmRank.Levels || h < 1 || h > RfmRank.Levels) continue;
			foreach (var (vi, hi) in new[] { (v, h), (v, 0), (0, h), (0, 0) }) {
				count[vi, hi] += cell.Count;
				amount[vi, hi] += cell.Amount;
			}
		}
		return new CrossData(count, amount, count[0, 0]);
	}

	static int RankOf(RfmCell cell, RfmAxis axis) => axis switch {
		RfmAxis.R => cell.RRank,
		RfmAxis.F => cell.FRank,
		_ => cell.MRank,
	};

	/// <summary>表示軸の並び（ランク5→1、最後に合計=0）</summary>
	static IEnumerable<int> RankOrder() => Enumerable.Range(1, RfmRank.Levels).Reverse().Append(0);

	/// <summary>表を再構築する。軸切替時は明細を消す（選択セルの意味が変わるため）</summary>
	void RebuildGrid() {
		var data = Aggregate();
		if (data == null || _resultParam == null) {
			GridCells = [];
			return;
		}
		var max = 1L;
		for (var v = 1; v <= RfmRank.Levels; v++)
			for (var h = 1; h <= RfmRank.Levels; h++)
				max = Math.Max(max, data.Count[v, h]);

		var cells = new ObservableCollection<RfmGridCell> {
			RfmGridCell.Header($"{VerticalAxis} ＼ {HorizontalAxis}", string.Empty),
		};
		foreach (var h in RankOrder()) {
			cells.Add(h == 0 ? RfmGridCell.Header("合計", string.Empty) : RfmGridCell.Header($"{HorizontalAxis}{h}", RankRange(HorizontalAxis, h, _resultParam)));
		}
		foreach (var v in RankOrder()) {
			cells.Add(v == 0 ? RfmGridCell.Header("合計", string.Empty) : RfmGridCell.Header($"{VerticalAxis}{v}", RankRange(VerticalAxis, v, _resultParam)));
			foreach (var h in RankOrder()) {
				var c = data.Count[v, h];
				var a = data.Amount[v, h];
				// 濃淡はランク×ランクのセルだけに付ける（合計は人数が大きく常に濃くなるため）
				var intensity = v > 0 && h > 0 ? 0.5 * c / max : 0;
				cells.Add(RfmGridCell.Data(v, h, c, a, data.Purchase, intensity, isTotal: v == 0 || h == 0));
			}
		}
		GridCells = cells;
		if (CustomerRows.Count > 0) ClearCustomers("表示軸を変更しました。セルを選択すると該当顧客を表示します。");
	}

	/// <summary>ランクの範囲表示（例 R5 "〜30日", F5 "10回以上", M5 "10万円以上"）</summary>
	static string RankRange(RfmAxis axis, int rank, RfmAnalysisParameter p) {
		if (axis == RfmAxis.R) {
			// RankR: 経過日数 <= b[0] でランク5、b[3] 超でランク1
			var b = p.RBounds;
			var i = RfmRank.Levels - rank; // 0..4
			return i switch {
				0 => $"〜{b[0]:N0}日",
				_ when i == b.Count => $"{b[^1] + 1:N0}日〜",
				_ => $"{b[i - 1] + 1:N0}〜{b[i]:N0}日",
			};
		}
		var bounds = axis == RfmAxis.F ? p.FBounds : p.MBounds;
		// RankUp: 値 >= b[k-2] でランクk以上
		var lo = rank >= 2 ? bounds[rank - 2] : (long?)null;
		var hi = rank <= bounds.Count ? bounds[rank - 1] : (long?)null;
		if (axis == RfmAxis.F) {
			if (lo == null) return hi!.Value - 1 <= 1 ? "1回" : $"〜{hi - 1:N0}回";
			if (hi == null) return $"{lo:N0}回以上";
			return hi - 1 == lo ? $"{lo:N0}回" : $"{lo:N0}〜{hi - 1:N0}回";
		}
		if (lo == null) return $"{Yen(hi!.Value)}未満";
		if (hi == null) return $"{Yen(lo.Value)}以上";
		return $"{Yen(lo.Value)}〜{Yen(hi.Value)}未満";
	}

	/// <summary>金額の短縮表示。万円単位で割り切れる場合は「N万円」</summary>
	static string Yen(long value) =>
		value % 10000 == 0 ? $"{value / 10000:N0}万円" : $"{value:N0}円";

	static string DefaultMonthFrom() => DateTime.Today.AddMonths(-(MaxMonths - 1)).ToString("yyyy/MM", CultureInfo.InvariantCulture);
	static string DefaultMonthTo() => DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	/// <summary>年月文字列(yyyy/MM 等)を月初の日付へ変換する</summary>
	static bool TryParseYearMonth(string? text, out DateTime yearMonth) {
		yearMonth = default;
		if (!DateTime.TryParseExact((text ?? string.Empty).Trim(), YearMonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
		yearMonth = new DateTime(parsed.Year, parsed.Month, 1);
		return true;
	}

	static string FormatDay(string yyyymmdd) =>
		DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
			: yyyymmdd;

	/// <summary>クロス表(条件・人数・構成比・金額・平均)をCSV出力する。顧客一覧は個人情報のため出力しない</summary>
	[RelayCommand(CanExecute = nameof(CanExportCsv))]
	void ExportCsv() {
		if (_result == null || _resultParam == null) return;
		var dialog = new SaveFileDialog {
			Title = "RFMクロス分析表をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"RFMクロス分析表_{_resultParam.BaseDay}_{VerticalAxis}{HorizontalAxis}.csv",
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;
		try {
			File.WriteAllText(dialog.FileName, BuildCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Message = $"CSVを出力しました: {dialog.FileName}";
		}
		catch (Exception ex) {
			Message = $"CSV出力失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	bool CanExportCsv() => HasResult && !IsBusy;

	string BuildCsv() {
		var p = _resultParam!;
		var data = Aggregate()!;
		var sb = new StringBuilder();
		void Line(params string[] fields) => sb.AppendLine(CsvText.BuildLine(fields));

		Line("RFMクロス分析表");
		Line("基準日", FormatDay(p.BaseDay));
		Line("期間", $"{FormatDay(p.DayFrom)}〜{FormatDay(p.BaseDay)}");
		Line("購入店舗", _resultShopLabel);
		Line("退会者", p.IncludeWithdrawn ? "含む" : "含まない");
		Line(["R閾値(日)", .. p.RBounds.Select(x => x.ToString(CultureInfo.InvariantCulture))]);
		Line(["F閾値(回)", .. p.FBounds.Select(x => x.ToString(CultureInfo.InvariantCulture))]);
		Line(["M閾値(円)", .. p.MBounds.Select(x => x.ToString(CultureInfo.InvariantCulture))]);
		Line("対象顧客数", _result!.TargetCount.ToString(CultureInfo.InvariantCulture));
		Line("購入顧客数", data.Purchase.ToString(CultureInfo.InvariantCulture));
		Line("購入なし", _result.NoPurchaseCount.ToString(CultureInfo.InvariantCulture));

		var blocks = new (string Name, Func<int, int, string> Value)[] {
			("人数", (v, h) => data.Count[v, h].ToString(CultureInfo.InvariantCulture)),
			("構成比(%)", (v, h) => RfmGridCell.Ratio(data.Count[v, h], data.Purchase).ToString("0.0", CultureInfo.InvariantCulture)),
			("金額", (v, h) => data.Amount[v, h].ToString(CultureInfo.InvariantCulture)),
			("平均金額", (v, h) => RfmGridCell.Average(data.Count[v, h], data.Amount[v, h]).ToString(CultureInfo.InvariantCulture)),
		};
		string AxisHeader(RfmAxis axis, int rank) => rank == 0 ? "合計" : $"{axis}{rank} {RankRange(axis, rank, p)}";
		foreach (var (name, value) in blocks) {
			sb.AppendLine();
			Line(name);
			Line([$"{VerticalAxis}＼{HorizontalAxis}", .. RankOrder().Select(h => AxisHeader(HorizontalAxis, h))]);
			foreach (var v in RankOrder()) {
				Line([AxisHeader(VerticalAxis, v), .. RankOrder().Select(h => value(v, h))]);
			}
		}
		return sb.ToString();
	}
}

/// <summary>閾値入力1行（4値）。値の変更は親VMへ PropertyChanged で伝える</summary>
public partial class RfmThresholdRow : ObservableObject {
	public RfmAxis Axis { get; }
	public string Label { get; }
	public string Unit { get; }

	[ObservableProperty]
	public partial string Value1 { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string Value2 { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string Value3 { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string Value4 { get; set; } = string.Empty;

	public RfmThresholdRow(RfmAxis axis, string label, string unit, IEnumerable<long> values) {
		Axis = axis;
		Label = label;
		Unit = unit;
		SetValues(values);
	}

	public void SetValues(IEnumerable<long> values) {
		var v = values.Select(x => x.ToString("N0", CultureInfo.InvariantCulture)).ToArray();
		Value1 = v[0];
		Value2 = v[1];
		Value3 = v[2];
		Value4 = v[3];
	}

	/// <summary>4値を数値化する。桁区切りカンマを許可。不正なら false</summary>
	public bool TryGetValues(out List<long> values) {
		values = [];
		foreach (var text in new[] { Value1, Value2, Value3, Value4 }) {
			if (!long.TryParse((text ?? string.Empty).Trim(), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var x)) return false;
			values.Add(x);
		}
		return true;
	}
}

/// <summary>クロス表の1セル（見出し・データ・合計）</summary>
public partial class RfmGridCell : ObservableObject {
	public bool IsHeader { get; private init; }
	public bool IsTotal { get; private init; }
	/// <summary>セル選択で明細を取得できるか（データ・合計セルで人数>0）</summary>
	public bool IsSelectable { get; private init; }
	/// <summary>縦軸ランク。0=合計</summary>
	public int RowRank { get; private init; }
	/// <summary>横軸ランク。0=合計</summary>
	public int ColRank { get; private init; }
	/// <summary>人数</summary>
	public long Count { get; private init; }
	/// <summary>金額合計</summary>
	public long Amount { get; private init; }
	public string Title { get; private init; } = string.Empty;
	public string SubTitle { get; private init; } = string.Empty;
	public string CountText { get; private init; } = string.Empty;
	public string RatioText { get; private init; } = string.Empty;
	public string AmountText { get; private init; } = string.Empty;
	public string AverageText { get; private init; } = string.Empty;
	/// <summary>人数に応じた背景の濃さ 0〜1</summary>
	public double Intensity { get; private init; }

	[ObservableProperty]
	public partial bool IsSelected { get; set; }

	public static RfmGridCell Header(string title, string subTitle) => new() { IsHeader = true, Title = title, SubTitle = subTitle };

	public static RfmGridCell Data(int rowRank, int colRank, long count, long amount, long purchase, double intensity, bool isTotal) => new() {
		RowRank = rowRank,
		ColRank = colRank,
		IsTotal = isTotal,
		IsSelectable = count > 0,
		Count = count,
		Amount = amount,
		CountText = $"{count:N0}人",
		RatioText = $"{Ratio(count, purchase):0.0}%",
		AmountText = $"¥{amount:N0}",
		AverageText = $"平均 ¥{Average(count, amount):N0}",
		Intensity = intensity,
	};

	/// <summary>構成比(%)。分母は購入顧客数、小数1桁</summary>
	public static double Ratio(long count, long purchase) => purchase > 0 ? Math.Round(count * 100.0 / purchase, 1, MidpointRounding.AwayFromZero) : 0;

	public static long Average(long count, long amount) => count > 0 ? (long)Math.Round((double)amount / count, MidpointRounding.AwayFromZero) : 0;
}

/// <summary>顧客一覧の表示行</summary>
public sealed class RfmCustomerRowView {
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string Kana { get; init; } = string.Empty;
	public string LastDay { get; init; } = string.Empty;
	public int Days { get; init; }
	public long Frequency { get; init; }
	public long Amount { get; init; }
	public int RRank { get; init; }
	public int FRank { get; init; }
	public int MRank { get; init; }

	public static RfmCustomerRowView From(RfmCustomerRow r) => new() {
		Code = r.Code,
		Name = r.Name,
		Kana = r.Kana,
		LastDay = r.LastDay.Length == 8 ? $"{r.LastDay[..4]}/{r.LastDay[4..6]}/{r.LastDay[6..]}" : r.LastDay,
		Days = r.Days,
		Frequency = r.Frequency,
		Amount = r.Amount,
		RRank = r.RRank,
		FRank = r.FRank,
		MRank = r.MRank,
	};
}
