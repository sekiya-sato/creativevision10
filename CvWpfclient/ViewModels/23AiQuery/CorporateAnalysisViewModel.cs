using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._23AiQuery;

/// <summary>
/// 全社分析（Doc/spec/2026-11-17_全社分析_設計.md）。
/// 年・月・日の推移表で全社の期首/期末在庫・発注・受注・仕入・売上・粗利を見て、
/// 内訳表（ブランド→アイテム→シーズン→展示会→素材→商品）をダブルクリックで絞り込んでいく。
/// <para>
/// 集計はサーバ(Msg068)の SQL で行う。画面は比率・判定の計算と表示だけを持つ。
/// 推移表と内訳表は同じ絞込で取得し、条件（単位・期間）を変えたら結果を消して古い結果で操作させない。
/// </para>
/// </summary>
public partial class CorporateAnalysisViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "全社分析";

	/// <summary>判定: 消化率がこれ未満で発注がある商品を「過剰発注」とする(%)</summary>
	public const double OverOrderSellThroughPercent = 30;
	/// <summary>判定: 在庫回転日数がこれを超えると「在庫過多」(日)</summary>
	public const double OverStockTurnoverDays = 180;

	/// <summary>内訳の既定の進み順。絞り込むと未使用の次の軸へ進む</summary>
	static readonly CorporateAnalysisAxis[] DrillOrder = [
		CorporateAnalysisAxis.Brand, CorporateAnalysisAxis.Item, CorporateAnalysisAxis.Season,
		CorporateAnalysisAxis.Tenji, CorporateAnalysisAxis.Material, CorporateAnalysisAxis.Shohin,
	];

	public IReadOnlyList<Option<CorporateAnalysisUnit>> UnitItems { get; } = [
		new(CorporateAnalysisUnit.Year, "年（年度）"),
		new(CorporateAnalysisUnit.Month, "月"),
		new(CorporateAnalysisUnit.Day, "日"),
	];

	public IReadOnlyList<Option<CorporateAnalysisAxis>> AxisItems { get; } = [
		new(CorporateAnalysisAxis.Brand, "ブランド"),
		new(CorporateAnalysisAxis.Item, "アイテム"),
		new(CorporateAnalysisAxis.Season, "シーズン"),
		new(CorporateAnalysisAxis.Tenji, "展示会"),
		new(CorporateAnalysisAxis.Material, "素材"),
		new(CorporateAnalysisAxis.Shohin, "商品"),
	];

	public IReadOnlyList<Option<CorporateAnalysisSortKey>> SortItems { get; } = [
		new(CorporateAnalysisSortKey.Sales, "売上金額順"),
		new(CorporateAnalysisSortKey.ClosingStock, "期末在庫金額順"),
		new(CorporateAnalysisSortKey.Hachu, "発注金額順"),
	];

	[ObservableProperty]
	public partial Option<CorporateAnalysisUnit> SelectedUnit { get; set; }

	[ObservableProperty]
	public partial string FromText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ToText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string PeriodHint { get; set; } = string.Empty;

	[ObservableProperty]
	public partial Option<CorporateAnalysisAxis> SelectedAxis { get; set; }

	[ObservableProperty]
	public partial Option<CorporateAnalysisSortKey> SelectedSort { get; set; }

	/// <summary>絞込（全社 › ブランド › …）</summary>
	public ObservableCollection<CorporateAnalysisFilter> Filters { get; } = [];

	[ObservableProperty]
	public partial string FilterText { get; set; } = "全社";

	[ObservableProperty]
	public partial ObservableCollection<CorporateAnalysisRowView> TrendRows { get; set; } = [];

	[ObservableProperty]
	public partial CorporateAnalysisRowView? SelectedTrendRow { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<CorporateAnalysisRowView> BreakdownRows { get; set; } = [];

	[ObservableProperty]
	public partial CorporateAnalysisRowView? SelectedBreakdownRow { get; set; }

	[ObservableProperty]
	public partial string TrendCaption { get; set; } = "推移";

	[ObservableProperty]
	public partial string BreakdownCaption { get; set; } = "内訳";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExportCsvCommand), nameof(BackCommand), nameof(ResetFiltersCommand))]
	public partial bool HasResult { get; set; }

	/// <summary>結果を取得した時点の条件（単位・期間）。内訳の再取得・CSVはこれを使う</summary>
	CorporateAnalysisParameter? _resultParam;
	List<CorporateAnalysisBucket> _buckets = [];
	/// <summary>内訳表の対象区切り（null=期間全体）</summary>
	CorporateAnalysisBucket? _breakdownBucket;
	/// <summary>非同期応答が古い条件のものなら捨てるための世代</summary>
	int _version;
	bool _suppress;

	public CorporateAnalysisViewModel() {
		_suppress = true;
		SelectedAxis = AxisItems[0];
		SelectedSort = SortItems[0];
		SelectedUnit = UnitItems[1];
		_suppress = false;
		ApplyDefaultPeriod();
	}

	partial void OnSelectedUnitChanged(Option<CorporateAnalysisUnit> value) {
		if (_suppress) return;
		ApplyDefaultPeriod();
		InvalidateResult();
	}

	partial void OnFromTextChanged(string value) { if (!_suppress) InvalidateResult(); }
	partial void OnToTextChanged(string value) { if (!_suppress) InvalidateResult(); }

	partial void OnSelectedAxisChanged(Option<CorporateAnalysisAxis> value) {
		if (_suppress || !HasResult) return;
		_ = ReloadBreakdownAsync();
	}

	partial void OnSelectedSortChanged(Option<CorporateAnalysisSortKey> value) {
		if (_suppress || !HasResult || SelectedAxis.Value != CorporateAnalysisAxis.Shohin) return;
		_ = ReloadBreakdownAsync();
	}

	partial void OnSelectedTrendRowChanged(CorporateAnalysisRowView? value) {
		if (_suppress || !HasResult || value == null) return;
		var bucket = value.IsTotal ? null : _buckets.FirstOrDefault(b => b.Key == value.BucketKey);
		if (bucket?.Key == _breakdownBucket?.Key) return;
		_breakdownBucket = bucket;
		_ = ReloadBreakdownAsync();
	}

	/// <summary>単位を切り替えたときの既定期間（直近）。</summary>
	void ApplyDefaultPeriod() {
		var today = DateTime.Today;
		_suppress = true;
		switch (SelectedUnit.Value) {
		case CorporateAnalysisUnit.Year:
			FromText = (today.Year - 4).ToString(CultureInfo.InvariantCulture);
			ToText = today.Year.ToString(CultureInfo.InvariantCulture);
			PeriodHint = $"年度は期首月から12か月（最大{CorporateAnalysisPeriod.MaxYears}年度）。yyyy で入力";
			break;
		case CorporateAnalysisUnit.Day:
			FromText = new DateTime(today.Year, today.Month, 1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
			ToText = today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
			PeriodHint = $"最大{CorporateAnalysisPeriod.MaxDays}日。yyyy/MM/dd で入力";
			break;
		default:
			FromText = today.AddMonths(-11).ToString("yyyy/MM", CultureInfo.InvariantCulture);
			ToText = today.ToString("yyyy/MM", CultureInfo.InvariantCulture);
			PeriodHint = $"自社締日による計上月（最大{CorporateAnalysisPeriod.MaxMonths}か月）。yyyy/MM で入力";
			break;
		}
		_suppress = false;
	}

	void InvalidateResult() {
		_version++;
		if (!HasResult && TrendRows.Count == 0 && BreakdownRows.Count == 0) return;
		HasResult = false;
		_resultParam = null;
		_buckets = [];
		_breakdownBucket = null;
		TrendRows = [];
		BreakdownRows = [];
		TrendCaption = "推移";
		BreakdownCaption = "内訳";
		Message = "条件が変わりました。［集計］で集計し直してください。";
	}

	protected override void OnClearConditions() {
		_suppress = true;
		SelectedUnit = UnitItems[1];
		SelectedAxis = AxisItems[0];
		SelectedSort = SortItems[0];
		_suppress = false;
		ApplyDefaultPeriod();
		Filters.Clear();
		UpdateFilterText();
		InvalidateResult();
	}

	/// <summary>入力を検証してサーバ形式の期間（年=yyyy / 月=yyyyMM / 日=yyyyMMdd）にする。</summary>
	bool TryBuildPeriod(out string from, out string to) {
		from = to = string.Empty;
		var unit = SelectedUnit.Value;
		bool Parse(string text, out string value) {
			value = string.Empty;
			var s = (text ?? string.Empty).Trim();
			switch (unit) {
			case CorporateAnalysisUnit.Year:
				if (s.Length == 4 && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out _)) { value = s; return true; }
				return false;
			case CorporateAnalysisUnit.Day:
				if (TryParseDateQuiet(s, out var d)) { value = ToDenDay(d); return true; }
				return false;
			default:
				if (DateTime.TryParseExact(s, ["yyyy/MM", "yyyy/M", "yyyyMM", "yyyy-MM"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)) {
					value = m.ToString("yyyyMM", CultureInfo.InvariantCulture);
					return true;
				}
				return false;
			}
		}
		if (!Parse(FromText, out from) || !Parse(ToText, out to)) {
			MessageEx.ShowWarningDialog($"期間の入力が不正です。{PeriodHint}", owner: ActiveWindow);
			return false;
		}
		return true;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryBuildPeriod(out var from, out var to)) {
			Message = string.Empty;
			return;
		}
		var version = ++_version;
		var baseParam = new CorporateAnalysisParameter { Unit = SelectedUnit.Value, From = from, To = to };
		var started = DateTime.Now;
		CorporateAnalysisResult trend;
		CorporateAnalysisResult breakdown;
		try {
			// 推移（全社×区切り）と内訳（軸×期間全体）を並行して取得する
			var trendTask = QueryAsync(Clone(baseParam, byBucket: true, CorporateAnalysisAxis.None), ct);
			var breakdownTask = QueryAsync(Clone(baseParam, byBucket: false, SelectedAxis.Value), ct);
			trend = await trendTask;
			breakdown = await breakdownTask;
		}
		catch (InvalidParameterException ex) {
			MessageEx.ShowWarningDialog(ex.Message, owner: ActiveWindow);
			Message = ex.Message;
			return;
		}
		if (version != _version) return;

		_resultParam = baseParam;
		_buckets = trend.Buckets;
		_breakdownBucket = null;
		_suppress = true;
		TrendRows = BuildTrendRows(trend);
		SelectedTrendRow = TrendRows.FirstOrDefault();
		_suppress = false;
		ApplyBreakdown(breakdown);
		HasResult = true;
		var first = _buckets.FirstOrDefault();
		var last = _buckets.LastOrDefault();
		TrendCaption = $"推移（{FilterText}）　{first?.Label} 〜 {last?.Label}　締日 {(trend.ShimeBi == 99 ? "末" : trend.ShimeBi.ToString(CultureInfo.InvariantCulture))}・期首月 {trend.FiscalStartMonth}月";
		// 操作方法は内訳表のツールチップに置き、最小幅でも切れない長さにする
		Message = $"{DateTime.Now:MM/dd HH:mm:ss} 集計しました（{(DateTime.Now - started).TotalSeconds:0.0}秒）";
	}

	/// <summary>内訳だけを取り直す（軸・並び順・推移の選択行・絞込の変更時）。</summary>
	async Task ReloadBreakdownAsync() {
		if (_resultParam == null || IsBusy) return;
		var version = _version;
		StartBusy("内訳を集計中...");
		try {
			var p = Clone(_resultParam, byBucket: false, SelectedAxis.Value);
			if (_breakdownBucket != null) {
				var key = _resultParam.Unit == CorporateAnalysisUnit.Year ? _breakdownBucket.Key[..4] : _breakdownBucket.Key;
				p.From = p.To = key;
			}
			var result = await QueryAsync(p, CancellationToken.None);
			if (version != _version) return;
			ApplyBreakdown(result);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 内訳を集計しました。";
		}
		catch (Exception ex) {
			Message = $"内訳の集計失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>推移・内訳の両方を取り直す（絞込の変更時）。</summary>
	async Task ReloadAllAsync() {
		if (_resultParam == null) return;
		var keepBucket = _breakdownBucket;
		await SearchCommand.ExecuteAsync(null);
		if (keepBucket != null && HasResult) {
			var row = TrendRows.FirstOrDefault(r => !r.IsTotal && r.BucketKey == keepBucket.Key);
			if (row != null) SelectedTrendRow = row;
		}
	}

	CorporateAnalysisParameter Clone(CorporateAnalysisParameter p, bool byBucket, CorporateAnalysisAxis axis) => new() {
		Unit = p.Unit, From = p.From, To = p.To, ByBucket = byBucket, GroupAxis = axis,
		Filters = [.. Filters.Select(f => new CorporateAnalysisFilter { Axis = f.Axis, Id = f.Id, Name = f.Name })],
		SortKey = SelectedSort.Value,
	};

	void ApplyBreakdown(CorporateAnalysisResult result) {
		var bucket = result.Buckets.FirstOrDefault();
		var days = bucket?.Days ?? 0;
		var rows = new ObservableCollection<CorporateAnalysisRowView>();
		if (result.Rows.Count > 0) {
			// 件数上限で切った場合は表示中の行だけの合計なので、推移の値と一致しないことを名前で示す
			rows.Add(CorporateAnalysisRowView.Total(result.TotalCount > result.Rows.Count ? "表示分合計" : "合計", result.Rows, days));
		}
		foreach (var r in result.Rows) {
			rows.Add(new CorporateAnalysisRowView(r, r.GroupName, days));
		}
		BreakdownRows = rows;
		SelectedBreakdownRow = null;
		var more = result.TotalCount > result.Rows.Count ? $"　※{result.TotalCount:N0}件中 上位{result.Rows.Count:N0}件（{SelectedSort.Label}）" : string.Empty;
		BreakdownCaption = $"{SelectedAxis.Label}別の内訳（{FilterText}）　{bucket?.Label}　{result.Rows.Count:N0}件{more}";
	}

	static ObservableCollection<CorporateAnalysisRowView> BuildTrendRows(CorporateAnalysisResult result) {
		var map = result.Rows.ToDictionary(r => r.BucketKey);
		var rows = new List<CorporateAnalysisRowView>();
		foreach (var b in result.Buckets) {
			var r = map.GetValueOrDefault(b.Key) ?? new CorporateAnalysisRow { BucketKey = b.Key };
			rows.Add(new CorporateAnalysisRowView(r, b.Label, b.Days));
		}
		// 先頭に期間全体の行（期首=最初の区切りの期首、期末=最後の区切りの期末、他は合計）
		var total = CorporateAnalysisRowView.Total("期間計", result.Rows, result.Buckets.Sum(b => b.Days));
		if (rows.Count > 0) {
			total.OpeningStockQty = rows[0].OpeningStockQty;
			total.OpeningStockAmount = rows[0].OpeningStockAmount;
			total.ClosingStockQty = rows[^1].ClosingStockQty;
			total.ClosingStockAmount = rows[^1].ClosingStockAmount;
			total.Recalc();
		}
		return [total, .. rows];
	}

	/// <summary>内訳の行をダブルクリック: その値で絞り込み、次の軸へ進めて取り直す。</summary>
	[RelayCommand]
	async Task DrillDown(CorporateAnalysisRowView? row) {
		row ??= SelectedBreakdownRow;
		if (row == null || row.IsTotal || !HasResult || IsBusy) return;
		var axis = SelectedAxis.Value;
		if (axis == CorporateAnalysisAxis.Shohin) {
			Message = "商品は最も細かい単位のため、これ以上絞り込めません。";
			return;
		}
		Filters.Add(new CorporateAnalysisFilter { Axis = axis, Id = row.GroupId, Name = $"{AxisLabel(axis)}:{row.Label}" });
		UpdateFilterText();
		_suppress = true;
		SelectedAxis = AxisItems.First(a => a.Value == NextAxis());
		_suppress = false;
		await ReloadAllAsync();
	}

	[RelayCommand(CanExecute = nameof(CanBack))]
	async Task Back() {
		if (Filters.Count == 0) return;
		var removed = Filters[^1];
		Filters.RemoveAt(Filters.Count - 1);
		UpdateFilterText();
		_suppress = true;
		SelectedAxis = AxisItems.First(a => a.Value == removed.Axis);
		_suppress = false;
		await ReloadAllAsync();
	}

	bool CanBack() => HasResult && Filters.Count > 0;

	[RelayCommand(CanExecute = nameof(CanBack))]
	async Task ResetFilters() {
		Filters.Clear();
		UpdateFilterText();
		_suppress = true;
		SelectedAxis = AxisItems[0];
		_suppress = false;
		await ReloadAllAsync();
	}

	CorporateAnalysisAxis NextAxis() {
		var used = Filters.Select(f => f.Axis).ToHashSet();
		return DrillOrder.FirstOrDefault(a => !used.Contains(a), CorporateAnalysisAxis.Shohin);
	}

	void UpdateFilterText() {
		FilterText = Filters.Count == 0 ? "全社" : "全社 › " + string.Join(" › ", Filters.Select(f => f.Name));
		BackCommand.NotifyCanExecuteChanged();
		ResetFiltersCommand.NotifyCanExecuteChanged();
	}

	string AxisLabel(CorporateAnalysisAxis axis) => AxisItems.FirstOrDefault(a => a.Value == axis)?.Label ?? axis.ToString();

	async Task<CorporateAnalysisResult> QueryAsync(CorporateAnalysisParameter param, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg068_CorporateAnalysis,
			DataType = typeof(CorporateAnalysisParameter),
			DataMsg = Common.SerializeObject(param),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code == CvMsgErrorCode.InvalidParameter) {
			throw new InvalidParameterException(reply.Option ?? reply.DataMsg ?? "条件が不正です。");
		}
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "全社分析の集計に失敗しました。");
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as CorporateAnalysisResult
			?? throw new InvalidOperationException("全社分析の結果を解析できませんでした。");
	}

	sealed class InvalidParameterException(string message) : Exception(message);

	// ---- CSV ----

	[RelayCommand(CanExecute = nameof(CanExportCsv))]
	void ExportCsv() {
		if (_resultParam == null) return;
		var dialog = new SaveFileDialog {
			Title = "全社分析をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"全社分析_{_resultParam.From}-{_resultParam.To}.csv",
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

	bool CanExportCsv() => HasResult;

	string BuildCsv() {
		var sb = new StringBuilder();
		void Line(params string[] fields) => sb.AppendLine(CsvText.BuildLine(fields));
		string[] Header(string first) => [first, "コード", "期首在庫数", "期首在庫金額", "期末在庫数", "期末在庫金額", "発注数", "発注金額", "受注数", "受注金額",
			"仕入数", "仕入金額", "売上数", "売上金額", "売上原価", "粗利", "粗利率(%)", "消化率(%)", "在庫回転日数", "判定"];
		string N(long v) => v.ToString(CultureInfo.InvariantCulture);
		string R(double? v) => v?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;
		string[] Values(CorporateAnalysisRowView r) => [r.Label, r.Code,
			N(r.OpeningStockQty), N(r.OpeningStockAmount), N(r.ClosingStockQty), N(r.ClosingStockAmount),
			N(r.HachuQty), N(r.HachuAmount), N(r.JuchuQty), N(r.JuchuAmount), N(r.ShiireQty), N(r.ShiireAmount),
			N(r.SalesQty), N(r.SalesAmount), N(r.SalesCost), N(r.GrossProfit),
			R(r.GrossMarginRate), R(r.SellThroughRate), R(r.TurnoverDays), r.Judgement];

		Line("全社分析");
		Line("単位", SelectedUnit.Label);
		Line("絞込", FilterText);
		Line(TrendCaption);
		Line(Header("期間"));
		foreach (var r in TrendRows) Line(Values(r));
		Line();
		Line(BreakdownCaption);
		Line(Header(SelectedAxis.Label));
		foreach (var r in BreakdownRows) Line(Values(r));
		return sb.ToString();
	}
}

/// <summary>コンボボックスの選択肢。</summary>
public sealed record Option<T>(T Value, string Label) {
	public override string ToString() => Label;
}

/// <summary>推移表・内訳表の1行（比率・判定は画面で計算する）。</summary>
public sealed class CorporateAnalysisRowView {
	public CorporateAnalysisRowView(CorporateAnalysisRow r, string label, int days) {
		BucketKey = r.BucketKey;
		GroupId = r.GroupId;
		Code = r.GroupCode;
		Label = label;
		Days = days;
		OpeningStockQty = r.OpeningStockQty;
		OpeningStockAmount = r.OpeningStockAmount;
		ClosingStockQty = r.ClosingStockQty;
		ClosingStockAmount = r.ClosingStockAmount;
		HachuQty = r.HachuQty;
		HachuAmount = r.HachuAmount;
		JuchuQty = r.JuchuQty;
		JuchuAmount = r.JuchuAmount;
		ShiireQty = r.ShiireQty;
		ShiireAmount = r.ShiireAmount;
		SalesQty = r.SalesQty;
		SalesAmount = r.SalesAmount;
		SalesCost = r.SalesCost;
		Recalc();
	}

	/// <summary>合計行。在庫・金額とも単純合計（推移表の期間計は呼び出し側で在庫を差し替える）。</summary>
	public static CorporateAnalysisRowView Total(string label, IEnumerable<CorporateAnalysisRow> rows, int days) {
		var list = rows.ToList();
		var sum = new CorporateAnalysisRow {
			OpeningStockQty = list.Sum(x => x.OpeningStockQty),
			OpeningStockAmount = list.Sum(x => x.OpeningStockAmount),
			ClosingStockQty = list.Sum(x => x.ClosingStockQty),
			ClosingStockAmount = list.Sum(x => x.ClosingStockAmount),
			HachuQty = list.Sum(x => x.HachuQty),
			HachuAmount = list.Sum(x => x.HachuAmount),
			JuchuQty = list.Sum(x => x.JuchuQty),
			JuchuAmount = list.Sum(x => x.JuchuAmount),
			ShiireQty = list.Sum(x => x.ShiireQty),
			ShiireAmount = list.Sum(x => x.ShiireAmount),
			SalesQty = list.Sum(x => x.SalesQty),
			SalesAmount = list.Sum(x => x.SalesAmount),
			SalesCost = list.Sum(x => x.SalesCost),
		};
		var total = new CorporateAnalysisRowView(sum, label, days) { IsTotal = true };
		total.Recalc();
		return total;
	}

	public bool IsTotal { get; init; }
	public string BucketKey { get; }
	public long GroupId { get; }
	public string Code { get; }
	public string Label { get; }
	public int Days { get; }
	public long OpeningStockQty { get; set; }
	public long OpeningStockAmount { get; set; }
	public long ClosingStockQty { get; set; }
	public long ClosingStockAmount { get; set; }
	public long HachuQty { get; }
	public long HachuAmount { get; }
	public long JuchuQty { get; }
	public long JuchuAmount { get; }
	public long ShiireQty { get; }
	public long ShiireAmount { get; }
	public long SalesQty { get; }
	public long SalesAmount { get; }
	public long SalesCost { get; }
	public long GrossProfit => SalesAmount - SalesCost;
	/// <summary>粗利率(%)。売上0は空</summary>
	public double? GrossMarginRate { get; private set; }
	/// <summary>消化率(%) = 売上数 ÷ (売上数 + 期末在庫数)。分母0以下は空</summary>
	public double? SellThroughRate { get; private set; }
	/// <summary>在庫回転日数 = 平均在庫金額 ÷ 売上原価 × 日数。売上原価0以下は空</summary>
	public double? TurnoverDays { get; private set; }
	public string Judgement { get; private set; } = string.Empty;

	/// <summary>比率と判定を計算し直す（在庫を差し替えた後に呼ぶ）。</summary>
	public void Recalc() {
		GrossMarginRate = SalesAmount != 0 ? Math.Round((double)GrossProfit / SalesAmount * 100, 1) : null;
		var denominator = SalesQty + ClosingStockQty;
		SellThroughRate = denominator > 0 && SalesQty >= 0 ? Math.Round((double)SalesQty / denominator * 100, 1) : null;
		var average = (OpeningStockAmount + ClosingStockAmount) / 2.0;
		TurnoverDays = SalesCost > 0 && Days > 0 ? Math.Round(average / SalesCost * Days, 1) : null;

		var marks = new List<string>();
		if (ClosingStockQty > 0 && SalesQty <= 0) marks.Add("不良在庫");
		if (HachuQty > 0 && SellThroughRate is { } st && st < CorporateAnalysisViewModel.OverOrderSellThroughPercent) marks.Add("過剰発注");
		if (TurnoverDays is { } td && td > CorporateAnalysisViewModel.OverStockTurnoverDays) marks.Add("在庫過多");
		if (SalesQty > 0 && ClosingStockQty <= 0) marks.Add("欠品注意");
		// 判定は個々の行（分類・商品）向け。合計行には出さない
		Judgement = IsTotal ? string.Empty : string.Join(" ", marks);
	}
}
