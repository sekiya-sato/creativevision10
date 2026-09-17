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
using System.Windows;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>滞留・欠品例外の一覧1行（配分 TranHaibun をラップ）</summary>
public sealed partial class ShippingStagnationRow : ObservableObject {
	public long Id { get; set; }
	public long Vdu { get; set; }
	public string KakuteiDayDisplay { get; set; } = string.Empty;
	public string NouhinDayDisplay { get; set; } = string.Empty;
	/// <summary>確定日からの経過日数（今日 − 確定日）</summary>
	public int ElapsedDays { get; set; }
	/// <summary>納品予定日を過ぎているか</summary>
	public bool IsOverdue { get; set; }
	public string OverdueDisplay => IsOverdue ? "予定日超過" : string.Empty;
	public string SokoDisplay { get; set; } = string.Empty;
	public string TenpoDisplay { get; set; } = string.Empty;
	public string DenKindDisplay { get; set; } = string.Empty;
	public string ShohinDisplay { get; set; } = string.Empty;
	public string ColSizDisplay { get; set; } = string.Empty;
	/// <summary>指示数（配分数）</summary>
	public int Su { get; set; }
	/// <summary>実数量（欠品実績モードで表示）</summary>
	public int JitsuSu { get; set; }
	/// <summary>欠品数（欠品実績モードで表示）</summary>
	public int ShortSu { get; set; }

	[ObservableProperty]
	public partial bool IsChecked { get; set; }
}

/// <summary>
/// 滞留・欠品例外画面。確定済みなのに出荷処理されず放置された配分（滞留）を検出し、
/// 例外操作（確定取消／強制完了）を行う。欠品実績の照会も兼ねる。
/// <para>
/// 旧CV.netの「出荷指示一覧（確定済みかつ未完了の滞留を検出）」に相当する。仕様と決定は
/// `Doc/spec/archive/2026-08-18_I7_滞留・欠品例外_詳細設計.md` を参照する。サーバは I2/I3 の
/// `ShippingCancelParam`（確定取消）／`ShippingCreateParam`（実数量0＝全量欠品で強制完了）を再利用し、変更しない。
/// </para>
/// </summary>
public partial class ShippingConfirmListViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "滞留・欠品例外";

	public IReadOnlyList<string> ViewKinds { get; } = ["滞留", "欠品実績"];

	[ObservableProperty]
	public partial string ViewKind { get; set; } = "滞留";

	bool IsStagnation => ViewKind == "滞留";

	[ObservableProperty]
	public partial string KakuteiFromText { get; set; } = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string KakuteiToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCode { get; set; } = string.Empty;

	/// <summary>滞留とみなす確定日からの経過日数（既定3日）</summary>
	[ObservableProperty]
	public partial string StagnationDaysText { get; set; } = "3";

	/// <summary>納品予定日超過だけに絞る（滞留モード）</summary>
	[ObservableProperty]
	public partial bool OverdueOnly { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<ShippingStagnationRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial int CheckedCount { get; set; }

	protected override void Init() {
		Title = QueryTitle;
		Message = "確定日の範囲・滞留日数を指定して［検索実行］を押してください。";
	}

	protected override void OnClearConditions() {
		KakuteiFromText = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		KakuteiToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		SokoCode = string.Empty;
		TokuiCode = string.Empty;
		StagnationDaysText = "3";
		OverdueOnly = false;
		ViewKind = "滞留";
		DetachRows(Rows);
		Rows = [];
		UpdateCounts();
	}

	[RelayCommand]
	void SelectSoko() { var c = SelectSokoCode(); if (c != null) SokoCode = c; }

	[RelayCommand]
	void SelectTokui() { var c = SelectTokuiCode(); if (c != null) TokuiCode = c; }

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
		DetachRows(Rows);
		Rows = [.. rows];
		AttachRows(Rows);
		UpdateCounts();
		Message = Rows.Count == 0
			? (IsStagnation ? "該当する滞留がありません。" : "該当する欠品実績がありません。")
			: $"{Rows.Count:N0} 件を取得しました。（{ViewKind}）";
	}

	async Task<List<TranHaibun>> LoadCandidatesAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		var (where, parameters) = BuildWhere(dayFrom, dayTo);
		var sql = $@"
SELECT h.*
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
WHERE {where}
ORDER BY h.KakuteiDay, h.Id_Soko, h.Id_Tenpo, h.Id
LIMIT {maxCount.ToString(CultureInfo.InvariantCulture)}";
		return await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
	}

	/// <summary>
	/// 検索条件からWHERE句とバインドパラメータを組み立てる。画面一覧の検索(<see cref="LoadCandidatesAsync"/>)と
	/// PDF印刷(<see cref="BuildPrintSqlParam"/>)の両方で共用し、SQLの二重定義を避ける。
	/// </summary>
	(string where, List<string> parameters) BuildWhere(string dayFrom, string dayTo) {
		var todayYmd = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		List<string> parameters = [dayFrom, dayTo];
		string where;
		if (IsStagnation) {
			// 確定済み・未処理（滞留候補）
			where = "h.EndFlag = 0 AND ifnull(h.KakuteiDay,'') <> '' AND h.KakuteiDay BETWEEN @0 AND @1";
			if (OverdueOnly) {
				where += $" AND ifnull(h.NouhinDay,'') <> '' AND h.NouhinDay < {AddSqlParameter(parameters, todayYmd)}";
			}
			else {
				// 経過日数≥閾値（確定日 ≤ 今日−閾値）または 納品予定日超過
				var days = ParseStagnationDays();
				var thresholdYmd = DateTime.Today.AddDays(-days).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
				var th = AddSqlParameter(parameters, thresholdYmd);
				var today = AddSqlParameter(parameters, todayYmd);
				where += $" AND (h.KakuteiDay <= {th} OR (ifnull(h.NouhinDay,'') <> '' AND h.NouhinDay < {today}))";
			}
		}
		else {
			// 欠品実績（完了かつ欠品）
			where = "h.EndFlag = 1 AND h.ShortSu > 0 AND h.KakuteiDay BETWEEN @0 AND @1";
		}
		where += RangeEq(parameters, "soko.Code", SokoCode);
		where += RangeEq(parameters, "ten.Code", TokuiCode);
		return (where, parameters);
	}

	async Task<List<ShippingStagnationRow>> ComposeRowsAsync(List<TranHaibun> candidates, CancellationToken ct) {
		if (candidates.Count == 0) return [];
		var tokuiMap = await LoadTokuiMapAsync(candidates.Select(x => x.Id_Soko).Concat(candidates.Select(x => x.Id_Tenpo)), ct);
		var shohinMap = await LoadShohinMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var skuMap = await LoadSkuMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var today = DateTime.Today;

		return [.. candidates.Select(h => {
			var ten = tokuiMap.GetValueOrDefault(h.Id_Tenpo);
			var tenType = ten?.TenType ?? 0;
			return new ShippingStagnationRow {
				Id = h.Id,
				Vdu = h.Vdu,
				KakuteiDayDisplay = FormatDay(h.KakuteiDay),
				NouhinDayDisplay = FormatDay(h.NouhinDay),
				ElapsedDays = ElapsedDaysFrom(h.KakuteiDay, today),
				IsOverdue = IsOverdueDay(h.NouhinDay, today),
				SokoDisplay = FormatTokui(h.Id_Soko, tokuiMap),
				TenpoDisplay = FormatTokui(h.Id_Tenpo, tokuiMap),
				DenKindDisplay = IsShukka(tenType) ? "出荷売上" : "移動",
				ShohinDisplay = FormatShohin(h.Id_Shohin, shohinMap),
				ColSizDisplay = skuMap.GetValueOrDefault(new SkuKey(h.Id_Shohin, h.Id_Col, h.Id_Siz), $"{h.Id_Col}/{h.Id_Siz}"),
				Su = h.Su,
				JitsuSu = h.JitsuSu,
				ShortSu = h.ShortSu,
			};
		})];
	}

	/// <summary>チェックした滞留の確定を取り消す（未確定へ戻し再指示できるようにする）</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task CancelConfirm(CancellationToken ct) {
		if (IsBusy || !IsStagnation) return;
		var targets = Rows.Where(r => r.IsChecked).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog("確定取消する行を選択してください。", owner: ActiveWindow);
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{targets.Count:N0} 件の確定を取り消しますか。（未確定へ戻ります）", owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("確定取消中...");
		try {
			var param = new ShippingCancelParam([.. targets.Select(r => r.Id)]);
			var reply = await SendExecuteAsync(param, ct);
			if (HandleError(reply, "確定取消")) return;
			var result = Common.DeserializeObject(reply.DataMsg ?? "", typeof(ShippingCancelResult)) as ShippingCancelResult;
			await OnSearchAsync(ct);
			Message = $"{result?.CanceledCount ?? 0:N0} 件の確定を取り消しました。";
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "確定取消を中断しました"; }
		catch (Exception ex) {
			Message = $"確定取消に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>チェックした滞留を強制完了する（出荷せず全量欠品で EndFlag=1・引当解除）</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task ForceComplete(CancellationToken ct) {
		if (IsBusy || !IsStagnation) return;
		var targets = Rows.Where(r => r.IsChecked).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog("強制完了する行を選択してください。", owner: ActiveWindow);
			return;
		}
		if (MessageEx.ShowQuestionDialog(
			$"{targets.Count:N0} 件を強制完了しますか。\n出荷せず完了（全量欠品）にし、引当を解除します。取り消せません。",
			owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("強制完了中...");
		try {
			// 実数量0で出荷処理する。ProcessShipping は伝票を作らず EndFlag=1・引当解除だけ行う
			ShippingCreateRow[] rows = [.. targets.Select(r => new ShippingCreateRow(r.Id, r.Vdu, 0))];
			var param = new ShippingCreateParam(rows, ToDenDay(DateTime.Today), 0);
			var reply = await SendExecuteAsync(param, ct);
			if (HandleError(reply, "強制完了")) return;
			var result = Common.DeserializeObject(reply.DataMsg ?? "", typeof(ShippingCreateResult)) as ShippingCreateResult;
			await OnSearchAsync(ct);
			Message = $"{result?.ReleasedCount ?? 0:N0} 件を強制完了し、引当を解除しました（伝票は作成していません）。";
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "強制完了を中断しました"; }
		catch (Exception ex) {
			Message = $"強制完了に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>表示中の一覧をCSV(UTF-8 BOM)へ書き出す。PDF帳票は別途 qfm が要るためCSVで代替（I7 follow-up）。</summary>
	[RelayCommand]
	void ExportCsv() {
		if (Rows.Count == 0) {
			MessageEx.ShowWarningDialog("出力する明細がありません。先に検索してください。", owner: ActiveWindow);
			return;
		}
		var dialog = new SaveFileDialog {
			Title = $"{ViewKind}一覧をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"滞留欠品例外_{DateTime.Today:yyyyMMdd}.csv",
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

	string BuildCsv() {
		string[] headers = ["確定日", "納品予定日", "経過日数", "予定日超過", "倉庫", "出荷先", "種別", "商品", "色/サイズ", "指示数", "実数量", "欠品"];
		var sb = new StringBuilder();
		sb.AppendLine(string.Join(",", headers.Select(CsvField)));
		foreach (var r in Rows) {
			string[] cells = [
				r.KakuteiDayDisplay, r.NouhinDayDisplay, r.ElapsedDays.ToString(CultureInfo.InvariantCulture),
				r.IsOverdue ? "超過" : "", r.SokoDisplay, r.TenpoDisplay, r.DenKindDisplay,
				r.ShohinDisplay, r.ColSizDisplay,
				r.Su.ToString(CultureInfo.InvariantCulture),
				r.JitsuSu.ToString(CultureInfo.InvariantCulture),
				r.ShortSu.ToString(CultureInfo.InvariantCulture),
			];
			sb.AppendLine(string.Join(",", cells.Select(CsvField)));
		}
		return sb.ToString();
	}

	static string CsvField(string value) {
		var v = (value ?? string.Empty).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
		return v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
	}

	/// <summary>印刷フォーム(qfm)ファイル名</summary>
	static string FormFile => "ShippingStagnationList.qfm";

	/// <summary>
	/// 表示中の検索条件でPDF帳票(倉庫→出荷先の2段グループ・小計、総合計)を出力する。
	/// <para>
	/// 0件のときに空白PDFを出さないよう、ExportCsv と同じ「先に検索してください」ガードで印刷自体を中止する
	/// （画面の Rows が現在の検索条件に対する最新の結果である前提）。qfm 側には0件時の専用メッセージは実装していない。
	/// </para>
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoOutputPdf(CancellationToken ct) {
		if (IsBusy) return;
		if (Rows.Count == 0) {
			MessageEx.ShowWarningDialog("出力する明細がありません。先に検索してください。", owner: ActiveWindow);
			return;
		}
		if (!TryParseDate(KakuteiFromText, out var from)) return;
		if (!TryParseDate(KakuteiToText, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("確定日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}

		StartBusy("PDF出力中...");
		try {
			var sqlParam = BuildPrintSqlParam(from, to);
			await PrintPdfHelper.RunPrintPdfAsync(this, ActiveWindow, m => Message = m, FormFile, null, sqlParam, ct);
		}
		catch (OperationCanceledException) {
			Message = "PDF出力を中断しました";
		}
		catch (Exception ex) {
			Message = $"PDF出力に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>
	/// 印刷用SQLを組み立てる。WHERE句は画面検索(<see cref="BuildWhere"/>)を再利用し、絞込結果を一致させる。
	/// <para>
	/// 並び順のみ画面（KakuteiDay, Id_Soko, Id_Tenpo, Id）と異なり、倉庫→出荷先を先頭に置く
	/// （<c>Id_Soko, Id_Tenpo, KakuteiDay, Id</c>）。qfmのグループ小計は「キー変化での区切り」で動くため、
	/// 画面と同じ確定日優先の並びのままでは同じ倉庫/出荷先が日付をまたいで何度も現れるたびに小計が分断され、
	/// 「倉庫ごとの合計」という小計本来の意味にならない。この並び順変更は判断が必要な点として作業ログ・報告に明記する。
	/// </para>
	/// </summary>
	QueryListSqlParam BuildPrintSqlParam(DateTime from, DateTime to) {
		var (where, parameters) = BuildWhere(ToDenDay(from), ToDenDay(to));
		var todayYmd = AddSqlParameter(parameters, DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
		var todayIso = AddSqlParameter(parameters, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		var condition = AddSqlParameter(parameters, BuildConditionText(from, to));

		// SELECT列順・別名は ShippingStagnationList.qfm の item1..item13 と厳密に一致させる。
		// item1..12 は画面CSV(BuildCsv)と同じ12列。item13(ConditionDisp)はqfm側の帳票ヘッダ(抽出条件表示)専用の追加列で、
		// CSVには存在しない（qfmには実行時に決まる値を渡す手段が無いため、全行へ同じ値を乗せている）。
		var sql = $@"
SELECT
substr(h.KakuteiDay,1,4) || '/' || substr(h.KakuteiDay,5,2) || '/' || substr(h.KakuteiDay,7,2) KakuteiDayDisp,
CASE WHEN ifnull(h.NouhinDay,'') = '' THEN '' ELSE substr(h.NouhinDay,1,4) || '/' || substr(h.NouhinDay,5,2) || '/' || substr(h.NouhinDay,7,2) END NouhinDayDisp,
CAST(MAX(CAST(julianday({todayIso}) - julianday(substr(h.KakuteiDay,1,4) || '-' || substr(h.KakuteiDay,5,2) || '-' || substr(h.KakuteiDay,7,2)) AS INTEGER), 0) AS TEXT) ElapsedDaysDisp,
CASE WHEN ifnull(h.NouhinDay,'') <> '' AND h.NouhinDay < {todayYmd} THEN '超過' ELSE '' END OverdueDisp,
{CodeNameDisplay.Sql("soko.Id", "soko.Code", "soko.Name")} SokoDisp,
{CodeNameDisplay.Sql("ten.Id", "ten.Code", "ten.Name")} TenpoDisp,
CASE WHEN ten.TenType IN (1,3) THEN '出荷売上' ELSE '移動' END DenKindDisp,
{CodeNameDisplay.Sql("sh.Id", "sh.Code", "sh.Name")} ShohinDisp,
trim(trim(ifnull(sku.Code_Col,'') || ' ' || ifnull(sku.Mei_Col,'')) || ' / ' || trim(ifnull(sku.Code_Siz,'') || ' ' || ifnull(sku.Mei_Siz,''))) ColSizDisp,
h.Su Su,
h.JitsuSu JitsuSu,
h.ShortSu ShortSu,
{condition} ConditionDisp
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
LEFT JOIN {nameof(MasterShohin)} sh ON sh.Id = h.Id_Shohin
LEFT JOIN {nameof(DerivedShohinColSiz)} sku ON sku.Id_Shohin = h.Id_Shohin AND sku.Id_Col = h.Id_Col AND sku.Id_Siz = h.Id_Siz
WHERE {where}
ORDER BY h.Id_Soko, h.Id_Tenpo, h.KakuteiDay, h.Id";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	string BuildConditionText(DateTime from, DateTime to) {
		var overdue = IsStagnation && OverdueOnly ? "（予定日超過のみ）" : string.Empty;
		var soko = string.IsNullOrWhiteSpace(SokoCode) ? "指定なし" : SokoCode.Trim();
		var tokui = string.IsNullOrWhiteSpace(TokuiCode) ? "指定なし" : TokuiCode.Trim();
		return $"モード:{ViewKind}{overdue} 対象期間:{from:yyyy/MM/dd}〜{to:yyyy/MM/dd} 倉庫:{soko} 出荷先:{tokui}";
	}

	[RelayCommand]
	void CheckAll() => SetAllChecked(true);

	[RelayCommand]
	void UncheckAll() => SetAllChecked(false);

	/// <summary>エラーなら true。競合は一覧を破棄して再取得を促す。</summary>
	bool HandleError(CvMsg reply, string action) {
		if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
			DetachRows(Rows);
			Rows = [];
			UpdateCounts();
			Message = $"他端末で更新されたため{action}しませんでした（1件も更新していません）。［検索実行］で再取得してください。";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
			return true;
		}
		if (reply.Code < 0) {
			var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
			Message = $"{action}に失敗しました。{detail}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
			return true;
		}
		return false;
	}

	void SetAllChecked(bool value) {
		foreach (var row in Rows) row.IsChecked = value;
		UpdateCounts();
	}

	int ParseStagnationDays() =>
		int.TryParse(StagnationDaysText.Trim(), out var d) && d >= 0 ? d : 3;

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

	void AttachRows(IEnumerable<ShippingStagnationRow> rows) {
		foreach (var row in rows) row.PropertyChanged += OnRowPropertyChanged;
	}

	void DetachRows(IEnumerable<ShippingStagnationRow> rows) {
		foreach (var row in rows) row.PropertyChanged -= OnRowPropertyChanged;
	}

	void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(ShippingStagnationRow.IsChecked)) UpdateCounts();
	}

	void UpdateCounts() => CheckedCount = Rows.Count(r => r.IsChecked);

	static int ElapsedDaysFrom(string kakuteiYmd, DateTime today) =>
		DateTime.TryParseExact(kakuteiYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? Math.Max((today - d.Date).Days, 0) : 0;

	static bool IsOverdueDay(string nouhinYmd, DateTime today) =>
		DateTime.TryParseExact(nouhinYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			&& d.Date < today;

	static string RangeEq(List<string> parameters, string column, string? code) {
		var c = (code ?? string.Empty).Trim();
		return string.IsNullOrEmpty(c) ? string.Empty : $" AND {column} = {AddSqlParameter(parameters, c)}";
	}

	static bool IsShukka(int tenType) => tenType is 1 or 3;

	static string FormatDay(string yyyymmdd) =>
		yyyymmdd is { Length: 8 } ? $"{yyyymmdd[..4]}/{yyyymmdd.Substring(4, 2)}/{yyyymmdd.Substring(6, 2)}" : yyyymmdd;

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
