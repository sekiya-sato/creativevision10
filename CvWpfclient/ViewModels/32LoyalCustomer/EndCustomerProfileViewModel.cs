using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>
/// 顧客カルテ: 顧客を検索・特定し、ポイント台帳(TranPointEvent)の履歴と残高を照会する（照会専用）。
/// <para>
/// 取得はすべて汎用照会(Msg101)で行い、サーバ処理は追加しない。
/// 履歴は顧客の全行を取得して累計残高を計算し、期間・種別の絞込みは表示だけに適用する（期間前の繰越を正しく出すため）。
/// 検索条件を変えたら旧結果を外し、顧客を切り替えたら旧顧客の履歴を先に消してから取得する。
/// </para>
/// </summary>
public partial class EndCustomerProfileViewModel : BaseViewModel {
	/// <summary>種別「すべて」</summary>
	public const int AllTypes = -1;
	/// <summary>顧客一覧の最大取得件数</summary>
	const int CustomerMaxCount = 500;
	/// <summary>1顧客のポイント履歴の最大取得件数。到達時は累計残高を出さない</summary>
	const int HistoryMaxCount = 20_000;
	/// <summary>Id IN (...) の分割件数</summary>
	const int InChunk = 500;

	// ---- 検索条件 ----------------------------------------------------------

	[ObservableProperty]
	public partial string SearchCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchKana { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchTel { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchTenpoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchTenpoName { get; set; } = string.Empty;

	/// <summary>退会会員も一覧に含める</summary>
	[ObservableProperty]
	public partial bool IncludeWithdrawn { get; set; }

	partial void OnSearchCodeChanged(string value) => InvalidateCustomers();
	partial void OnSearchNameChanged(string value) => InvalidateCustomers();
	partial void OnSearchKanaChanged(string value) => InvalidateCustomers();
	partial void OnSearchTelChanged(string value) => InvalidateCustomers();
	partial void OnSearchTenpoCodeChanged(string value) {
		// 選択画面で選んだ名称以外はコード変更で外す（検索時に引き直す）
		if (!settingSearchTenpo) SearchTenpoName = string.Empty;
		InvalidateCustomers();
	}
	partial void OnIncludeWithdrawnChanged(bool value) => InvalidateCustomers();

	bool settingSearchTenpo;

	// ---- 顧客一覧 ----------------------------------------------------------

	public ObservableCollection<EndCustomerProfileCustomerRow> Customers { get; } = [];

	[ObservableProperty]
	public partial EndCustomerProfileCustomerRow? SelectedCustomer { get; set; }

	partial void OnSelectedCustomerChanged(EndCustomerProfileCustomerRow? value) => _ = LoadDetailAsync(value);

	[ObservableProperty]
	public partial int CustomerCount { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsIdle))]
	[NotifyCanExecuteChangedFor(nameof(DoSearchCommand))]
	public partial bool IsBusy { get; set; }

	/// <summary>検索中でなく条件・一覧を操作できる</summary>
	public bool IsIdle => !IsBusy;

	// ---- 顧客情報・残高 ----------------------------------------------------

	/// <summary>顧客が選択され、情報を表示している</summary>
	[ObservableProperty]
	public partial bool HasCustomer { get; set; }

	[ObservableProperty]
	public partial string CustomerTitle { get; set; } = "顧客を選択してください";

	[ObservableProperty]
	public partial string CustomerInfo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BalanceText { get; set; } = string.Empty;

	/// <summary>台帳合計・集計残高・会員残高が一致しない</summary>
	[ObservableProperty]
	public partial bool IsBalanceMismatch { get; set; }

	[ObservableProperty]
	public partial string DetailMessage { get; set; } = string.Empty;

	// ---- ポイント履歴（表示絞込みはクライアント側） ----------------------

	public IReadOnlyList<KeyValuePair<int, string>> FilterTypeOptions { get; } = [
		new(AllTypes, "すべて"),
		.. new[] { EnumPointEventType.Grant, EnumPointEventType.Use, EnumPointEventType.Cancel, EnumPointEventType.Adjustment, EnumPointEventType.Expire, EnumPointEventType.OpeningBalance, EnumPointEventType.LegacyHistory }
			.Select(x => new KeyValuePair<int, string>((int)x, PointLedgerManualViewModel.TypeName((int)x))),
	];

	[ObservableProperty]
	public partial string FilterDayFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string FilterDayTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int FilterType { get; set; } = AllTypes;

	partial void OnFilterDayFromChanged(string value) => ApplyFilter();
	partial void OnFilterDayToChanged(string value) => ApplyFilter();
	partial void OnFilterTypeChanged(int value) => ApplyFilter();

	/// <summary>取得した全履歴（計上日・Id順、累計残高付き）</summary>
	List<EndCustomerPointRow> allHistory = [];

	public ObservableCollection<EndCustomerPointRow> History { get; } = [];

	[ObservableProperty]
	public partial string HistoryCountText { get; set; } = string.Empty;

	/// <summary>顧客切替ごとに増やし、遅れて届いた旧顧客の結果を捨てる</summary>
	int detailVersion;
	CancellationTokenSource? detailCts;

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	// ---- コマンド ----------------------------------------------------------

	/// <summary>検索（F5）</summary>
	[RelayCommand(CanExecute = nameof(IsIdle))]
	async Task DoSearch(CancellationToken ct) {
		var code = Trim(SearchCode);
		var name = Trim(SearchName);
		var kana = Trim(SearchKana);
		var tel = Trim(SearchTel);
		var tenpoCode = Trim(SearchTenpoCode);
		if (code.Length + name.Length + kana.Length + tel.Length + tenpoCode.Length == 0) {
			Warn("検索条件を1つ以上指定してください。");
			return;
		}
		IsBusy = true;
		try {
			List<string> clauses = [];
			List<string> parameters = [];
			if (tenpoCode.Length > 0) {
				var tenpo = (await QueryListAsync<MasterTokui>(new QueryListParam(typeof(MasterTokui), "TenType=6 AND Code = @0", "Id", [tenpoCode]), ct)).FirstOrDefault();
				if (tenpo == null) {
					InvalidateCustomers();
					Warn($"店舗コード {tenpoCode} が見つかりません。");
					return;
				}
				SetSearchTenpo(tenpo);
				clauses.Add($"Id_Tenpo = {tenpo.Id.ToString(CultureInfo.InvariantCulture)}");
			}
			if (code.Length > 0) clauses.Add($"Code LIKE {AddParameter(parameters, code + "%")}");
			if (name.Length > 0) clauses.Add($"Name LIKE {AddParameter(parameters, "%" + name + "%")}");
			if (kana.Length > 0) clauses.Add($"Kana LIKE {AddParameter(parameters, "%" + kana + "%")}");
			if (tel.Length > 0) clauses.Add($"Tel LIKE {AddParameter(parameters, "%" + tel + "%")}");
			var customers = await QueryListAsync<MasterEndCustomer>(
				new QueryListParam(typeof(MasterEndCustomer), string.Join(" AND ", clauses), "Code", [.. parameters], CustomerMaxCount), ct);

			// 会員情報（退会・会員残高）。顧客1件に複数行あれば Id 最小を使う（顧客マスタメンテと同じ）
			var accounts = new Dictionary<long, MasterEndCustomerAccount>();
			foreach (var chunk in customers.Select(x => x.Id).Chunk(InChunk)) {
				foreach (var a in await QueryListAsync<MasterEndCustomerAccount>(new QueryListParam(typeof(MasterEndCustomerAccount), $"Id_Customer IN ({InList(chunk)})", "Id"), ct)) {
					accounts.TryAdd(a.Id_Customer, a);
				}
			}

			ClearDetail();
			SelectedCustomer = null;
			Customers.Clear();
			var excluded = 0;
			// コード完全一致を先頭に置く
			foreach (var c in customers.OrderBy(x => x.Code == code ? 0 : 1)) {
				accounts.TryGetValue(c.Id, out var account);
				var row = new EndCustomerProfileCustomerRow(c, account);
				if (row.IsWithdrawn && !IncludeWithdrawn) { excluded++; continue; }
				Customers.Add(row);
			}
			CustomerCount = Customers.Count;
			Message = customers.Count >= CustomerMaxCount ? $"上限 {CustomerMaxCount:N0} 件まで取得しています。条件を絞ってください。"
				: Customers.Count == 0 ? "条件に合う顧客はありません。"
				: string.Empty;
			if (excluded > 0) Message = $"{Message} 退会会員 {excluded:N0} 件を除外しています。".Trim();
			if (Customers.Count == 1) SelectedCustomer = Customers[0];
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"顧客の検索失敗: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	/// <summary>条件の店舗を選択画面で選ぶ</summary>
	[RelayCommand]
	void SelectSearchTenpo() {
		var tenpo = PrintPdfHelper.ShowSelectDialog<MasterTokui>(this, typeof(MasterTokui), "TenType=6", "Code");
		if (tenpo == null) return;
		SetSearchTenpo(tenpo);
	}

	/// <summary>期間・種別の絞込みを解除する</summary>
	[RelayCommand]
	void ClearFilter() {
		FilterDayFrom = string.Empty;
		FilterDayTo = string.Empty;
		FilterType = AllTypes;
	}

	// ---- 内部処理 ----------------------------------------------------------

	/// <summary>検索条件の変更。旧結果を外して再検索させる</summary>
	void InvalidateCustomers() {
		if (Customers.Count == 0 && SelectedCustomer == null) return;
		SelectedCustomer = null;
		Customers.Clear();
		CustomerCount = 0;
		ClearDetail();
		Message = "条件が変更されました。［検索（F5）］で再検索してください。";
	}

	/// <summary>顧客情報・履歴を消し、取得中の要求を取り消す</summary>
	void ClearDetail() {
		detailVersion++;
		detailCts?.Cancel();
		detailCts = null;
		allHistory = [];
		History.Clear();
		HasCustomer = false;
		CustomerTitle = "顧客を選択してください";
		CustomerInfo = string.Empty;
		BalanceText = string.Empty;
		IsBalanceMismatch = false;
		DetailMessage = string.Empty;
		HistoryCountText = string.Empty;
	}

	/// <summary>選択顧客の情報・残高・ポイント履歴を取得する</summary>
	async Task LoadDetailAsync(EndCustomerProfileCustomerRow? row) {
		ClearDetail();
		if (row == null) return;
		var version = detailVersion;
		var cts = new CancellationTokenSource();
		detailCts = cts;
		var ct = cts.Token;
		var c = row.Customer;
		HasCustomer = true;
		CustomerTitle = $"{c.Code}  {c.Name}";
		CustomerInfo = row.InfoText;
		DetailMessage = "ポイント履歴を取得しています…";
		try {
			var idText = c.Id.ToString(CultureInfo.InvariantCulture);
			var events = await QueryListAsync<TranPointEvent>(
				new QueryListParam(typeof(TranPointEvent), $"Id_Customer = {idText}", "DenDay, Id", [], HistoryMaxCount), ct);
			var summary = (await QueryListAsync<SummaryPoint>(new QueryListParam(typeof(SummaryPoint), $"Id_Customer = {idText}"), ct)).FirstOrDefault();
			var tenpos = new Dictionary<long, MasterTokui>();
			foreach (var chunk in events.Select(x => x.Id_Tenpo).Where(x => x != 0).Distinct().Chunk(InChunk)) {
				foreach (var t in await QueryListAsync<MasterTokui>(new QueryListParam(typeof(MasterTokui), $"Id IN ({InList(chunk)})"), ct)) {
					tenpos[t.Id] = t;
				}
			}
			if (version != detailVersion) return;

			// 取消済み: この顧客の取消行が元に持つ Id（取消は元行と同じ顧客で登録される）
			var cancelled = events.Where(x => x.EventType == (int)EnumPointEventType.Cancel && x.Id_OriginalEvent != 0)
				.Select(x => x.Id_OriginalEvent).ToHashSet();
			var truncated = events.Count >= HistoryMaxCount;
			long running = 0;
			var rows = new List<EndCustomerPointRow>(events.Count);
			foreach (var e in events) {
				running += e.PointDelta;
				tenpos.TryGetValue(e.Id_Tenpo, out var t);
				rows.Add(new EndCustomerPointRow(new PointLedgerManualRow(e, c.Code, c.Name, cancelled.Contains(e.Id)),
					truncated ? null : running, t == null ? string.Empty : $"{t.Code} {t.Name}"));
			}
			allHistory = rows;

			var accountPoint = row.Account?.Point;
			var summaryPoint = summary?.Point;
			if (events.Count == 0 && summary == null && row.Account == null) {
				BalanceText = "ポイント履歴なし";
				IsBalanceMismatch = false;
			}
			else if (truncated) {
				BalanceText = $"集計残高 {FormatBalance(summaryPoint)}　会員残高 {FormatBalance(accountPoint)}";
				IsBalanceMismatch = summaryPoint != accountPoint;
			}
			else {
				BalanceText = $"台帳合計 {running:N0} P　集計残高 {FormatBalance(summaryPoint)}　会員残高 {FormatBalance(accountPoint)}";
				IsBalanceMismatch = summaryPoint != running || (row.Account != null && accountPoint != running);
			}
			DetailMessage = truncated ? $"履歴が上限 {HistoryMaxCount:N0} 件に達したため、累計残高・台帳合計は表示しません。"
				: IsBalanceMismatch ? "残高が一致しません。ポイント再計算で確認してください。"
				: string.Empty;
			ApplyFilter();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			if (version != detailVersion) return;
			DetailMessage = $"ポイント履歴の取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(DetailMessage, owner: ActiveWindow);
		}
		finally {
			if (ReferenceEquals(detailCts, cts)) detailCts = null;
			cts.Dispose();
		}
	}

	/// <summary>期間・種別で表示行を絞る。累計残高は全履歴で計算済みの値を使う</summary>
	void ApplyFilter() {
		var from = IsDate(FilterDayFrom) ? FilterDayFrom : string.Empty;
		var to = IsDate(FilterDayTo) ? FilterDayTo : string.Empty;
		var hasPeriod = from.Length > 0 || to.Length > 0;
		History.Clear();
		var hiddenUnknownDay = 0;
		foreach (var r in allHistory) {
			var day = r.Ledger.Event.DenDay;
			if (hasPeriod) {
				// 日付不明（旧CV履歴の「.」等）は期間指定時に表示しない。累計残高には含む
				if (!IsDate(day)) { hiddenUnknownDay++; continue; }
				if (from.Length > 0 && string.CompareOrdinal(day, from) < 0) continue;
				if (to.Length > 0 && string.CompareOrdinal(day, to) > 0) continue;
			}
			if (FilterType != AllTypes && r.Ledger.Event.EventType != FilterType) continue;
			History.Add(r);
		}
		if (!HasCustomer) { HistoryCountText = string.Empty; return; }
		HistoryCountText = History.Count == allHistory.Count ? $"件数: {History.Count:N0}" : $"件数: {History.Count:N0} / 全 {allHistory.Count:N0}";
		if (hiddenUnknownDay > 0) HistoryCountText += $"（日付不明 {hiddenUnknownDay:N0} 件は非表示・残高には含む）";
	}

	void SetSearchTenpo(MasterTokui tenpo) {
		settingSearchTenpo = true;
		try {
			SearchTenpoCode = tenpo.Code;
			SearchTenpoName = tenpo.Name;
		}
		finally { settingSearchTenpo = false; }
	}

	static string FormatBalance(int? point) => point.HasValue ? $"{point.Value:N0} P" : "なし";

	static string Trim(string? value) => (value ?? string.Empty).Trim();

	static string InList(IEnumerable<long> ids) => string.Join(",", ids.Select(x => x.ToString(CultureInfo.InvariantCulture)));

	static bool IsDate(string? value) => DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

	static string AddParameter(List<string> parameters, string value) {
		parameters.Add(value);
		return $"@{parameters.Count - 1}";
	}

	void Warn(string message) {
		Message = message;
		MessageEx.ShowWarningDialog(message, owner: ActiveWindow);
	}

	void ShowError(string message) {
		Message = message;
		MessageEx.ShowErrorDialog(message, owner: ActiveWindow);
	}

	/// <summary>汎用照会(Msg101)で一覧を取得する</summary>
	static async Task<List<T>> QueryListAsync<T>(QueryListParam param, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var reply = await coreService.QueryMsgAsync(new CvMsg {
			Code = 0, Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param),
		}, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0 && reply.Code != -1) throw new InvalidOperationException(reply.Option ?? reply.DataMsg);
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? list.Cast<T>().ToList() : [];
	}
}

/// <summary>顧客カルテの顧客一覧行</summary>
public sealed class EndCustomerProfileCustomerRow(MasterEndCustomer customer, MasterEndCustomerAccount? account) {
	public MasterEndCustomer Customer { get; } = customer;
	/// <summary>会員情報。未作成の顧客は null</summary>
	public MasterEndCustomerAccount? Account { get; } = account;
	public bool IsWithdrawn => Account?.IsWithdrawalFlag is int f && f != 0;
	public string TenpoText => string.IsNullOrEmpty(Customer.VTenpo?.Cd) ? string.Empty : $"{Customer.VTenpo.Cd} {Customer.VTenpo.Mei}";
	public string WithdrawnText => !IsWithdrawn ? string.Empty
		: string.IsNullOrWhiteSpace(Account?.WithdrawnDate) ? "退会" : $"退会 {FormatDay(Account.WithdrawnDate)}";

	/// <summary>右側の顧客情報欄</summary>
	public string InfoText => string.Join("　", new[] {
		string.IsNullOrWhiteSpace(Customer.Kana) ? string.Empty : $"カナ: {Customer.Kana}",
		string.IsNullOrWhiteSpace(Customer.Rank) ? string.Empty : $"ランク: {Customer.Rank}",
		string.IsNullOrWhiteSpace(Account?.PointRank) ? string.Empty : $"ポイントランク: {Account.PointRank}",
		TenpoText.Length == 0 ? string.Empty : $"所属店舗: {TenpoText}",
		string.IsNullOrWhiteSpace(Customer.Tel) ? string.Empty : $"電話: {Customer.Tel}",
		Account == null ? "会員情報なし" : WithdrawnText,
	}.Where(x => x.Length > 0));

	static string FormatDay(string day) =>
		DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : day;
}

/// <summary>顧客カルテのポイント履歴行。種別名・旧CV履歴の内訳はポイント手動登録の行表示を共用する</summary>
public sealed class EndCustomerPointRow(PointLedgerManualRow ledger, long? balance, string tenpoText) {
	public PointLedgerManualRow Ledger { get; } = ledger;
	/// <summary>計上日・Id順の累計残高。履歴が上限に達した場合は null</summary>
	public long? Balance { get; } = balance;
	public string BalanceText => Balance?.ToString("#,0;-#,0;0", CultureInfo.InvariantCulture) ?? string.Empty;
	public string TenpoText { get; } = tenpoText;
	/// <summary>計上日。日付でない値（旧CV履歴の「.」等）はそのまま表示する</summary>
	public string DayText => DateTime.TryParseExact(Ledger.Event.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
		? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : Ledger.Event.DenDay;
	public string TenuriText => Ledger.Event.Id_Tenuri == 0 ? string.Empty : Ledger.Event.Id_Tenuri.ToString(CultureInfo.InvariantCulture);
	public string OriginalText => Ledger.Event.Id_OriginalEvent == 0 ? string.Empty : Ledger.Event.Id_OriginalEvent.ToString(CultureInfo.InvariantCulture);
}
