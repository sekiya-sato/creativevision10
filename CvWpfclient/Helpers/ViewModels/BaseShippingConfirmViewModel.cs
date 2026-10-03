/*
# description
BaseShippingConfirmViewModel は配分確定画面（HaibunCommitViewModel。並び順は商品順／出荷先順）の基底です。
旧CV.netの「出荷指示確定」と「出荷処理」を1画面にまとめたものです（決定 D8：確定で即伝票作成）。

配分(TranHaibun)の未完了行を一覧し、選んだ行の確定数を入れて確定します。
- 確定: 選択行を HaibunCommitParam(Id, Vdu, 確定数) でサーバへ送る。サーバは確定数を反映し、
  出荷売上／移動伝票を作って EndFlag=1（引当解除）にする。確定数0の行は全量欠品として伝票なしで完了する。
  有効在庫が1SKUでも割れる場合はサーバが1件も確定せず、割れたSKUを ShippingShortageDto[] で返す。
- 確定取消は無い（決定 D9）。訂正は作成された伝票側で行う。
- 取置配分(Kubun=6)は店舗売上へ変換する別画面で扱うため、一覧に出さない。

商品別/得意先別の違いは並び順(SortOrderSql)だけで、データ源(TranHaibun の EndFlag=0)は同じです。
サーバ側ロジックは CvDomainLogic/ShippingDb.Commit、詳細は
Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md。

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

/// <summary>配分確定の一覧1行</summary>
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

	/// <summary>入荷済み数の表示。仕入配分(区分0)だけ出し、ほかの区分は空欄（区分0は入荷済み以下でしか確定できない）</summary>
	public string ArrivedDisplay { get; set; } = string.Empty;

	/// <summary>確定できる最大数。仕入配分は min(指示数, 入荷済み)、ほかは指示数</summary>
	public int MaxCommitSu { get; set; }

	/// <summary>仕入配分(区分0)か</summary>
	public bool IsReceiptAllocation { get; set; }

	/// <summary>確定数（出荷・移動する数）。既定は指示数（全量出荷）。0〜Su に収める。0 は全量欠品</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShortSu))]
	public partial int KakuteiSu { get; set; }

	/// <summary>欠品数 = 指示数 − 確定数</summary>
	public int ShortSu => Math.Max(Su - KakuteiSu, 0);

	partial void OnKakuteiSuChanged(int value) {
		var clamped = Math.Clamp(value, 0, Math.Max(Su, 0));
		if (clamped != value) KakuteiSu = clamped;
	}

	[ObservableProperty]
	public partial bool IsChecked { get; set; }
}

/// <summary>配分確定画面の共通基底</summary>
public abstract partial class BaseShippingConfirmViewModel : BaseQueryViewModel {

	/// <summary>一覧の並び順（TranHaibun のエイリアスは h）。配分確定画面の並び順の選択から返す</summary>
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

	/// <summary>確定日 兼 生成する伝票の在庫計上日。既定は本日</summary>
	[ObservableProperty]
	public partial string KakuteiDayText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

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
		Message = Rows.Count == 0 ? "該当する配分がありません。" : $"{Rows.Count:N0} 件を取得しました。";
	}

	/// <summary>配分(TranHaibun)の未完了行を取得する（取置を除く）。並び順はサブクラス（商品別/得意先別）で変える</summary>
	async Task<List<TranHaibun>> LoadCandidatesAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [dayFrom, dayTo];
		// 取置配分は店舗売上へ変換する別経路なので、ここでは確定対象にしない。
		// 旧状態「確定済み・未出荷」(KakuteiDay有効・EndFlag=0)も未確定と同じく確定対象に含める
		var where = $"h.EndFlag = 0 AND h.Kubun <> {(int)EnumHaibun.Reservation} AND h.DenDay BETWEEN @0 AND @1";
		where += RangeEq(parameters, "soko.Code", SokoCode);
		where += RangeEq(parameters, "sh.Code", ShohinCode);
		where += RangeEq(parameters, "ten.Code", TokuiCode);
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
				// 仕入配分は入荷済みの数までしか確定できないので、初期値を入荷済みまでにする（Step 4 4.3）
				KakuteiSu = h.Kubun == (int)EnumHaibun.Hatsukai ? Math.Min(h.Su, h.ArrivedSu) : h.Su,
				MaxCommitSu = h.Kubun == (int)EnumHaibun.Hatsukai ? Math.Min(h.Su, h.ArrivedSu) : h.Su,
				IsReceiptAllocation = h.Kubun == (int)EnumHaibun.Hatsukai,
				ArrivedDisplay = h.Kubun == (int)EnumHaibun.Hatsukai ? h.ArrivedSu.ToString("N0", CultureInfo.InvariantCulture) : string.Empty,
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

	/// <summary>
	/// チェックした行を確定数で確定し、伝票を作る。有効在庫割れは1件も確定せず、割れたSKUを一覧表示する
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task ConfirmSelected(CancellationToken ct) {
		if (IsBusy) return;
		if (!TryParseDate(KakuteiDayText, out var kakuteiDay)) return;
		var targets = Rows.Where(r => r.IsChecked).ToList();
		if (targets.Count == 0) {
			MessageEx.ShowWarningDialog("確定する行を選択してください。", owner: ActiveWindow);
			return;
		}
		var shortRows = targets.Count(r => r.KakuteiSu < r.Su);
		var zeroRows = targets.Count(r => r.KakuteiSu == 0);
		// 仕入配分の未入荷分は確定すると欠品として完了し、後から入荷しても割り当たらない（Step 4 4.3）
		var unarrived = targets.Count(r => r.IsReceiptAllocation && r.MaxCommitSu < r.Su);
		var question = $"{targets.Count:N0} 件（確定数 合計 {targets.Sum(r => r.KakuteiSu):N0} 点）を確定し、出荷売上／移動伝票を作成します。"
			+ (shortRows > 0 ? $"\n欠品のある行が {shortRows:N0} 件（うち全量欠品 {zeroRows:N0} 件）あります。" : string.Empty)
			+ (unarrived > 0 ? $"\n未入荷分のある仕入配分が {unarrived:N0} 件あります。確定すると未入荷分は欠品で完了し、後から入荷しても割り当たりません。入荷を待つ場合はチェックを外してください。" : string.Empty)
			+ "\n確定後は取り消せません。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(question, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("確定中...");
		try {
			HaibunCommitRow[] rows = [.. targets.Select(r => new HaibunCommitRow(r.Id, r.Vdu, r.KakuteiSu))];
			// 入力社員は0で送り、サーバがログイン中の社員を使う
			var param = new HaibunCommitParam(rows, ToDenDay(kakuteiDay), 0);
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ShippingUnavailable) {
				ShowShortage(reply);
				return;
			}
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				Message = "他端末で更新されたため確定していません。再検索してください。";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"確定に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			var result = Common.DeserializeObject(reply.DataMsg ?? "", typeof(HaibunCommitResult)) as HaibunCommitResult;
			await OnSearchAsync(ct);
			Message = $"{result?.CommittedCount ?? targets.Count:N0} 件を確定し、伝票を {result?.CreatedSlipIds.Length ?? 0:N0} 件作成しました。"
				+ (result is { ShortageRowCount: > 0 } ? $"（欠品 {result.ShortageRowCount:N0} 件）" : string.Empty);
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) { Message = "確定を中断しました"; }
		catch (Exception ex) {
			Message = $"確定に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally { FinishBusy(); }
	}

	/// <summary>チェックした行の確定数を確定できる最大数（指示数。仕入配分は入荷済みまで）へ戻す</summary>
	[RelayCommand]
	protected void SetKakuteiToShiji() {
		foreach (var row in Rows.Where(r => r.IsChecked)) row.KakuteiSu = row.MaxCommitSu;
	}

	/// <summary>チェックした行の確定数を0（全量欠品）にする</summary>
	[RelayCommand]
	protected void SetKakuteiToZero() {
		foreach (var row in Rows.Where(r => r.IsChecked)) row.KakuteiSu = 0;
	}

	[RelayCommand]
	protected void CheckAll() => SetAllChecked(true);

	[RelayCommand]
	protected void UncheckAll() => SetAllChecked(false);

	void ShowShortage(CvMsg reply) {
		var dto = Common.DeserializeObject(reply.DataMsg ?? "[]", typeof(ShippingShortageDto[])) as ShippingShortageDto[] ?? [];
		var lines = dto.Take(30).Select(e =>
			$"倉庫{e.Id_Soko} 商品{e.Id_Shohin} 色{e.Id_Col} サイズ{e.Id_Siz}: 確定数{e.Shiji} / 有効{e.Yuko}");
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
