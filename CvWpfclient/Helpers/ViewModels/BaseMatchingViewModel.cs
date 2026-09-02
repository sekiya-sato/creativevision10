/*
# description
BaseMatchingViewModel は消込画面（入金消込 / 支払消込）の共通基底クラスです。

消込とは、売上又は仕入伝票を**伝票単位で決済済みの目印をつける処理**です。
`Tran00Uriage.EndFlag` / `Tran03Shiire.EndFlag` に 1 を立て、元帳の印字時に `*` を付けます。
入金・支払との個別対応、充当金額、未充当金額は保持しません（部分消込は仕様対象外）。
仕様は `Doc/spec/archive/2026-08-12_phase1_業務仕様決定ドラフト.md` 2.1 / 2.1.1 / 2.1.2 を参照してください。

画面は次の2段構成です。
- `一覧取得`: 請求先(必須)配下の伝票を掛計上日で、入金/支払を支払日で取得し、
  伝票一覧（消込Flg付き）と入金/支払の区分別集計を並べて合計金額を比較できるようにする。
- `消込実行`: 一覧取得時点からCheckBoxが変化した伝票だけを `EndFlag` へ書き戻す。

消込は残高計算へ影響しません。売掛・買掛残高は伝票金額ベースで、`SummaryUriKake` /
`SummaryKaiKake` の値は `EndFlag` の有無で変わりません。

【旧実装からの変更】FIFO自動充当（`ApplyFifoAllocation` / `Allocated` / `AutoMatch` / `ClearMatch`）と
`.omo/2026-07-31_kesikomi_design.md` の `TranKesikomi` 新設案は不採用となり、本クラスから廃止しました。

# example
public partial class NyukinMatchingViewModel : Helpers.BaseMatchingViewModel<Tran00Uriage, Tran06Nyukin> {
	protected override string QueryTitle => "入金消込";
	protected override string DenTableName => nameof(Tran00Uriage);
	...
}
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.Helpers;

/// <summary>債権(売上)/債務(仕入)伝票1件。消込Flgの入力行。</summary>
public sealed partial class MatchingDenRow : ObservableObject {
	public long Id { get; set; }

	/// <summary>一覧取得時点の更新日時。消込実行時の楽観排他に使う。</summary>
	public long Vdu { get; set; }

	public string KakeDay { get; set; } = string.Empty;
	public string DenDay { get; set; } = string.Empty;
	public long Id_Tori { get; set; }

	/// <summary>取引先の「(Id) コード 名称」表示。</summary>
	public string ToriDisplay { get; set; } = string.Empty;

	public string KubunText { get; set; } = string.Empty;
	public string ManualNo { get; set; } = string.Empty;

	/// <summary>債権/債務金額。返品・値引は CalcFlag=-1 によりマイナスになる。</summary>
	public long Amount { get; set; }

	/// <summary>一覧取得時点の `EndFlag`。消込実行時の差分判定に使う。</summary>
	public int OriginalEndFlag { get; set; }

	/// <summary>消込Flg（入力可）。</summary>
	[ObservableProperty]
	public partial bool IsKesikomi { get; set; }

	/// <summary>一覧取得時点から消込状態が変化したか。</summary>
	public bool IsChanged => IsKesikomi != (OriginalEndFlag == 1);
}

/// <summary>入金/支払の区分別集計1行（明細 `Jmeisai` の `Id_Kin` 単位）。</summary>
public sealed partial class MatchingKinRow : ObservableObject {
	/// <summary>入金・支払区分Id（`MasterMeisho` の `KIN` 区分）。0 は区分未設定。</summary>
	public long Id_Kin { get; set; }

	/// <summary>区分コード（`Code_Kin`）。</summary>
	public string CodeKin { get; set; } = string.Empty;

	/// <summary>区分名称（`Mei_Kin`）。</summary>
	public string MeiKin { get; set; } = string.Empty;

	/// <summary>明細件数。</summary>
	public int Count { get; set; }

	/// <summary>金額計。</summary>
	public long Amount { get; set; }
}

public abstract partial class BaseMatchingViewModel<TDen, TKin> : BaseQueryViewModel
	where TDen : TranAllHeader, new()
	where TKin : TranKinHeader, new() {

	/// <summary>債権/債務側のテーブル名（Tran00Uriage / Tran03Shiire）</summary>
	protected abstract string DenTableName { get; }

	/// <summary>債権/債務側の取引先Id列名（Id_Tokui / Id_Shiire）</summary>
	protected abstract string DenToriIdColumn { get; }

	/// <summary>入金/支払側のテーブル名（Tran06Nyukin / Tran07Shiharai）</summary>
	protected abstract string KinTableName { get; }

	/// <summary>取引先マスタのテーブル名（MasterTokui / MasterShiire）</summary>
	protected abstract string ToriMasterTableName { get; }

	/// <summary>
	/// 取引先マスタの絞り込み条件。副問い合わせでも使うのでテーブル別名を受け取る形にする
	/// （文字列置換で別名を差し込むと条件を書き換える事故になるため）。
	/// 得意先なら "m.TenType = 1"、仕入先なら絞り込み不要。
	/// </summary>
	protected virtual string ToriMasterWhereFor(string alias) => "1 = 1";

	/// <summary>
	/// 消込済フラグの列名。<typeparamref name="TDen"/> は <see cref="TranAllHeader"/> 派生で
	/// `EndFlag` を持たないため、部分更新の対象列名をここで固定する。
	/// </summary>
	protected virtual string EndFlagColumn => nameof(Tran00Uriage.EndFlag);

	/// <summary>債権/債務側の画面上の呼び名（"売上" / "仕入"）</summary>
	protected abstract string DenLabel { get; }

	/// <summary>入金/支払側の画面上の呼び名（"入金" / "支払"）</summary>
	protected abstract string KinLabel { get; }

	/// <summary>請求先/支払先の画面上の呼び名（"請求先" / "支払先"）</summary>
	protected abstract string PaysakiLabel { get; }

	/// <summary>得意先/仕入先の画面上の呼び名（"得意先" / "仕入先"）</summary>
	protected abstract string ToriLabel { get; }

	/// <summary>伝票から取引先Idを取り出す（TranAllHeader に無いため派生で橋渡し）</summary>
	protected abstract long GetDenToriId(TDen den);

	/// <summary>伝票の掛計上日を取り出す（TranAllHeader に無いため派生で橋渡し）</summary>
	protected abstract string GetDenKakeDay(TDen den);

	/// <summary>伝票の総合計を取り出す。0 なら KingakuTotal + Tax で代替する</summary>
	protected abstract long GetDenTotal(TDen den);

	/// <summary>伝票の消費税。Total が 0 の伝票の代替計算に使う。</summary>
	protected abstract long GetDenTax(TDen den);

	/// <summary>伝票の消込済フラグ（TranAllHeader に無いため派生で橋渡し）</summary>
	protected abstract int GetDenEndFlag(TDen den);

	/// <summary>伝票の区分表示</summary>
	protected abstract string GetDenKubunText(TDen den);

	/// <summary>伝票の手入力No</summary>
	protected abstract string GetDenManualNo(TDen den);

	/// <summary>債権/債務側で読み込む列（軽量化のため明細JSONは読まない）</summary>
	protected abstract string DenSelectColumns { get; }

	/// <summary>取引先マスタから1件選ばせる。派生でマスタ型を確定する。</summary>
	protected abstract (long Id, string Code, string Name)? PickToriMaster(long startPos);

	// ---- 検索条件 ----------------------------------------------------------------

	/// <summary>請求先/支払先Id。必須。0 は未選択。</summary>
	[ObservableProperty]
	public partial long PaysakiId { get; set; }

	/// <summary>請求先/支払先の「(Id) コード 名称」表示。</summary>
	[ObservableProperty]
	public partial string PaysakiDisplay { get; set; } = string.Empty;

	/// <summary>得意先/仕入先Id。任意。0 なら請求先配下すべて。</summary>
	[ObservableProperty]
	public partial long ToriId { get; set; }

	/// <summary>得意先/仕入先の「(Id) コード 名称」表示。</summary>
	[ObservableProperty]
	public partial string ToriDisplay { get; set; } = string.Empty;

	/// <summary>掛計上日 開始 yyyy/MM/dd（既定: 先月1日）</summary>
	[ObservableProperty]
	public partial string KakeDayFromText { get; set; } = DefaultKakeDayFrom();

	/// <summary>掛計上日 終了 yyyy/MM/dd（既定: 先月末日）</summary>
	[ObservableProperty]
	public partial string KakeDayToText { get; set; } = DefaultKakeDayTo();

	/// <summary>支払日 開始 yyyy/MM/dd（既定: 当月1日）。入金/支払は掛計上日(KakeDay)で切る。</summary>
	[ObservableProperty]
	public partial string PayDayFromText { get; set; } = DefaultPayDayFrom();

	/// <summary>支払日 終了 yyyy/MM/dd（既定: 当月末日）</summary>
	[ObservableProperty]
	public partial string PayDayToText { get; set; } = DefaultPayDayTo();

	// ---- 結果 --------------------------------------------------------------------

	[ObservableProperty]
	public partial ObservableCollection<MatchingDenRow> DenRows { get; set; } = [];

	[ObservableProperty]
	public partial MatchingDenRow? SelectedDenRow { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MatchingKinRow> KinRows { get; set; } = [];

	[ObservableProperty]
	public partial MatchingKinRow? SelectedKinRow { get; set; }

	/// <summary>伝票の全件合計（チェックの有無を問わない）</summary>
	[ObservableProperty]
	public partial long DenTotal { get; set; }

	/// <summary>消込Flgをチェックした伝票の合計</summary>
	[ObservableProperty]
	public partial long CheckedTotal { get; set; }

	/// <summary>入金/支払の合計</summary>
	[ObservableProperty]
	public partial long KinTotal { get; set; }

	/// <summary>チェック件数</summary>
	[ObservableProperty]
	public partial int CheckedCount { get; set; }

	/// <summary>一覧取得時点から消込状態が変化した件数</summary>
	[ObservableProperty]
	public partial int ChangedCount { get; set; }

	static string DefaultKakeDayFrom() =>
		DateTime.Today.AddMonths(-1).ToString("yyyy/MM/01", CultureInfo.InvariantCulture);

	static string DefaultKakeDayTo() {
		var lastMonth = DateTime.Today.AddMonths(-1);
		return new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month))
			.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
	}

	static string DefaultPayDayFrom() =>
		DateTime.Today.ToString("yyyy/MM/01", CultureInfo.InvariantCulture);

	static string DefaultPayDayTo() {
		var today = DateTime.Today;
		return new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))
			.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
	}

	protected override void OnClearConditions() {
		PaysakiId = 0;
		PaysakiDisplay = string.Empty;
		ToriId = 0;
		ToriDisplay = string.Empty;
		KakeDayFromText = DefaultKakeDayFrom();
		KakeDayToText = DefaultKakeDayTo();
		PayDayFromText = DefaultPayDayFrom();
		PayDayToText = DefaultPayDayTo();
		DetachDenRows(DenRows);
		DenRows = [];
		KinRows = [];
		UpdateTotals();
	}

	/// <summary>一覧取得。請求先必須、掛計上日と支払日の2組の期間で絞る。</summary>
	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (PaysakiId <= 0) {
			MessageEx.ShowWarningDialog($"{PaysakiLabel}を選択してください。", owner: ActiveWindow);
			return;
		}
		if (!TryParseDate(KakeDayFromText, out var kakeFrom)) return;
		if (!TryParseDate(KakeDayToText, out var kakeTo)) return;
		if (kakeFrom > kakeTo) {
			MessageEx.ShowWarningDialog("掛計上日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryParseDate(PayDayFromText, out var payFrom)) return;
		if (!TryParseDate(PayDayToText, out var payTo)) return;
		if (payFrom > payTo) {
			MessageEx.ShowWarningDialog("支払日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		var toriMap = await LoadToriMapAsync(ct);
		var denList = await LoadDenAsync(ToDenDay(kakeFrom), ToDenDay(kakeTo), maxCount, ct);
		var kinList = await LoadKinAsync(ToDenDay(payFrom), ToDenDay(payTo), maxCount, ct);

		var denRows = denList
			.Select(d => {
				var toriId = GetDenToriId(d);
				toriMap.TryGetValue(toriId, out var tori);
				var total = GetDenTotal(d);
				if (total == 0) total = d.KingakuTotal + GetDenTax(d);
				var endFlag = GetDenEndFlag(d);
				return new MatchingDenRow {
					Id = d.Id,
					Vdu = d.Vdu,
					KakeDay = GetDenKakeDay(d),
					DenDay = d.DenDay,
					Id_Tori = toriId,
					ToriDisplay = CodeNameDisplay.Format(toriId, tori?.Code, tori?.Name),
					KubunText = GetDenKubunText(d),
					ManualNo = GetDenManualNo(d),
					// 返品・値引は CalcFlag=-1。元帳(Phase 3a)と同じ規則で符号を掛ける。
					Amount = total * d.CalcFlag,
					OriginalEndFlag = endFlag,
					// 消込済み伝票は初期ONで表示する。チェックを外して消込実行すると解除になる。
					IsKesikomi = endFlag == 1,
				};
			})
			.OrderBy(r => r.Id_Tori)
			.ThenBy(r => r.KakeDay, StringComparer.Ordinal)
			.ThenBy(r => r.Id)
			.ToList();

		DetachDenRows(DenRows);
		DenRows = new ObservableCollection<MatchingDenRow>(denRows);
		AttachDenRows(DenRows);
		KinRows = new ObservableCollection<MatchingKinRow>(SummarizeKin(kinList));
		UpdateTotals();
		Message = $"{DenLabel} {DenRows.Count:N0} 件（うち消込済 {CheckedCount:N0} 件） / {KinLabel} {kinList.Count:N0} 件を取得しました";
	}

	/// <summary>
	/// 入金/支払の明細 `Jmeisai` を区分別へ集計する。
	/// <para>
	/// `Jmeisai` は `[SerializedColumn]` のJSON列でSQLの GROUP BY が使えないためクライアント側で展開する。
	/// 集計キーは `Code_Kin`（伝票へコピーされた文字列）ではなく `Id_Kin`（`MasterMeisho` の `KIN` 区分への
	/// 外部キー）とし、名称は `Mei_Kin` を使う。明細が空の伝票はヘッダの `KingakuTotal` を区分未設定へ寄せる。
	/// </para>
	/// </summary>
	static List<MatchingKinRow> SummarizeKin(List<TKin> kinList) {
		var map = new Dictionary<long, MatchingKinRow>();
		MatchingKinRow GetRow(long idKin, string code, string mei) {
			if (!map.TryGetValue(idKin, out var row)) {
				row = new MatchingKinRow {
					Id_Kin = idKin,
					CodeKin = code,
					MeiKin = idKin == 0 && mei.Length == 0 ? "(区分未設定)" : mei,
				};
				map[idKin] = row;
			}
			// 同一Idで名称が空の明細が混ざる場合に備え、最初に見つかった非空の値を採用する
			if (row.CodeKin.Length == 0 && code.Length > 0) row.CodeKin = code;
			if (row.MeiKin.Length == 0 && mei.Length > 0) row.MeiKin = mei;
			return row;
		}

		foreach (var kin in kinList) {
			var meisai = kin.Jmeisai;
			if (meisai == null || meisai.Count == 0) {
				var row = GetRow(0, string.Empty, string.Empty);
				row.Count++;
				row.Amount += kin.KingakuTotal;
				continue;
			}
			foreach (var m in meisai) {
				var row = GetRow(m.Id_Kin, m.Code_Kin?.Trim() ?? string.Empty, m.Mei_Kin?.Trim() ?? string.Empty);
				row.Count++;
				row.Amount += m.Kingaku;
			}
		}
		return [.. map.Values.OrderBy(r => r.CodeKin, StringComparer.Ordinal).ThenBy(r => r.Id_Kin)];
	}

	// ---- 消込実行 ----------------------------------------------------------------

	/// <summary>
	/// 一覧取得時点から消込Flgが変化した伝票だけを `EndFlag` へ書き戻す。
	/// 在庫・掛集計へは影響しないため、<see cref="PartialUpdateParam"/> で該当列のみを更新する
	/// （行全体を保存する <see cref="UpdateParam"/> だと1件ごとに在庫再集計が走る）。
	/// <para>
	/// 楽観排他は行単位で、一覧取得時点の <see cref="MatchingDenRow.Vdu"/> を送ってサーバー側で照合する。
	/// 1件でも競合すればサーバーは全件rollbackして <see cref="CvMsgErrorCode.ConcurrentUpdate"/> を返すので、
	/// その場合は一覧を破棄して再取得を促す。成功時も `Vdu` が変わるため一覧を取り直す。
	/// </para>
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task ExecuteKesikomi(CancellationToken ct) {
		if (IsBusy) return;
		var setRows = DenRows.Where(r => r.IsKesikomi && r.OriginalEndFlag == 0).ToList();
		var clearRows = DenRows.Where(r => !r.IsKesikomi && r.OriginalEndFlag == 1).ToList();
		if (setRows.Count == 0 && clearRows.Count == 0) {
			Message = "消込状態に変更がありません。";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return;
		}

		var confirm = clearRows.Count == 0
			? $"{setRows.Count:N0} 件を消込しますか。"
			: $"{setRows.Count:N0} 件を消込、{clearRows.Count:N0} 件を解除しますか。";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("消込実行中...");
		try {
			// 消込(1)と解除(0)を1回の要求へまとめる。サーバー側は単一トランザクションで処理し、
			// 行ごとに一覧取得時点の Vdu を照合する（1件でも競合すれば全件戻る）。
			PartialUpdateRow[] rows = [
				.. setRows.Select(r => new PartialUpdateRow(r.Id, r.Vdu, ["1"])),
				.. clearRows.Select(r => new PartialUpdateRow(r.Id, r.Vdu, ["0"])),
			];
			var param = new PartialUpdateParam(typeof(TDen), [EndFlagColumn], rows);
			var reply = await SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				// サーバー側でrollback済みなので1件も書き込まれていない。一覧を破棄して再取得を促す。
				DetachDenRows(DenRows);
				DenRows = [];
				KinRows = [];
				UpdateTotals();
				Message = "他端末で更新されたため消込しませんでした（1件も更新していません）。［一覧取得（F5）］で最新の一覧を再取得してください。";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"消込に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			var done = clearRows.Count == 0
				? $"{setRows.Count:N0}件消込しました"
				: $"{setRows.Count:N0}件消込、{clearRows.Count:N0}件解除しました";
			// Vdu が変わったので一覧を取り直す。続けて消込実行しても楽観排他で弾かれない状態にする。
			await OnSearchAsync(ct);
			// OnSearchAsync が Message を上書きするため、実行結果は再取得の後に設定する
			Message = done;
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "消込実行を中断しました";
		}
		catch (Exception ex) {
			Message = $"消込に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>一覧の消込Flgを全てONにする。</summary>
	[RelayCommand]
	protected void CheckAll() => SetAllChecked(true);

	/// <summary>一覧の消込Flgを全てOFFにする。</summary>
	[RelayCommand]
	protected void UncheckAll() => SetAllChecked(false);

	void SetAllChecked(bool value) {
		foreach (var row in DenRows) row.IsKesikomi = value;
		UpdateTotals();
	}

	Task<CvMsg> SendExecuteAsync(object parameter, CancellationToken ct) =>
		CoreServiceClient.SendExecuteAsync(parameter, ct);

	// ---- 合計 --------------------------------------------------------------------

	void AttachDenRows(IEnumerable<MatchingDenRow> rows) {
		foreach (var row in rows) row.PropertyChanged += OnDenRowPropertyChanged;
	}

	void DetachDenRows(IEnumerable<MatchingDenRow> rows) {
		foreach (var row in rows) row.PropertyChanged -= OnDenRowPropertyChanged;
	}

	void OnDenRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(MatchingDenRow.IsKesikomi)) UpdateTotals();
	}

	void UpdateTotals() {
		DenTotal = DenRows.Sum(r => r.Amount);
		CheckedTotal = DenRows.Where(r => r.IsKesikomi).Sum(r => r.Amount);
		CheckedCount = DenRows.Count(r => r.IsKesikomi);
		ChangedCount = DenRows.Count(r => r.IsChanged);
		KinTotal = KinRows.Sum(r => r.Amount);
	}

	// ---- データ取得 ---------------------------------------------------------------

	/// <summary>
	/// Id値をSQLへ直接埋め込む。
	/// <para>
	/// `QueryListSqlParam.Parameters` は `string[]` のため、Idをパラメータで渡すと SQLite が
	/// 整数列と文字列を比較して常に不一致になる（動的型のため `Id = '1'` は 0件）。
	/// Idは `long` で数値以外を含み得ないので、サーバー側 `HandlePartialUpdate` の `Id IN (...)` と
	/// 同じ理由で直接埋め込む。
	/// </para>
	/// </summary>
	static string SqlId(long id) => id.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// 請求先配下の取引先Idを返す副問い合わせ。
	/// <para>
	/// `Id_Paysaki` が請求先を指す取引先に加え、請求先自身も含める。請求先が自社（`Id_Paysaki` が
	/// 自分自身）の運用と `Id_Paysaki` 未設定(0)の両方が既存データにあるため、後者が漏れないよう
	/// `Id = 請求先 AND Id_Paysaki IN (0, 請求先)` を OR で足す（ドラフト 2.1.2-1 の推奨案）。
	/// </para>
	/// </summary>
	string BuildPaysakiSubQuery() {
		var p = SqlId(PaysakiId);
		return $@"SELECT m.Id FROM {ToriMasterTableName} m
       WHERE {ToriMasterWhereFor("m")}
         AND (m.Id_Paysaki = {p} OR (m.Id = {p} AND m.Id_Paysaki IN (0, {p})))";
	}

	async Task<Dictionary<long, MasterTokui>> LoadToriMapAsync(CancellationToken ct) {
		// 得意先/仕入先どちらも Code/Name を持つので MasterTokui 型で受ける（列構成が同じ）。
		var sql = $@"
SELECT m.Id, m.Vdc, m.Vdu, m.Code, m.Name, m.Ryaku, m.Kana
FROM {ToriMasterTableName} m
WHERE m.Id IN ({BuildPaysakiSubQuery()})";
		var list = await QuerySqlListAsync<MasterTokui>(sql, [], ct);
		return list.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
	}

	async Task<List<TDen>> LoadDenAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		var sql = $@"
SELECT {DenSelectColumns}
FROM {DenTableName} h
WHERE h.KakeDay >= {AddSqlParameter(parameters, dayFrom)}
  AND h.KakeDay <= {AddSqlParameter(parameters, dayTo)}
  AND h.{DenToriIdColumn} IN ({BuildPaysakiSubQuery()}){BuildToriWhere($"h.{DenToriIdColumn}")}
ORDER BY h.{DenToriIdColumn}, h.KakeDay, h.Id
LIMIT {maxCount}";
		return await QuerySqlListAsync<TDen>(sql, parameters, ct);
	}

	async Task<List<TKin>> LoadKinAsync(string dayFrom, string dayTo, int maxCount, CancellationToken ct) {
		List<string> parameters = [];
		// 入金/支払は請求先配下の取引先分をすべて集計する（得意先Idでの絞り込みは伝票側だけに効かせる）。
		// 区分別集計に明細が必要なので Jmeisai を読む。
		var sql = $@"
SELECT h.Id, h.Vdc, h.Vdu, h.KakeDay, h.Id_Shain, h.VShain, h.Id_Torisaki, h.VTori,
       h.KingakuTotal, h.ManualNo, h.Memo, h.Jmeisai
FROM {KinTableName} h
WHERE h.KakeDay >= {AddSqlParameter(parameters, dayFrom)}
  AND h.KakeDay <= {AddSqlParameter(parameters, dayTo)}
  AND h.Id_Torisaki IN ({BuildPaysakiSubQuery()})
ORDER BY h.KakeDay, h.Id
LIMIT {maxCount}";
		return await QuerySqlListAsync<TKin>(sql, parameters, ct);
	}

	/// <summary>得意先/仕入先Idが指定されていれば伝票側を1社へ絞る。</summary>
	string BuildToriWhere(string column) =>
		ToriId <= 0 ? string.Empty : $"{Environment.NewLine}  AND {column} = {SqlId(ToriId)}";

	// ---- 選択ダイアログ ----------------------------------------------------------

	[RelayCommand]
	void SelectPaysaki() {
		var picked = PickToriMaster(PaysakiId);
		if (picked == null) return;
		PaysakiId = picked.Value.Id;
		PaysakiDisplay = CodeNameDisplay.Format(picked.Value.Id, picked.Value.Code, picked.Value.Name);
	}

	[RelayCommand]
	void SelectTori() {
		var picked = PickToriMaster(ToriId);
		if (picked == null) return;
		ToriId = picked.Value.Id;
		ToriDisplay = CodeNameDisplay.Format(picked.Value.Id, picked.Value.Code, picked.Value.Name);
	}

	/// <summary>得意先/仕入先の絞り込みを解除する（請求先配下すべてへ戻す）。</summary>
	[RelayCommand]
	void ClearTori() {
		ToriId = 0;
		ToriDisplay = string.Empty;
	}
}
