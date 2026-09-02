/*
# description
BaseShippingConfirmViewModel は出荷指示確定画面（出荷指示確定(商品) / 出荷指示確定(得意先)）の共通基底です。
旧CV.netの「出荷指示確定」に相当します。

配分(TranHaibun)の未完了行を一覧し、選んだ行を確定(KakuteiDayを立てる)または確定取消します。
- 確定: 未確定(KakuteiDay空)の選択行を ShippingConfirmParam でサーバへ送る。有効在庫（実在庫 − 引当数）が
  1SKUでも負になる場合はサーバが1件も確定せず、割れたSKUを ShippingShortageDto[] で返す。
- 取消: 確定済み(伝票未作成)の選択行を ShippingCancelParam で KakuteiDay=空 へ戻す。

商品別/得意先別の違いは並び順(SortOrderSql)だけで、データ源(TranHaibun の EndFlag=0)は同じです。
サーバ側ロジックは CvDomainLogic/ShippingDb、詳細は Doc/spec/archive/2026-08-18_I2I3_出荷指示確定・出荷処理_詳細設計.md。

一覧の列は既存の照会画面(ZaikoQuery)と同じく、テーブル単位に型付きで取得してクライアントで合成します
（サーバの QueryListSqlParam はDBマップ型しか返せないため、クライアント専用POCOは使いません）。
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.Helpers;

/// <summary>出荷指示確定の一覧1行</summary>
public sealed partial class ShippingConfirmRow : ObservableObject {
	public long Id { get; set; }
	public long Vdu { get; set; }
	public string DenDay { get; set; } = string.Empty;
	public string NouhinDay { get; set; } = string.Empty;
	public string SokoDisplay { get; set; } = string.Empty;
	public string TenpoDisplay { get; set; } = string.Empty;
	/// <summary>伝票種別（出荷売上 / 移動）。出荷先の店種区分で決まる（決定 I4）</summary>
	public string DenKindDisplay { get; set; } = string.Empty;
	public string ShohinDisplay { get; set; } = string.Empty;
	public string ColSizDisplay { get; set; } = string.Empty;
	/// <summary>指示数（配分数）</summary>
	public int Su { get; set; }
	/// <summary>参考: 確定前の有効在庫（実在庫 − 引当数）</summary>
	public int Yuko { get; set; }
	/// <summary>確定済みか（KakuteiDayが有効）</summary>
	public bool IsConfirmed { get; set; }
	public string StatusDisplay => IsConfirmed ? "確定済み" : "未確定";

	[ObservableProperty]
	public partial bool IsChecked { get; set; }
}

/// <summary>出荷指示確定画面の共通基底</summary>
public abstract partial class BaseShippingConfirmViewModel : BaseQueryViewModel {

	/// <summary>一覧の並び順。商品別 / 得意先別で上書きする（TranHaibun のエイリアスは h）</summary>
	protected abstract string SortOrderSql { get; }

	[ObservableProperty]
	public partial string DenDayFromText { get; set; } = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenDayToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string SokoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCode { get; set; } = string.Empty;

	/// <summary>確定日（確定実行に使う）。既定は本日</summary>
	[ObservableProperty]
	public partial string KakuteiDayText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	public IReadOnlyList<string> ViewKinds { get; } = ["未確定のみ", "確定済みも表示"];

	[ObservableProperty]
	public partial string ViewKind { get; set; } = "未確定のみ";

	[ObservableProperty]
	public partial ObservableCollection<ShippingConfirmRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial int CheckedCount { get; set; }

	protected override void Init() {
		Title = QueryTitle;
		Message = "指示日の範囲を指定して［検索実行］を押してください。";
	}

	protected override void OnClearConditions() {
		DenDayFromText = DateTime.Now.AddMonths(-3).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DenDayToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		SokoCode = string.Empty;
		ShohinCode = string.Empty;
		TokuiCode = string.Empty;
		ViewKind = "未確定のみ";
		DetachRows(Rows);
		Rows = [];
		UpdateCounts();
	}

	[RelayCommand]
	protected void SelectSoko() { var c = SelectSokoCode(); if (c != null) SokoCode = c; }

	[RelayCommand]
	protected void SelectShohin() { var c = SelectShohinCode(); if (c != null) ShohinCode = c; }

	[RelayCommand]
	protected void SelectTokui() { var c = SelectTokuiCode(); if (c != null) TokuiCode = c; }

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFromText, out var from)) return;
		if (!TryParseDate(DenDayToText, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("指示日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		var candidates = await LoadCandidatesAsync(ToDenDay(from), ToDenDay(to), maxCount, ct);
		var rows = await ComposeRowsAsync(candidates, ct);
		DetachRows(Rows);
		Rows = [.. rows];
		AttachRows(Rows);
		UpdateCounts();
		Message = Rows.Count == 0 ? "該当する配分がありません。" : $"{Rows.Count:N0} 件を取得しました。（{ViewKind}）";
	}

	/// <summary>配分(TranHaibun)の未完了行を取得する。並び順はサブクラス（商品別/得意先別）で変える</summary>
	async Task<List<TranHaibun>> LoadCandidatesAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [dayFrom, dayTo];
		var where = "h.EndFlag = 0 AND h.DenDay BETWEEN @0 AND @1";
		where += RangeEq(parameters, "soko.Code", SokoCode);
		where += RangeEq(parameters, "sh.Code", ShohinCode);
		where += RangeEq(parameters, "ten.Code", TokuiCode);
		// 「未確定のみ」は KakuteiDay 空の行だけ。確定取消も見たいときは「確定済みも表示」
		if (ViewKind == "未確定のみ") {
			where += " AND ifnull(h.KakuteiDay,'') = ''";
		}
		var sql = $@"
SELECT h.*
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterShohin)} sh ON sh.Id = h.Id_Shohin
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
WHERE {where}
ORDER BY {SortOrderSql}
LIMIT {maxCount.ToString(CultureInfo.InvariantCulture)}";
		return await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
	}

	/// <summary>取得した配分に、倉庫・出荷先・商品・色サイズ・有効在庫の表示を付ける</summary>
	async Task<List<ShippingConfirmRow>> ComposeRowsAsync(List<TranHaibun> candidates, CancellationToken ct) {
		if (candidates.Count == 0) return [];

		var tokuiIds = candidates.Select(x => x.Id_Soko).Concat(candidates.Select(x => x.Id_Tenpo));
		var tokuiMap = await LoadTokuiMapAsync(tokuiIds, ct);
		var shohinMap = await LoadShohinMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var skuMap = await LoadSkuMapAsync(candidates.Select(x => x.Id_Shohin), ct);
		var yukoMap = await LoadYukoMapAsync(candidates, ct);

		return [.. candidates.Select(h => {
			var ten = tokuiMap.GetValueOrDefault(h.Id_Tenpo);
			var tenType = ten?.TenType ?? 0;
			return new ShippingConfirmRow {
				Id = h.Id,
				Vdu = h.Vdu,
				DenDay = FormatDay(h.DenDay),
				NouhinDay = FormatDay(h.NouhinDay),
				SokoDisplay = FormatTokui(h.Id_Soko, tokuiMap),
				TenpoDisplay = FormatTokui(h.Id_Tenpo, tokuiMap),
				DenKindDisplay = IsShukka(tenType) ? "出荷売上" : "移動",
				ShohinDisplay = FormatShohin(h.Id_Shohin, shohinMap),
				ColSizDisplay = skuMap.GetValueOrDefault(new SkuKey(h.Id_Shohin, h.Id_Col, h.Id_Siz), $"{h.Id_Col}/{h.Id_Siz}"),
				Su = h.Su,
				Yuko = yukoMap.GetValueOrDefault(new SkuKey2(h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz)),
				IsConfirmed = !string.IsNullOrEmpty(h.KakuteiDay),
			};
		})];
	}

	async Task<Dictionary<long, MasterTokui>> LoadTokuiMapAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var sql = $"SELECT * FROM {nameof(MasterTokui)} WHERE Id IN ({string.Join(",", list)})";
		var rows = await QuerySqlListAsync<MasterTokui>(sql, [], ct);
		return rows.ToDictionary(x => x.Id);
	}

	async Task<Dictionary<long, MasterShohin>> LoadShohinMapAsync(IEnumerable<long> ids, CancellationToken ct) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var sql = $"SELECT * FROM {nameof(MasterShohin)} WHERE Id IN ({string.Join(",", list)})";
		var rows = await QuerySqlListAsync<MasterShohin>(sql, [], ct);
		return rows.ToDictionary(x => x.Id);
	}

	async Task<Dictionary<SkuKey, string>> LoadSkuMapAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		var list = shohinIds.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		var sql = $"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin IN ({string.Join(",", list)})";
		var rows = await QuerySqlListAsync<DerivedShohinColSiz>(sql, [], ct);
		var map = new Dictionary<SkuKey, string>();
		foreach (var d in rows) {
			map[new SkuKey(d.Id_Shohin, d.Id_Col, d.Id_Siz)] =
				$"{JoinCodeName(d.Code_Col, d.Mei_Col)} / {JoinCodeName(d.Code_Siz, d.Mei_Siz)}";
		}
		return map;
	}

	/// <summary>有効在庫 = SummaryRealStock.Su − ReserveQty を倉庫×SKUで引く</summary>
	async Task<Dictionary<SkuKey2, int>> LoadYukoMapAsync(IEnumerable<TranHaibun> candidates, CancellationToken ct) {
		var shohinIds = candidates.Select(x => x.Id_Shohin).Where(x => x > 0).Distinct().ToList();
		if (shohinIds.Count == 0) return [];
		var sql = $"SELECT * FROM {nameof(SummaryRealStock)} WHERE Id_Shohin IN ({string.Join(",", shohinIds)})";
		var rows = await QuerySqlListAsync<SummaryRealStock>(sql, [], ct);
		return rows.ToDictionary(x => new SkuKey2(x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz), x => x.Su - x.ReserveQty);
	}

	/// <summary>チェックした未確定行を確定する。有効在庫割れは1件も確定せず、割れたSKUを一覧表示する</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task ConfirmSelected(CancellationToken ct) {
		if (IsBusy) return;
		if (!TryParseDate(KakuteiDayText, out var kakuteiDay)) return;
		var targets = Rows.Where(r => r.IsChecked && !r.IsConfirmed).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog("確定する未確定の行を選択してください。", owner: ActiveWindow);
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{targets.Count:N0} 件を確定しますか。", owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("確定中...");
		try {
			var param = new ShippingConfirmParam([.. targets.Select(r => r.Id)], ToDenDay(kakuteiDay));
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ShippingUnavailable) {
				ShowShortage(reply);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"確定に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			await OnSearchAsync(ct);
			Message = $"{targets.Count:N0} 件を確定しました。";
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "確定を中断しました"; }
		catch (Exception ex) {
			Message = $"確定に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>チェックした確定済み行の確定を取り消す（伝票作成済みはサーバ側で対象外）</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task CancelSelected(CancellationToken ct) {
		if (IsBusy) return;
		var targets = Rows.Where(r => r.IsChecked && r.IsConfirmed).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog("取消する確定済みの行を選択してください。", owner: ActiveWindow);
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{targets.Count:N0} 件の確定を取り消しますか。", owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("確定取消中...");
		try {
			var param = new ShippingCancelParam([.. targets.Select(r => r.Id)]);
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"確定取消に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
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

	[RelayCommand]
	protected void CheckAll() => SetAllChecked(true);

	[RelayCommand]
	protected void UncheckAll() => SetAllChecked(false);

	void ShowShortage(CvMsg reply) {
		var dto = Common.DeserializeObject(reply.DataMsg ?? "[]", typeof(ShippingShortageDto[])) as ShippingShortageDto[] ?? [];
		var lines = dto.Take(30).Select(e =>
			$"倉庫{e.Id_Soko} 商品{e.Id_Shohin} 色{e.Id_Col} サイズ{e.Id_Siz}: 指示{e.Shiji} / 有効{e.Yuko}");
		var more = dto.Length > 30 ? $"\n… 他 {dto.Length - 30} 件" : string.Empty;
		Message = $"有効在庫が不足しているため1件も確定していません（{dto.Length} SKU）。";
		MessageEx.ShowErrorDialog(Message + "\n\n" + string.Join("\n", lines) + more, owner: ActiveWindow);
	}

	void SetAllChecked(bool value) {
		foreach (var row in Rows) row.IsChecked = value;
		UpdateCounts();
	}

	Task<CvMsg> SendExecuteAsync(object parameter, CancellationToken ct) =>
		CoreServiceClient.SendExecuteAsync(parameter, ct);

	void AttachRows(IEnumerable<ShippingConfirmRow> rows) {
		foreach (var row in rows) row.PropertyChanged += OnRowPropertyChanged;
	}

	void DetachRows(IEnumerable<ShippingConfirmRow> rows) {
		foreach (var row in rows) row.PropertyChanged -= OnRowPropertyChanged;
	}

	void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(ShippingConfirmRow.IsChecked)) UpdateCounts();
	}

	void UpdateCounts() => CheckedCount = Rows.Count(r => r.IsChecked);

	static string RangeEq(List<string> parameters, string column, string? code) {
		var c = (code ?? string.Empty).Trim();
		return string.IsNullOrEmpty(c) ? string.Empty : $" AND {column} = {AddSqlParameter(parameters, c)}";
	}

	/// <summary>出荷売上とみなす店種区分。1=卸先 / 3=売仕店（決定 I4 / G4）。サーバの ShippingDb.IsShukka と揃える</summary>
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

	protected readonly record struct SkuKey(long IdShohin, long IdCol, long IdSiz);
	protected readonly record struct SkuKey2(long IdSoko, long IdShohin, long IdCol, long IdSiz);
}
