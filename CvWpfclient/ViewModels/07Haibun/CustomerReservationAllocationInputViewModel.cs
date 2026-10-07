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

/// <summary>
/// 取置配分入力。直営店が一般顧客（<see cref="MasterEndCustomer"/>）向けに店舗在庫を取り置き、
/// 店舗売上への変換・取消・期限変更・数量変更までを1画面で行う（区分 <see cref="EnumHaibun.Reservation"/>(6)）。
/// <para>
/// 登録・数量変更・期限変更は配分の洗い替え保存（<c>HaibunSaveParam</c>）、売上変換は <see cref="ReservationConvertParam"/>、
/// 取消は <see cref="ReservationCancelParam"/> を送る。期限日を過ぎた取置はサーバの日次タスクが自動で取り消す。
/// 有効在庫が足りなくても登録はでき、確認ダイアログで警告する（判断 2）。
/// 店舗自身の在庫を一般顧客向けに取置し、店舗×顧客の売上変換または取消で引当を解除する。POSで別途会計すると二重計上になる。
/// </para>
/// </summary>
public partial class CustomerReservationAllocationInputViewModel : BaseViewModel {
	const int KubunReservation = (int)EnumHaibun.Reservation;
	/// <summary>期限が近いとみなす残日数（この日数以内は橙）</summary>
	public const int NearLimitDays = 3;
	const int MaxRows = 2000;

	/// <summary>POS との二重計上の注意（D10）。画面上部と売上変換の確認で出す</summary>
	public const string PosNotice = "取置の売上は、この画面の［売上変換］で計上します。POS で同じ商品を会計しないでください。";

	long idTenpo;
	long entryIdShohin;
	MasterShohin? entryShohin;

	[ObservableProperty]
	public partial string Message { get; set; } = "店舗を指定して［検索］を押してください。";

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== 条件 =====

	[ObservableProperty]
	public partial string TenpoCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenpoName { get; set; } = string.Empty;

	/// <summary>顧客（コードまたは名前の部分一致）</summary>
	[ObservableProperty]
	public partial string CustomerFilter { get; set; } = string.Empty;

	public IReadOnlyList<string> StatusKinds { get; } = ["取置中", $"期限{NearLimitDays}日以内", "完了を含む"];

	[ObservableProperty]
	public partial int StatusIndex { get; set; }

	[ObservableProperty]
	public partial DateTime? DenDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? DenDayTo { get; set; }

	// ===== 一覧 =====

	[ObservableProperty]
	public partial ObservableCollection<CustomerReservationRow> Rows { get; set; } = [];

	/// <summary>売上変換の売上日</summary>
	[ObservableProperty]
	public partial DateTime? UriDay { get; set; } = DateTime.Today;

	/// <summary>期限変更の新しい期限日</summary>
	[ObservableProperty]
	public partial DateTime? NewLimitDay { get; set; }

	/// <summary>数量変更の新しい取置数</summary>
	[ObservableProperty]
	public partial int NewSu { get; set; }

	// ===== 登録 =====

	[ObservableProperty]
	public partial string EntryCustomerCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string EntryCustomerName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string EntryShohinCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string EntryShohinName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<CustomerReservationSku> EntrySkus { get; set; } = [];

	[ObservableProperty]
	public partial CustomerReservationSku? EntrySku { get; set; }

	[ObservableProperty]
	public partial int EntrySu { get; set; } = 1;

	[ObservableProperty]
	public partial DateTime? EntryDenDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? EntryLimitDay { get; set; } = DateTime.Today.AddDays(7);

	[ObservableProperty]
	public partial string EntryMemo { get; set; } = string.Empty;

	/// <summary>取置日を変えたら期限日を取置日の1週間後へ揃える（決定 D11）</summary>
	partial void OnEntryDenDayChanged(DateTime? value) {
		if (value is DateTime d) EntryLimitDay = d.AddDays(7);
	}

	bool CanOperate() => !IsBusy && idTenpo > 0;

	/// <summary>
	/// 店舗を変えたら、検索するまで前の店舗の一覧・在庫で操作させない
	/// （内部の店舗Idは検索で確定する。古いIdのまま登録すると前の店舗に取置が入る）
	/// </summary>
	partial void OnTenpoCodeChanged(string value) {
		if (idTenpo == 0) return;
		idTenpo = 0;
		Rows = [];
		EntrySkus = [];
		EntrySku = null;
		Message = "店舗が変わりました。［検索］を押してください。";
		NotifyCommands();
	}

	partial void OnIsBusyChanged(bool value) => NotifyCommands();

	void NotifyCommands() {
		DoConvertCommand.NotifyCanExecuteChanged();
		CancelReservationCommand.NotifyCanExecuteChanged();
		ChangeLimitCommand.NotifyCanExecuteChanged();
		ChangeQtyCommand.NotifyCanExecuteChanged();
		DoRegisterCommand.NotifyCanExecuteChanged();
		SelectCustomerCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	void SelectTenpo() {
		var tenpo = ShowSelect<MasterTokui>(typeof(MasterTokui), "TenType = 6", "Code");
		if (tenpo == null) return;
		TenpoCode = tenpo.Code;
		TenpoName = tenpo.Name;
	}

	/// <summary>顧客選択。その店舗の顧客だけを出す（ほかの店舗の顧客はコードで入力する）</summary>
	[RelayCommand(CanExecute = nameof(CanOperate))]
	void SelectCustomer() {
		var customer = ShowSelect<MasterEndCustomer>(typeof(MasterEndCustomer),
			$"Id_Tenpo = {idTenpo.ToString(CultureInfo.InvariantCulture)}", "Code");
		if (customer == null) return;
		EntryCustomerCode = customer.Code;
		EntryCustomerName = customer.Name;
	}

	[RelayCommand]
	async Task SelectShohin(CancellationToken ct) {
		var shohin = ShowSelect<MasterShohin>(typeof(MasterShohin), string.Empty, "Code");
		if (shohin == null) return;
		EntryShohinCode = shohin.Code;
		await LoadEntryShohinAsync(ct);
	}

	/// <summary>商品コード入力後（フォーカス移動・Enter）に色サイズと有効在庫を読み直す</summary>
	[RelayCommand]
	async Task LoadEntryShohin(CancellationToken ct) {
		try {
			await LoadEntryShohinAsync(ct);
		}
		catch (Exception ex) {
			Message = ex.Message;
		}
	}

	/// <summary>検索(F5)</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(TenpoCode)) {
			MessageEx.ShowWarningDialog("店舗を指定してください。", owner: ActiveWindow);
			return;
		}
		if (DenDayFrom != null && DenDayTo != null && DenDayFrom > DenDayTo) {
			MessageEx.ShowWarningDialog("取置日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}
		StartBusy("取置を取得中...");
		try {
			var tenpo = (await QuerySqlListAsync<MasterTokui>(
				$"SELECT * FROM {nameof(MasterTokui)} WHERE Code = @0 AND TenType = 6", [TenpoCode.Trim()], ct)).FirstOrDefault()
				?? throw new InvalidOperationException($"直営店コード {TenpoCode} が見つかりません。");
			idTenpo = tenpo.Id;
			TenpoName = tenpo.Name;
			await LoadRowsAsync(ct);
			if (entryIdShohin > 0) await LoadEntryShohinAsync(ct);
		}
		catch (OperationCanceledException) {
			Message = "検索を中断しました";
		}
		catch (Exception ex) {
			Message = $"検索失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void CheckAll() {
		foreach (var row in Rows.Where(r => r.IsOpen)) row.IsChecked = true;
	}

	[RelayCommand]
	void UncheckAll() {
		foreach (var row in Rows) row.IsChecked = false;
	}

	/// <summary>売上変換。チェックした取置中の行を店舗売上（店舗×顧客ごとに1伝票）にする</summary>
	[RelayCommand(CanExecute = nameof(CanOperate))]
	async Task DoConvert(CancellationToken ct) {
		var targets = CheckedOpenRows();
		if (targets.Count == 0) return;
		if (UriDay == null) {
			MessageEx.ShowWarningDialog("売上日を入力してください。", owner: ActiveWindow);
			return;
		}
		var customers = targets.Select(r => r.Source.Id_Customer).Distinct().Count();
		var question = $"取置 {targets.Count:N0} 件（{targets.Sum(r => r.Su):N0} 点・{targets.Sum(r => (long)r.Su * r.Source.Tanka):N0} 円）を"
			+ $" {UriDay:yyyy/MM/dd} の店舗売上にします（顧客 {customers:N0} 人分の伝票を作成）。"
			+ $"\n{PosNotice}\n売上変換後は取り消せません（訂正は店舗売上入力で行います）。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(question, owner: ActiveWindow) != MessageBoxResult.Yes) return;
		await ExecuteAsync("売上変換中...", "売上変換",
			new ReservationConvertParam([.. targets.Select(ToRef)], ToYmd8(UriDay), 0),
			reply => {
				var result = Common.DeserializeObject(reply.DataMsg ?? "", typeof(ReservationConvertResult)) as ReservationConvertResult;
				return $"取置 {result?.ConvertedCount ?? targets.Count:N0} 件を売上変換し、店舗売上を {result?.CreatedSlipIds.Length ?? 0:N0} 件作成しました。";
			}, ct);
	}

	/// <summary>
	/// 取消。チェックした取置中の行を取り消す（伝票は作らない）。
	/// コマンド名を「～CancelCommand」にしないこと（BaseWindow が閉じるときに中断用とみなして実行してしまう）
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanOperate))]
	async Task CancelReservation(CancellationToken ct) {
		var targets = CheckedOpenRows();
		if (targets.Count == 0) return;
		var question = $"取置 {targets.Count:N0} 件（{targets.Sum(r => r.Su):N0} 点）を取り消し、引当を解除します。\n取消後は元に戻せません。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(question, owner: ActiveWindow) != MessageBoxResult.Yes) return;
		await ExecuteAsync("取消中...", "取消",
			new ReservationCancelParam([.. targets.Select(ToRef)], ToYmd8(DateTime.Today)),
			reply => {
				var result = Common.DeserializeObject(reply.DataMsg ?? "", typeof(ReservationCancelResult)) as ReservationCancelResult;
				return $"取置 {result?.CancelledCount ?? targets.Count:N0} 件を取り消しました。";
			}, ct);
	}

	/// <summary>期限変更。チェックした1行の期限日を変える（洗い替え保存）</summary>
	[RelayCommand(CanExecute = nameof(CanOperate))]
	async Task ChangeLimit(CancellationToken ct) {
		if (SingleCheckedOpenRow() is not CustomerReservationRow row) return;
		if (NewLimitDay is not DateTime limit) {
			MessageEx.ShowWarningDialog("新しい期限日を入力してください。", owner: ActiveWindow);
			return;
		}
		var replaced = CopyForReplace(row.Source);
		replaced.LimitDay = ToYmd8(limit);
		if (string.CompareOrdinal(replaced.LimitDay, replaced.DenDay) < 0) {
			MessageEx.ShowWarningDialog("期限日は取置日以降の日付にしてください。", owner: ActiveWindow);
			return;
		}
		if (!ConfirmPastLimit(limit)) return;
		await SaveAsync(row.Source, replaced, $"期限日を {limit:yyyy/MM/dd} に変更しました。", ct);
	}

	/// <summary>数量変更。チェックした1行の取置数を変える（洗い替え保存）。有効在庫を超えるときは警告する</summary>
	[RelayCommand(CanExecute = nameof(CanOperate))]
	async Task ChangeQty(CancellationToken ct) {
		if (SingleCheckedOpenRow() is not CustomerReservationRow row) return;
		if (NewSu <= 0) {
			MessageEx.ShowWarningDialog("取置数は1以上を入力してください（取り止めは［取消］）。", owner: ActiveWindow);
			return;
		}
		try {
			// 自分の取置は引当に入っているので、変更前の数を戻した有効在庫と比べる
			var yuko = await LoadYukoAsync(row.Source.Id_Shohin, row.Source.Id_Col, row.Source.Id_Siz, ct) + row.Su;
			if (NewSu > yuko && !ConfirmShortage(yuko, NewSu)) return;
		}
		catch (Exception ex) {
			Message = $"有効在庫の取得に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
			return;
		}
		var replaced = CopyForReplace(row.Source);
		replaced.Su = NewSu;
		replaced.Kingaku = NewSu * replaced.Tanka;
		await SaveAsync(row.Source, replaced, $"取置数を {NewSu:N0} に変更しました。", ct);
	}

	/// <summary>取置登録(F6)</summary>
	[RelayCommand(CanExecute = nameof(CanOperate), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (EntryDenDay is not DateTime denDay || EntryLimitDay is not DateTime limitDay) {
			MessageEx.ShowWarningDialog("取置日と期限日を入力してください。", owner: ActiveWindow);
			return;
		}
		if (limitDay < denDay) {
			MessageEx.ShowWarningDialog("期限日は取置日以降の日付にしてください。", owner: ActiveWindow);
			return;
		}
		if (!ConfirmPastLimit(limitDay)) return;
		if (EntrySu <= 0) {
			MessageEx.ShowWarningDialog("取置数は1以上を入力してください。", owner: ActiveWindow);
			return;
		}
		StartBusy("取置を登録中...");
		try {
			var customer = (await QuerySqlListAsync<MasterEndCustomer>(
				$"SELECT * FROM {nameof(MasterEndCustomer)} WHERE Code = @0", [EntryCustomerCode.Trim()], ct)).FirstOrDefault();
			if (customer == null) {
				MessageEx.ShowWarningDialog("顧客を指定してください。", owner: ActiveWindow);
				return;
			}
			EntryCustomerName = customer.Name;
			if (entryShohin == null || !string.Equals(entryShohin.Code, EntryShohinCode.Trim(), StringComparison.Ordinal)) {
				// 商品コードを打ち替えたまま登録しようとした。色サイズを選び直してもらう
				await LoadEntryShohinAsync(ct);
				MessageEx.ShowWarningDialog("商品を読み込みました。色サイズを確認してから登録してください。", owner: ActiveWindow);
				return;
			}
			if (EntrySku == null) {
				MessageEx.ShowWarningDialog("商品と色サイズを指定してください。", owner: ActiveWindow);
				return;
			}
			var sku = EntrySku;
			var yuko = await LoadYukoAsync(entryShohin.Id, sku.Id_Col, sku.Id_Siz, ct);
			if (EntrySu > yuko && !ConfirmShortage(yuko, EntrySu)) return;
			var question = $"{TenpoName} で {customer.Name} 様の取置 {entryShohin.Name} {sku.Display} {EntrySu:N0} 点（期限 {limitDay:yyyy/MM/dd}）を登録します。よろしいですか？";
			if (MessageEx.ShowQuestionDialog(question, owner: ActiveWindow) != MessageBoxResult.Yes) return;

			var denYmd = ToYmd8(denDay);
			var jodai = await LoadJodaiAsync(entryShohin, denYmd, ct);
			var row = new TranHaibun {
				Kubun = KubunReservation,
				DenDay = denYmd,
				LimitDay = ToYmd8(limitDay),
				Id_Tenpo = idTenpo,
				Id_Soko = idTenpo,
				Id_Customer = customer.Id,
				Id_Shohin = entryShohin.Id,
				JanCode = sku.JanCode,
				Id_Col = sku.Id_Col,
				Id_Siz = sku.Id_Siz,
				Su = EntrySu,
				Tanka = jodai,
				Kingaku = EntrySu * jodai,
				Jodai = jodai,
				Gedai = entryShohin.TankaGenka,
				Memo = EntryMemo,
			};
			await CoreServiceClient.SaveHaibunAsync([], [row], "取置", ct);
			await LoadRowsAsync(ct);
			await LoadEntryShohinAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} {customer.Name} 様の取置を登録しました（{EntrySu:N0} 点、単価 {jodai:N0} 円）。";
		}
		catch (OperationCanceledException) {
			Message = "登録を中断しました";
		}
		catch (Exception ex) {
			Message = $"登録失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	// ===== 読込 =====

	async Task LoadRowsAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [$"h.Kubun = {KubunReservation}", $"h.Id_Tenpo = {AddParameter(parameters, idTenpo)}"];
		switch (StatusIndex) {
			case 0:
				clauses.Add("h.EndFlag = 0");
				break;
			case 1:
				clauses.Add("h.EndFlag = 0");
				clauses.Add($"h.LimitDay <= {AddParameter(parameters, ToYmd8(DateTime.Today.AddDays(NearLimitDays)))}");
				break;
		}
		if (DenDayFrom is DateTime from) clauses.Add($"h.DenDay >= {AddParameter(parameters, ToYmd8(from))}");
		if (DenDayTo is DateTime to) clauses.Add($"h.DenDay <= {AddParameter(parameters, ToYmd8(to))}");
		if (!string.IsNullOrWhiteSpace(CustomerFilter)) {
			var like = AddParameter(parameters, $"%{CustomerFilter.Trim()}%");
			clauses.Add($"h.Id_Customer IN (SELECT Id FROM {nameof(MasterEndCustomer)} WHERE Code LIKE {like} OR Name LIKE {like} OR Kana LIKE {like})");
		}
		var haibun = await QuerySqlListAsync<TranHaibun>($"""
			SELECT h.* FROM {nameof(TranHaibun)} h
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY h.EndFlag, h.LimitDay, h.Id
			LIMIT {MaxRows}
			""", parameters, ct);

		var customers = await LoadByIdsAsync<MasterEndCustomer>(haibun.Select(x => x.Id_Customer), ct);
		var shohins = await LoadByIdsAsync<MasterShohin>(haibun.Select(x => x.Id_Shohin), ct);
		var shohinIds = haibun.Select(x => x.Id_Shohin).Where(x => x > 0).Distinct().ToList();
		var skus = shohinIds.Count == 0 ? [] : (await QuerySqlListAsync<DerivedShohinColSiz>(
				$"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin IN ({string.Join(",", shohinIds)})", [], ct))
			.DistinctBy(x => (x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(x => (x.Id_Shohin, x.Id_Col, x.Id_Siz));
		var today = DateTime.Today;
		Rows = [.. haibun.Select(h => {
			var c = customers.GetValueOrDefault(h.Id_Customer);
			var s = shohins.GetValueOrDefault(h.Id_Shohin);
			var sku = skus.GetValueOrDefault((h.Id_Shohin, h.Id_Col, h.Id_Siz));
			return new CustomerReservationRow(h, today) {
				CustomerDisplay = c == null ? $"Id{h.Id_Customer}" : $"{c.Code} {c.Name}",
				ShohinDisplay = s == null ? $"Id{h.Id_Shohin}" : $"{s.Code} {s.Name}",
				ColSizDisplay = sku == null ? $"{h.Id_Col}/{h.Id_Siz}" : $"{sku.Code_Col} {sku.Mei_Col} / {sku.Code_Siz}",
			};
		})];
		var open = Rows.Count(r => r.IsOpen);
		var limited = haibun.Count >= MaxRows ? $"（{MaxRows:N0} 件で打ち切りました。条件を絞ってください）" : string.Empty;
		Message = Rows.Count == 0
			? "該当する取置がありません。"
			: $"取置 {Rows.Count:N0} 件（取置中 {open:N0} 件・{Rows.Where(r => r.IsOpen).Sum(r => r.Su):N0} 点）を表示しました。{limited}";
		NotifyCommands();
	}

	/// <summary>登録欄の商品の色サイズと、その店舗の有効在庫を読む</summary>
	async Task LoadEntryShohinAsync(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(EntryShohinCode)) {
			entryIdShohin = 0;
			entryShohin = null;
			EntryShohinName = string.Empty;
			EntrySkus = [];
			return;
		}
		var shohin = (await QuerySqlListAsync<MasterShohin>(
			$"SELECT * FROM {nameof(MasterShohin)} WHERE Code = @0", [EntryShohinCode.Trim()], ct)).FirstOrDefault()
			?? throw new InvalidOperationException($"商品コード {EntryShohinCode} が見つかりません。");
		var previous = EntrySku;
		entryShohin = shohin;
		entryIdShohin = shohin.Id;
		EntryShohinName = shohin.Name;
		var colsiz = await QuerySqlListAsync<DerivedShohinColSiz>(
			$"SELECT * FROM {nameof(DerivedShohinColSiz)} WHERE Id_Shohin = @0 ORDER BY RowIdx",
			[shohin.Id.ToString(CultureInfo.InvariantCulture)], ct);
		var stock = idTenpo <= 0 ? [] : (await QuerySqlListAsync<SummaryRealStock>(
				$"SELECT * FROM {nameof(SummaryRealStock)} WHERE Id_Soko = @0 AND Id_Shohin = @1",
				[idTenpo.ToString(CultureInfo.InvariantCulture), shohin.Id.ToString(CultureInfo.InvariantCulture)], ct))
			.GroupBy(x => (x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su) - g.Sum(x => x.ReserveQty));
		EntrySkus = [.. colsiz.DistinctBy(c => (c.Id_Col, c.Id_Siz))
			.Select(c => new CustomerReservationSku(c, stock.GetValueOrDefault((c.Id_Col, c.Id_Siz))))];
		EntrySku = EntrySkus.FirstOrDefault(s => previous != null && s.Id_Col == previous.Id_Col && s.Id_Siz == previous.Id_Siz)
			?? EntrySkus.FirstOrDefault();
	}

	/// <summary>店舗×SKU の有効在庫（実在庫 − 引当）</summary>
	async Task<int> LoadYukoAsync(long idShohin, long idCol, long idSiz, CancellationToken ct) {
		var rows = await QuerySqlListAsync<SummaryRealStock>(
			$"SELECT * FROM {nameof(SummaryRealStock)} WHERE Id_Soko = @0 AND Id_Shohin = @1 AND Id_Col = @2 AND Id_Siz = @3",
			[.. new[] { idTenpo, idShohin, idCol, idSiz }.Select(x => x.ToString(CultureInfo.InvariantCulture))], ct);
		return rows.Sum(x => x.Su - x.ReserveQty);
	}

	/// <summary>取置日時点の店舗の上代（売上変換の単価になる。判断 3）</summary>
	async Task<int> LoadJodaiAsync(MasterShohin shohin, string day, CancellationToken ct) {
		List<string> parameters = [];
		var shohinParam = AddParameter(parameters, shohin.Id);
		var sql = $"""
			SELECT M.Id AS Id, {DerivedJodai.FinalJodaiSql(shohinParam, AddParameter(parameters, (int)EnumJodaiTaisho.Tenpo), AddParameter(parameters, idTenpo), AddParameter(parameters, day), "M")} AS TankaJodai
			FROM {nameof(MasterShohin)} M WHERE M.Id = {shohinParam}
			""";
		var found = (await QuerySqlListAsync<MasterShohin>(sql, parameters, ct)).FirstOrDefault();
		return found?.TankaJodai ?? shohin.TankaJodai;
	}

	async Task<Dictionary<long, T>> LoadByIdsAsync<T>(IEnumerable<long> ids, CancellationToken ct) where T : BaseDbClass {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		if (list.Count == 0) return [];
		return (await QuerySqlListAsync<T>($"SELECT * FROM {typeof(T).Name} WHERE Id IN ({string.Join(",", list)})", [], ct))
			.ToDictionary(x => x.Id);
	}

	// ===== 実行 =====

	async Task SaveAsync(TranHaibun original, TranHaibun replaced, string done, CancellationToken ct) {
		StartBusy("保存中...");
		try {
			await CoreServiceClient.SaveHaibunAsync([original], [replaced], "取置", ct);
			await LoadRowsAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} {done}";
		}
		catch (OperationCanceledException) {
			Message = "保存を中断しました";
		}
		catch (Exception ex) {
			Message = $"保存失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	async Task ExecuteAsync(string busy, string label, object param, Func<CvMsg, string> done, CancellationToken ct) {
		StartBusy(busy);
		try {
			var reply = await CoreServiceClient.SendExecuteAsync(param, ct);
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				Message = $"他端末で更新されたため{label}していません。再検索してください。";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}
			if (reply.Code < 0) {
				var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
				Message = $"{label}に失敗しました。{detail}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			var text = done(reply);
			await LoadRowsAsync(ct);
			Message = text;
			MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = $"{label}を中断しました";
		}
		catch (Exception ex) {
			Message = $"{label}に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	List<CustomerReservationRow> CheckedOpenRows() {
		var rows = Rows.Where(r => r.IsChecked && r.IsOpen).ToList();
		if (rows.Count == 0) {
			MessageEx.ShowWarningDialog("取置中の行をチェックしてください。", owner: ActiveWindow);
		}
		return rows;
	}

	CustomerReservationRow? SingleCheckedOpenRow() {
		var rows = Rows.Where(r => r.IsChecked && r.IsOpen).ToList();
		if (rows.Count != 1) {
			MessageEx.ShowWarningDialog("変更する取置中の行を1行だけチェックしてください。", owner: ActiveWindow);
			return null;
		}
		return rows[0];
	}

	/// <summary>期限日が今日より前なら、次の自動取消（翌日0:50）で取り消されることを確認する</summary>
	bool ConfirmPastLimit(DateTime limit) => limit.Date >= DateTime.Today || MessageEx.ShowQuestionDialog(
		$"期限日 {limit:yyyy/MM/dd} は今日より前です。次の自動取消で期限切れとして取り消されます。よろしいですか？",
		owner: ActiveWindow) == MessageBoxResult.Yes;

	bool ConfirmShortage(int yuko, int su) => MessageEx.ShowQuestionDialog(
		$"店舗の有効在庫（{yuko:N0}）を超えて {su:N0} 点を取り置こうとしています。\n在庫が足りないまま取り置くと、売上変換時に店舗在庫がマイナスになります。登録しますか？",
		owner: ActiveWindow) == MessageBoxResult.Yes;

	/// <summary>洗い替えで入れ直す行。Id・監査値はサーバが付け直す</summary>
	static TranHaibun CopyForReplace(TranHaibun h) => new() {
		Kubun = h.Kubun,
		DenDay = h.DenDay,
		NouhinDay = h.NouhinDay,
		LimitDay = h.LimitDay,
		Id_Tenpo = h.Id_Tenpo,
		Id_Soko = h.Id_Soko,
		Id_Customer = h.Id_Customer,
		Id_Shohin = h.Id_Shohin,
		JanCode = h.JanCode,
		Id_Col = h.Id_Col,
		Id_Siz = h.Id_Siz,
		Su = h.Su,
		Tanka = h.Tanka,
		Kingaku = h.Kingaku,
		Jodai = h.Jodai,
		Gedai = h.Gedai,
		Memo = h.Memo,
		Id_Shain = h.Id_Shain,
	};

	static ReservationRowRef ToRef(CustomerReservationRow r) => new(r.Source.Id, r.Source.Vdu);

	// ===== 共通ヘルパー =====

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	TResult? ShowSelect<TResult>(Type tableType, string where, string order) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	void StartBusy(string message) {
		IsBusy = true;
		Message = message;
		ClientLib.Cursor2Wait();
	}

	void FinishBusy() {
		IsBusy = false;
		ClientLib.Cursor2Normal();
	}

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	static string ToYmd8(DateTime? value) => value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>登録欄の色サイズ（店舗の有効在庫を横に出す）</summary>
public sealed class CustomerReservationSku(DerivedShohinColSiz colsiz, int yukoSu) {
	public long Id_Col { get; } = colsiz.Id_Col;
	public long Id_Siz { get; } = colsiz.Id_Siz;
	public string JanCode { get; } = colsiz.Jan1;
	public string Display { get; } = $"{colsiz.Code_Col} {colsiz.Mei_Col} / {colsiz.Code_Siz} {colsiz.Mei_Siz}".Trim();
	/// <summary>店舗の有効在庫（実在庫 − 引当）</summary>
	public int YukoSu { get; } = yukoSu;
	public string ListDisplay => $"{Display}（有効在庫 {YukoSu:N0}）";
}

/// <summary>取置一覧の1行</summary>
public sealed partial class CustomerReservationRow(TranHaibun source, DateTime today) : ObservableObject {
	public TranHaibun Source { get; } = source;

	[ObservableProperty]
	public partial bool IsChecked { get; set; }

	public string CustomerDisplay { get; init; } = string.Empty;
	public string ShohinDisplay { get; init; } = string.Empty;
	public string ColSizDisplay { get; init; } = string.Empty;
	public int Su => Source.Su;
	public string Memo => Source.Memo;
	public string DenDayDisp => FormatYmd(Source.DenDay);
	public string LimitDayDisp => FormatYmd(Source.LimitDay);

	/// <summary>取置中（未完了）か。売上変換・取消・変更はこの行だけ</summary>
	public bool IsOpen => Source.EndFlag == 0;

	/// <summary>期限日までの残日数（当日は0、過ぎると負）。取置中だけ</summary>
	public int? RemainDays => IsOpen && DateTime.TryParseExact(Source.LimitDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
		? (d - today.Date).Days
		: null;

	public string RemainDaysDisp => RemainDays is int r ? (r < 0 ? $"超過{-r}日" : $"{r}日") : string.Empty;

	/// <summary>期限が近い（残日数が <see cref="CustomerReservationAllocationInputViewModel.NearLimitDays"/> 以内）</summary>
	public bool IsNearLimit => RemainDays is int r && r >= 0 && r <= CustomerReservationAllocationInputViewModel.NearLimitDays;

	/// <summary>期限切れ（自動取消の前）</summary>
	public bool IsOverdue => RemainDays is < 0;

	public string StatusText => IsOpen ? "取置中" : (EnumHaibunEndReason)Source.EndReason switch {
		EnumHaibunEndReason.Converted => "売上変換",
		EnumHaibunEndReason.Cancelled => "取消",
		EnumHaibunEndReason.Expired => "期限切れ",
		_ => "完了",
	};

	/// <summary>売上変換で作った店舗売上のId</summary>
	public string SlipDisp => Source.EndReason == (int)EnumHaibunEndReason.Converted && Source.RelateNo2 > 0
		? Source.RelateNo2.ToString(CultureInfo.InvariantCulture)
		: string.Empty;

	public string EndDayDisp => IsOpen ? string.Empty : FormatYmd(Source.KakuteiDay);

	static string FormatYmd(string? ymd) =>
		DateTime.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : string.Empty;
}
