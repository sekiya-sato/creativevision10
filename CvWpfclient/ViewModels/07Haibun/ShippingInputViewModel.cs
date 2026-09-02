using CodeShare;
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

/// <summary>出荷処理入力の一覧1行。実数量(JitsuSu)は入力可、欠品は自動。</summary>
public sealed partial class ShippingProcessRow : ObservableObject {
	public long Id { get; set; }
	public long Vdu { get; set; }
	public string DenDay { get; set; } = string.Empty;
	public string SokoDisplay { get; set; } = string.Empty;
	public string TenpoDisplay { get; set; } = string.Empty;
	public string DenKindDisplay { get; set; } = string.Empty;
	public string ShohinDisplay { get; set; } = string.Empty;
	public string ColSizDisplay { get; set; } = string.Empty;
	/// <summary>指示数（配分数）</summary>
	public int Su { get; set; }

	/// <summary>実数量（出荷数）。既定は指示数（全量出荷）。0〜Su にクランプ。</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShortSu))]
	public partial int JitsuSu { get; set; }

	/// <summary>欠品数 = 指示数 − 実数量</summary>
	public int ShortSu => Math.Max(Su - JitsuSu, 0);

	[ObservableProperty]
	public partial bool IsChecked { get; set; }

	partial void OnJitsuSuChanged(int value) {
		var clamped = Math.Clamp(value, 0, Su);
		if (clamped != value) JitsuSu = clamped;
	}
}

/// <summary>
/// 出荷処理入力。確定済みの配分に実数量を入れ、出荷売上／移動伝票を作成して <c>EndFlag=1</c>（引当解除）にする。
/// 旧CV.netの「出荷処理」に相当し、ハンディ廃止(決定 I6)により実数量・欠品はこの画面で確定する。
/// サーバ側は <c>ShippingDb.ProcessShipping</c>、詳細は Doc/spec/archive/2026-08-18_I2I3_出荷指示確定・出荷処理_詳細設計.md。
/// </summary>
public sealed partial class ShippingInputViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "出荷処理入力";

	[ObservableProperty]
	public partial string KakuteiFromText { get; set; } = DateTime.Now.AddMonths(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string KakuteiToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCode { get; set; } = string.Empty;

	/// <summary>生成する伝票の在庫計上日。既定は本日</summary>
	[ObservableProperty]
	public partial string DenDayText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShainText { get; set; } = "（未設定）";

	long IdShain;

	[ObservableProperty]
	public partial ObservableCollection<ShippingProcessRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial int CheckedCount { get; set; }

	protected override void Init() {
		Title = QueryTitle;
		Message = "確定日の範囲を指定して［検索実行］を押してください。";
	}

	protected override void OnClearConditions() {
		KakuteiFromText = DateTime.Now.AddMonths(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		KakuteiToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		SokoCode = string.Empty;
		TokuiCode = string.Empty;
		Rows = [];
	}

	[RelayCommand]
	void SelectSoko() { var c = SelectSokoCode(); if (c != null) SokoCode = c; }

	[RelayCommand]
	void SelectTokui() { var c = SelectTokuiCode(); if (c != null) TokuiCode = c; }

	[RelayCommand]
	void SelectShain() {
		var shain = PrintPdfHelper.ShowSelectDialog<MasterShain>(this, typeof(MasterShain), "", "Code", IdShain);
		if (shain == null) return;
		IdShain = shain.Id;
		ShainText = CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(KakuteiFromText, out var from)) return;
		if (!TryParseDate(KakuteiToText, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("確定日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		var candidates = await LoadCandidatesAsync(ToDenDay(from), ToDenDay(to), maxCount, ct);
		var rows = await ComposeRowsAsync(candidates, ct);
		Rows = [.. rows];
		UpdateCounts();
		Message = Rows.Count == 0 ? "確定済み・未処理の配分がありません。" : $"{Rows.Count:N0} 件を取得しました。";
	}

	async Task<List<TranHaibun>> LoadCandidatesAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [dayFrom, dayTo];
		// 確定済み(KakuteiDay有効)かつ未処理(EndFlag=0)
		var where = "h.EndFlag = 0 AND ifnull(h.KakuteiDay,'') <> '' AND h.KakuteiDay BETWEEN @0 AND @1";
		where += RangeEq(parameters, "soko.Code", SokoCode);
		where += RangeEq(parameters, "ten.Code", TokuiCode);
		// 仮想ヘッダ(DenDay+NouhinDay+Id_Soko+Id_Tenpo+Kubun+RelateNo1)単位でまとまるよう並べる（決定 I5）
		var sql = $@"
SELECT h.*
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
WHERE {where}
ORDER BY h.DenDay, h.NouhinDay, h.Id_Soko, h.Id_Tenpo, h.Kubun, h.RelateNo1, h.Id
LIMIT {maxCount.ToString(CultureInfo.InvariantCulture)}";
		return await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
	}

	async Task<List<ShippingProcessRow>> ComposeRowsAsync(List<TranHaibun> candidates, CancellationToken ct) {
		if (candidates.Count == 0) return [];
		var tokuiMap = await LoadTokuiMapAsync(candidates.Select(x => x.Id_Soko).Concat(candidates.Select(x => x.Id_Tenpo)), ct);
		var shohinMap = await LoadShohinMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var skuMap = await LoadSkuMapAsync(candidates.Select(x => x.Id_Shohin), ct);

		return [.. candidates.Select(h => {
			var ten = tokuiMap.GetValueOrDefault(h.Id_Tenpo);
			var tenType = ten?.TenType ?? 0;
			return new ShippingProcessRow {
				Id = h.Id,
				Vdu = h.Vdu,
				DenDay = FormatDay(h.DenDay),
				SokoDisplay = FormatTokui(h.Id_Soko, tokuiMap),
				TenpoDisplay = FormatTokui(h.Id_Tenpo, tokuiMap),
				DenKindDisplay = IsShukka(tenType) ? "出荷売上" : "移動",
				ShohinDisplay = FormatShohin(h.Id_Shohin, shohinMap),
				ColSizDisplay = skuMap.GetValueOrDefault(new SkuKey(h.Id_Shohin, h.Id_Col, h.Id_Siz), $"{h.Id_Col}/{h.Id_Siz}"),
				Su = h.Su,
				// 既定は全量出荷。倉庫からの欠品連絡があれば実数量を下げる
				JitsuSu = h.Su,
			};
		})];
	}

	/// <summary>チェック行を出荷処理する。実数量で伝票を作成し EndFlag=1（引当解除）にする</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task Execute(CancellationToken ct) {
		if (IsBusy) return;
		if (!TryParseDate(DenDayText, out var denDay)) return;
		if (IdShain <= 0) {
			MessageEx.ShowWarningDialog("入力社員を選択してください。", owner: ActiveWindow);
			return;
		}
		var targets = Rows.Where(r => r.IsChecked).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog("出荷処理する行を選択してください。", owner: ActiveWindow);
			return;
		}
		var shipCount = targets.Count(r => r.JitsuSu > 0);
		var confirm = $"{targets.Count:N0} 件を出荷処理しますか。";
		if (shipCount < targets.Count) {
			confirm += $"\nうち {targets.Count - shipCount:N0} 件は実数量0（全量欠品）で、伝票を作らず完了だけ立てます。";
		}
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("出荷処理中...");
		try {
			ShippingCreateRow[] rows = [.. targets.Select(r => new ShippingCreateRow(r.Id, r.Vdu, r.JitsuSu))];
			var param = new ShippingCreateParam(rows, ToDenDay(denDay), IdShain);
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				Rows = [];
				UpdateCounts();
				Message = "他端末で更新されたため出荷処理しませんでした（1件も処理していません）。［検索実行］で再取得してください。";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"出荷処理に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			var result = Common.DeserializeObject(reply.DataMsg ?? "", typeof(ShippingCreateResult)) as ShippingCreateResult;
			await OnSearchAsync(ct);
			Message = $"{result?.CreatedSlipIds.Length ?? 0:N0} 件の伝票を作成し、{result?.ReleasedCount ?? 0:N0} 件を引当解除しました。";
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "出荷処理を中断しました"; }
		catch (Exception ex) {
			Message = $"出荷処理に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	[RelayCommand]
	void CheckAll() => SetAllChecked(true);

	[RelayCommand]
	void UncheckAll() => SetAllChecked(false);

	void SetAllChecked(bool value) {
		foreach (var row in Rows) row.IsChecked = value;
		UpdateCounts();
	}

	void UpdateCounts() => CheckedCount = Rows.Count(r => r.IsChecked);

	async Task<Dictionary<long, MasterTokui>> LoadTokuiMapAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var rows = await QuerySqlListAsync<MasterTokui>($"SELECT * FROM {nameof(MasterTokui)} WHERE Id IN ({string.Join(",", list)})", [], ct);
		return rows.ToDictionary(x => x.Id);
	}

	async Task<Dictionary<long, MasterShohin>> LoadShohinMapAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var rows = await QuerySqlListAsync<MasterShohin>($"SELECT * FROM {nameof(MasterShohin)} WHERE Id IN ({string.Join(",", list)})", [], ct);
		return rows.ToDictionary(x => x.Id);
	}

	async Task<Dictionary<SkuKey, string>> LoadSkuMapAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		var list = shohinIds.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var rows = await QuerySqlListAsync<DerivedShohinColSiz>($"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin IN ({string.Join(",", list)})", [], ct);
		var map = new Dictionary<SkuKey, string>();
		foreach (var d in rows) {
			map[new SkuKey(d.Id_Shohin, d.Id_Col, d.Id_Siz)] =
				$"{JoinCodeName(d.Code_Col, d.Mei_Col)} / {JoinCodeName(d.Code_Siz, d.Mei_Siz)}";
		}
		return map;
	}

	Task<CvMsg> SendExecuteAsync(object parameter, CancellationToken ct) =>
		CoreServiceClient.SendExecuteAsync(parameter, ct);

	static string RangeEq(List<string> parameters, string column, string? code) {
		var c = (code ?? string.Empty).Trim();
		return string.IsNullOrEmpty(c) ? string.Empty : $" AND {column} = {AddSqlParameter(parameters, c)}";
	}

	static bool IsShukka(int tenType) => tenType is 1 or 3;

	static string FormatDay(string yyyymmdd) =>
		yyyymmdd.Length == 8 ? $"{yyyymmdd[..4]}/{yyyymmdd.Substring(4, 2)}/{yyyymmdd.Substring(6, 2)}" : yyyymmdd;

	static string FormatTokui(long id, IReadOnlyDictionary<long, MasterTokui> map) =>
		map.TryGetValue(id, out var t) ? CodeNameDisplay.Format(t.Id, t.Code, t.Name) : (id == 0 ? string.Empty : $"Id:{id}");

	static string FormatShohin(long id, IReadOnlyDictionary<long, MasterShohin> map) =>
		map.TryGetValue(id, out var s) ? CodeNameDisplay.Format(s.Id, s.Code, s.Name) : $"Id:{id}";

	static string JoinCodeName(string? code, string? name) {
		var cd = (code ?? string.Empty).Trim();
		var mei = (name ?? string.Empty).Trim();
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}

	readonly record struct SkuKey(long IdShohin, long IdCol, long IdSiz);
}
