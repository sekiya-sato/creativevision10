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
/// ポイント手動登録: 店舗売上以外のポイント付与・使用・調整・失効をポイント台帳(TranPointEvent)へ手動で追加し、手動行を取消する。
/// <para>
/// 台帳は追記のみ（汎用CRUDの追加 Msg201/InsertParam）。検査・残高更新はサーバ(PointLedgerDb)が同一トランザクションで行う。
/// EventKey は画面で採番し、登録成功時にだけ採番し直す（失敗・応答不明の再送はサーバが同じキーとして拒否し二重計上しない）。
/// 一覧条件を変えたら旧結果を外し、再検索するまで取消できないようにする。
/// </para>
/// </summary>
public partial class PointLedgerManualViewModel : BaseViewModel {
	/// <summary>種別「すべて」</summary>
	public const int AllTypes = -1;
	/// <summary>一覧の最大取得件数</summary>
	const int ListMaxCount = 5000;
	/// <summary>手動登録で入力できるポイントの絶対値の上限</summary>
	const long MaxPointInput = 99_999_999;
	/// <summary>手動登録行の EventKey 接頭辞（サーバ PointLedgerDb.ManualKeyPrefix と同じ）</summary>
	internal const string ManualKeyPrefix = "MANUAL:";

	/// <summary>種別の表示名</summary>
	public static string TypeName(int eventType) => (EnumPointEventType)eventType switch {
		EnumPointEventType.Grant => "付与",
		EnumPointEventType.Use => "使用",
		EnumPointEventType.Cancel => "取消",
		EnumPointEventType.Adjustment => "調整",
		EnumPointEventType.Expire => "失効",
		EnumPointEventType.OpeningBalance => "期首",
		_ => eventType.ToString(CultureInfo.InvariantCulture),
	};

	/// <summary>一覧条件の種別</summary>
	public IReadOnlyList<KeyValuePair<int, string>> SearchTypeOptions { get; } = [
		new(AllTypes, "すべて"),
		.. new[] { EnumPointEventType.Grant, EnumPointEventType.Use, EnumPointEventType.Cancel, EnumPointEventType.Adjustment, EnumPointEventType.Expire, EnumPointEventType.OpeningBalance }
			.Select(x => new KeyValuePair<int, string>((int)x, TypeName((int)x))),
	];

	/// <summary>登録できる種別（取消は一覧の行から行う）</summary>
	public IReadOnlyList<KeyValuePair<int, string>> EntryTypeOptions { get; } =
		new[] { EnumPointEventType.Grant, EnumPointEventType.Use, EnumPointEventType.Adjustment, EnumPointEventType.Expire }
			.Select(x => new KeyValuePair<int, string>((int)x, TypeName((int)x))).ToList();

	// ---- 一覧条件 ----------------------------------------------------------

	[ObservableProperty]
	public partial string SearchCustomerCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchCustomerName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SearchDayFrom { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string SearchDayTo { get; set; } = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial int SearchType { get; set; } = AllTypes;

	/// <summary>手動登録(EventKey が MANUAL: で始まる行)のみ</summary>
	[ObservableProperty]
	public partial bool ManualOnly { get; set; } = true;

	partial void OnSearchCustomerCodeChanged(string value) {
		// 選択画面で選んだ名称以外はコード変更で外す（検索時に引き直す）
		if (!settingSearchCustomer) SearchCustomerName = string.Empty;
		InvalidateList();
	}
	partial void OnSearchDayFromChanged(string value) => InvalidateList();
	partial void OnSearchDayToChanged(string value) => InvalidateList();
	partial void OnSearchTypeChanged(int value) => InvalidateList();
	partial void OnManualOnlyChanged(bool value) => InvalidateList();

	bool settingSearchCustomer;
	/// <summary>検索した顧客（未指定なら null）。フォームの顧客の既定にする</summary>
	MasterEndCustomer? searchedCustomer;

	/// <summary>条件の顧客の現在残高（SummaryPoint.Point）。顧客未指定・未検索は空</summary>
	[ObservableProperty]
	public partial string BalanceText { get; set; } = string.Empty;

	// ---- 一覧 --------------------------------------------------------------

	public ObservableCollection<PointLedgerManualRow> Rows { get; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(CancelSelectedCommand))]
	public partial PointLedgerManualRow? SelectedRow { get; set; }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsIdle))]
	[NotifyCanExecuteChangedFor(nameof(DoRegisterCommand), nameof(CancelSelectedCommand), nameof(DoSearchCommand))]
	public partial bool IsBusy { get; set; }

	/// <summary>処理中でなく条件・フォームを操作できる</summary>
	public bool IsIdle => !IsBusy;

	// ---- 編集フォーム（新規登録のみ） --------------------------------------

	[ObservableProperty]
	public partial string EntryCustomerCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string EntryCustomerName { get; set; } = string.Empty;

	partial void OnEntryCustomerCodeChanged(string value) {
		if (!settingEntryCustomer) EntryCustomerName = string.Empty;
	}

	bool settingEntryCustomer;

	[ObservableProperty]
	public partial string EntryDay { get; set; } = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EntryPointHint))]
	public partial int EntryType { get; set; } = (int)EnumPointEventType.Grant;

	[ObservableProperty]
	public partial string EntryPoint { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string EntryMemo { get; set; } = string.Empty;

	/// <summary>登録の一意キー。成功時にだけ採番し直す（失敗・応答不明の再送は同じキーで送る）</summary>
	[ObservableProperty]
	public partial string EntryEventKey { get; set; } = NewEventKey();

	/// <summary>ポイント入力の説明</summary>
	public string EntryPointHint => (EnumPointEventType)EntryType switch {
		EnumPointEventType.Adjustment => "符号付きで入力（加算は正、減算は負。0は不可）",
		EnumPointEventType.Use or EnumPointEventType.Expire => "正数で入力（減算として登録します）",
		_ => "正数で入力",
	};

	static string NewEventKey() => ManualKeyPrefix + Guid.NewGuid().ToString("N");

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	// ---- コマンド ----------------------------------------------------------

	[RelayCommand]
	Task Init(CancellationToken ct) => ListCoreAsync(ct);

	/// <summary>検索（F5）</summary>
	[RelayCommand(CanExecute = nameof(IsIdle))]
	Task DoSearch(CancellationToken ct) => ListCoreAsync(ct);

	/// <summary>条件の顧客を選択画面で選ぶ</summary>
	[RelayCommand]
	void SelectSearchCustomer() {
		var customer = PrintPdfHelper.ShowSelectDialog<MasterEndCustomer>(this, typeof(MasterEndCustomer), string.Empty, "Code", startPos: searchedCustomer?.Id ?? 0);
		if (customer == null) return;
		settingSearchCustomer = true;
		try {
			SearchCustomerCode = customer.Code;
			SearchCustomerName = customer.Name;
		}
		finally { settingSearchCustomer = false; }
	}

	/// <summary>登録する顧客を選択画面で選ぶ</summary>
	[RelayCommand]
	void SelectEntryCustomer() {
		var customer = PrintPdfHelper.ShowSelectDialog<MasterEndCustomer>(this, typeof(MasterEndCustomer), string.Empty, "Code", startPos: searchedCustomer?.Id ?? 0);
		if (customer == null) return;
		SetEntryCustomer(customer);
	}

	/// <summary>登録（F2）。確認後に台帳へ1行追加し、成功したら再検索してフォームをクリアする</summary>
	[RelayCommand(CanExecute = nameof(IsIdle))]
	async Task DoRegister(CancellationToken ct) {
		var code = (EntryCustomerCode ?? string.Empty).Trim();
		if (code.Length == 0) { Warn("登録する顧客を指定してください。"); return; }
		if (!IsDate(EntryDay)) { Warn("計上日を正しく指定してください。"); return; }
		var type = (EnumPointEventType)EntryType;
		if (!EntryTypeOptions.Any(x => x.Key == EntryType)) { Warn("種別を選択してください。"); return; }
		if (!long.TryParse((EntryPoint ?? string.Empty).Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var input)) {
			Warn("ポイントを数値で入力してください。");
			return;
		}
		if (Math.Abs(input) > MaxPointInput) { Warn($"ポイントは {MaxPointInput:N0} 以下で入力してください。"); return; }
		long delta;
		if (type == EnumPointEventType.Adjustment) {
			if (input == 0) { Warn("調整ポイントは0以外で入力してください。"); return; }
			delta = input;
		}
		else {
			if (input <= 0) { Warn($"{TypeName(EntryType)}のポイントは正数で入力してください。"); return; }
			delta = type is EnumPointEventType.Use or EnumPointEventType.Expire ? -input : input;
		}
		var memo = (EntryMemo ?? string.Empty).Trim();
		if (memo.Length is 0 or > 200) { Warn("理由を1～200文字で入力してください。"); return; }

		IsBusy = true;
		try {
			var customer = await FindCustomerAsync(code, ct);
			if (customer == null) { Warn($"顧客コード {code} が見つかりません。"); return; }
			SetEntryCustomer(customer);
			var question = $"次の内容でポイント台帳へ登録しますか？\n\n顧客: {customer.Code} {customer.Name}\n計上日: {FormatDay(EntryDay)}\n種別: {TypeName(EntryType)}\nポイント: {delta.ToString("+#,0;-#,0", CultureInfo.InvariantCulture)}\n理由: {memo}";
			if (MessageEx.ShowQuestionDialog(question, owner: ActiveWindow) != MessageBoxResult.Yes) return;
			var row = new TranPointEvent {
				EventKey = EntryEventKey,
				DenDay = EntryDay,
				Id_Customer = customer.Id,
				EventType = EntryType,
				PointDelta = delta,
				Id_Shain = 0,
				Memo = memo,
			};
			var saved = await InsertAsync(row, "登録", ct);
			if (saved == null) return;
			// 成功したので次の登録用に採番し直し、フォームをクリアしてから最新の一覧・残高を表示する
			ResetForm();
			await ListCoreAsync(ct);
			Message = $"登録しました（Id={saved.Id}、{TypeName(saved.EventType)} {saved.PointDelta.ToString("+#,0;-#,0", CultureInfo.InvariantCulture)}）";
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			ShowError($"登録の結果を確認できませんでした: {ex.Message}\n一覧を再検索して登録されたか確認してください。同じ内容のまま再度登録しても二重には計上されません。");
		}
		finally { IsBusy = false; }
	}

	/// <summary>選択行を取消。元の行と逆符号の取消行を本日付で追加する</summary>
	[RelayCommand(CanExecute = nameof(CanCancelSelected))]
	async Task CancelSelected(CancellationToken ct) {
		var target = SelectedRow;
		if (target == null || !target.CanCancel) return;
		var org = target.Event;
		var today = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var question = $"次の行を取消しますか？\n\nId: {org.Id}\n顧客: {target.CustomerCode} {target.CustomerName}\n種別: {target.TypeName}\nポイント: {target.PointText}\n理由: {org.Memo}\n\n逆符号({(-org.PointDelta).ToString("+#,0;-#,0", CultureInfo.InvariantCulture)})の取消行を計上日 {FormatDay(today)} で追加します。";
		if (MessageEx.ShowQuestionDialog(question, owner: ActiveWindow) != MessageBoxResult.Yes) return;
		IsBusy = true;
		try {
			var memo = $"取消(元Id={org.Id}) {org.Memo}";
			var row = new TranPointEvent {
				// 取消の EventKey はサーバが「MANUAL:C:{元Id}」に設定する（1行1回）
				EventKey = string.Empty,
				DenDay = today,
				Id_Customer = org.Id_Customer,
				EventType = (int)EnumPointEventType.Cancel,
				PointDelta = -org.PointDelta,
				Id_OriginalEvent = org.Id,
				Id_Shain = 0,
				Memo = memo.Length > 200 ? memo[..200] : memo,
			};
			var saved = await InsertAsync(row, "取消", ct);
			if (saved == null) return;
			await ListCoreAsync(ct);
			Message = $"取消しました（元Id={org.Id}、取消Id={saved.Id}）";
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			ShowError($"取消の結果を確認できませんでした: {ex.Message}\n一覧を再検索して確認してください。");
		}
		finally { IsBusy = false; }
	}

	bool CanCancelSelected() => !IsBusy && SelectedRow?.CanCancel == true;

	// ---- 内部処理 ----------------------------------------------------------

	/// <summary>一覧条件の変更。旧結果を外して再検索させる</summary>
	void InvalidateList() {
		searchedCustomer = null;
		BalanceText = string.Empty;
		if (Rows.Count == 0 && SelectedRow == null) return;
		SelectedRow = null;
		Rows.Clear();
		Count = 0;
		Message = "条件が変更されました。［検索（F5）］で再検索してください。";
	}

	/// <summary>条件で台帳を検索し、取消済み・顧客名・残高を付けて表示する</summary>
	async Task ListCoreAsync(CancellationToken ct) {
		if (!IsDate(SearchDayFrom) || !IsDate(SearchDayTo) || string.CompareOrdinal(SearchDayFrom, SearchDayTo) > 0) {
			Warn("計上日は開始日・終了日を正しく指定してください。");
			return;
		}
		// 登録・取消後の再検索など、呼出元が処理中のときは処理中のまま戻す
		var wasBusy = IsBusy;
		IsBusy = true;
		try {
			MasterEndCustomer? customer = null;
			var code = (SearchCustomerCode ?? string.Empty).Trim();
			if (code.Length > 0) {
				customer = await FindCustomerAsync(code, ct);
				if (customer == null) {
					InvalidateList();
					Warn($"顧客コード {code} が見つかりません。");
					return;
				}
			}
			List<string> clauses = [];
			List<string> parameters = [];
			clauses.Add($"DenDay >= {AddParameter(parameters, SearchDayFrom)}");
			clauses.Add($"DenDay <= {AddParameter(parameters, SearchDayTo)}");
			if (customer != null) clauses.Add($"Id_Customer = {customer.Id.ToString(CultureInfo.InvariantCulture)}");
			if (SearchType != AllTypes) clauses.Add($"EventType = {SearchType.ToString(CultureInfo.InvariantCulture)}");
			if (ManualOnly) clauses.Add($"EventKey LIKE {AddParameter(parameters, ManualKeyPrefix + "%")}");
			var events = await QueryListAsync<TranPointEvent>(
				new QueryListParam(typeof(TranPointEvent), string.Join(" AND ", clauses), "DenDay, Id", [.. parameters], ListMaxCount), ct);

			// 取消済み（この行を元に持つ取消行がある）と顧客名を引く
			var cancelled = new HashSet<long>();
			var customers = new Dictionary<long, MasterEndCustomer>();
			foreach (var chunk in events.Select(x => x.Id).Chunk(500)) {
				var inList = string.Join(",", chunk.Select(x => x.ToString(CultureInfo.InvariantCulture)));
				foreach (var c in await QueryListAsync<TranPointEvent>(new QueryListParam(typeof(TranPointEvent),
					$"EventType = {(int)EnumPointEventType.Cancel} AND Id_OriginalEvent IN ({inList})"), ct)) {
					cancelled.Add(c.Id_OriginalEvent);
				}
			}
			foreach (var chunk in events.Select(x => x.Id_Customer).Distinct().Chunk(500)) {
				var inList = string.Join(",", chunk.Select(x => x.ToString(CultureInfo.InvariantCulture)));
				foreach (var c in await QueryListAsync<MasterEndCustomer>(new QueryListParam(typeof(MasterEndCustomer), $"Id IN ({inList})"), ct)) {
					customers[c.Id] = c;
				}
			}
			string balance = string.Empty;
			if (customer != null) {
				var summary = (await QueryListAsync<SummaryPoint>(new QueryListParam(typeof(SummaryPoint), $"Id_Customer = {customer.Id.ToString(CultureInfo.InvariantCulture)}"), ct)).FirstOrDefault();
				balance = $"現在残高 {(summary?.Point ?? 0):N0} P";
			}

			SelectedRow = null;
			Rows.Clear();
			foreach (var e in events) {
				customers.TryGetValue(e.Id_Customer, out var c);
				Rows.Add(new PointLedgerManualRow(e, c?.Code ?? string.Empty, c?.Name ?? string.Empty, cancelled.Contains(e.Id)));
			}
			Count = Rows.Count;
			// 一覧の表示に合わせて条件の顧客名・残高を確定する
			SearchCustomerName = customer?.Name ?? string.Empty;
			searchedCustomer = customer;
			BalanceText = balance;
			if (customer != null && string.IsNullOrWhiteSpace(EntryCustomerCode)) SetEntryCustomer(customer);
			Message = Count == 0 ? "条件に合うポイント台帳はありません。"
				: Count >= ListMaxCount ? $"上限 {ListMaxCount:N0} 件まで表示しています。条件を絞ってください。"
				: string.Empty;
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"一覧の取得失敗: {ex.Message}"); }
		finally { IsBusy = wasBusy; }
	}

	/// <summary>台帳へ1行追加する。エラー応答は表示して null を返す</summary>
	async Task<TranPointEvent?> InsertAsync(TranPointEvent row, string actionName, CancellationToken ct) {
		var reply = await SendMessageAsync(new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(InsertParam),
			DataMsg = Common.SerializeObject(new InsertParam(typeof(TranPointEvent), Common.SerializeObject(row))),
		}, ct);
		if (reply.Code < 0) {
			var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
			if (string.IsNullOrWhiteSpace(detail)) detail = reply.Option ?? reply.DataMsg;
			Message = $"{actionName}エラー: {detail}";
			MessageEx.ShowWarningDialog($"{Message} ({reply.Code})", owner: ActiveWindow);
			return null;
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as TranPointEvent
			?? throw new InvalidOperationException("サーバ応答を読み取れません。");
	}

	/// <summary>フォームを初期状態へ戻し、EventKey を採番し直す（登録成功時だけ呼ぶ）</summary>
	void ResetForm() {
		EntryEventKey = NewEventKey();
		if (searchedCustomer != null) SetEntryCustomer(searchedCustomer);
		else {
			settingEntryCustomer = true;
			try {
				EntryCustomerCode = string.Empty;
				EntryCustomerName = string.Empty;
			}
			finally { settingEntryCustomer = false; }
		}
		EntryDay = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		EntryType = (int)EnumPointEventType.Grant;
		EntryPoint = string.Empty;
		EntryMemo = string.Empty;
	}

	void SetEntryCustomer(MasterEndCustomer customer) {
		settingEntryCustomer = true;
		try {
			EntryCustomerCode = customer.Code;
			EntryCustomerName = customer.Name;
		}
		finally { settingEntryCustomer = false; }
	}

	async Task<MasterEndCustomer?> FindCustomerAsync(string code, CancellationToken ct) =>
		(await QueryListAsync<MasterEndCustomer>(new QueryListParam(typeof(MasterEndCustomer), "Code = @0", "Id", [code]), ct)).FirstOrDefault();

	static string FormatDay(string day) =>
		DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : day;

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

	ValueTask<CvMsg> SendMessageAsync(CvMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return new ValueTask<CvMsg>(coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct)));
	}

	/// <summary>汎用照会(Msg101)で一覧を取得する</summary>
	async Task<List<T>> QueryListAsync<T>(QueryListParam param, CancellationToken ct) {
		var reply = await SendMessageAsync(new CvMsg {
			Code = 0, Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param),
		}, ct);
		if (reply.Code < 0 && reply.Code != -1) throw new InvalidOperationException(reply.Option ?? reply.DataMsg);
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? list.Cast<T>().ToList() : [];
	}
}

/// <summary>ポイント手動登録の一覧行</summary>
public sealed class PointLedgerManualRow(TranPointEvent pointEvent, string customerCode, string customerName, bool isCancelled) {
	public TranPointEvent Event { get; } = pointEvent;
	public string CustomerCode { get; } = customerCode;
	public string CustomerName { get; } = customerName;
	/// <summary>この行を元に持つ取消行がある</summary>
	public bool IsCancelled { get; } = isCancelled;
	public string TypeName => PointLedgerManualViewModel.TypeName(Event.EventType);
	public string PointText => Event.PointDelta.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture);
	public string CancelledText => IsCancelled ? "取消済" : string.Empty;
	/// <summary>手動登録行（EventKey が MANUAL: で始まる）</summary>
	public bool IsManual => Event.EventKey.StartsWith(PointLedgerManualViewModel.ManualKeyPrefix, StringComparison.Ordinal);
	/// <summary>取消できる: 手動行・取消行でない・未取消</summary>
	public bool CanCancel => IsManual && Event.EventType != (int)EnumPointEventType.Cancel && !IsCancelled;
}
