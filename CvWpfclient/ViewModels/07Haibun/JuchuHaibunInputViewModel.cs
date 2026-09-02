using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 受注配分入力画面の ViewModel。
/// <para>
/// 受注データ(<see cref="Tran12Jyuchu"/>)を受注No(=Id)で特定し、その明細(受注残)へ
/// 配分数を入力して <see cref="TranHaibun"/> を作成・修正する。
/// 配分区分は <see cref="EnumHaibun.Juchu"/>(受注配分)、<see cref="TranHaibun.RelateNo1"/> に受注Id を入れる。
/// 配分先(<see cref="TranHaibun.Id_Tenpo"/>)と出庫元倉庫(<see cref="TranHaibun.Id_Soko"/>)は
/// 受注ヘッダの得意先・倉庫で決まる。
/// </para>
/// <para>
/// 旧CV.netは「倉庫＋商品を選び SKU行×得意先列のクロス表へ入力する」商品単位の画面だったが、
/// CV10 は<b>受注伝票まるごとを1配分として扱う</b>（ユーザー確定 2026-08-21）。
/// 受注残・有効在庫の算式、超過の扱いは `Doc/spec/archive/2026-08-21_受注配分入力_詳細設計.md` を参照する。
/// </para>
/// </summary>
public partial class JuchuHaibunInputViewModel : BaseViewModel {
	/// <summary>配分区分。本画面は受注配分のみを作る。</summary>
	public const int KubunJuchu = (int)EnumHaibun.Juchu;

	/// <summary>
	/// 本画面が修正できる配分の条件（未送信かつ未確定かつ未完了）。
	/// この条件から外れた配分は受注残から差し引く「確定済配分」として扱う。
	/// </summary>
	const string EditableCondition = "T.SendFlg = 0 AND IFNULL(T.KakuteiDay, '') = '' AND T.EndFlag = 0";

	Tran12Jyuchu? targetJuchu;

	/// <summary>修正対象として読み込んだ既存配分。登録時の洗い替え削除に使う（Id/Vdu 保持）。</summary>
	List<TranHaibun> loadedEditableRows = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoSearchCommand))]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoRegisterCommand))]
	[NotifyCanExecuteChangedFor(nameof(ClearAllCommand))]
	[NotifyCanExecuteChangedFor(nameof(LoadJuchuZanCommand))]
	[NotifyCanExecuteChangedFor(nameof(SpreadSameSuCommand))]
	[NotifyCanExecuteChangedFor(nameof(SelectJuchuCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== タブ1: 検索条件 =====

	[ObservableProperty]
	public partial string JuchuNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string JuchuNoTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? JuchuDayFrom { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

	[ObservableProperty]
	public partial DateTime? JuchuDayTo { get; set; }

	[ObservableProperty]
	public partial DateTime? ShijiDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? ShijiDayTo { get; set; }

	[ObservableProperty]
	public partial DateTime? NouhinDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? NouhinDayTo { get; set; }

	[ObservableProperty]
	public partial long CondId_Tokui { get; set; }

	[ObservableProperty]
	public partial string CondTokuiDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial long CondId_Shain { get; set; }

	[ObservableProperty]
	public partial string CondShainDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CondShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CondShohinCodeTo { get; set; } = string.Empty;

	/// <summary>取引区分（受注ヘッダ <see cref="Tran12Jyuchu.Kubun"/>）。null は全て。</summary>
	[ObservableProperty]
	public partial int? CondKubun { get; set; }

	/// <summary>配分状況（<see cref="HaibunJokyoOptions"/> のインデックス）。0:全て 1:未配分 2:配分済</summary>
	[ObservableProperty]
	public partial int CondHaibunJokyo { get; set; }

	[ObservableProperty]
	public partial int CondMaxCount { get; set; } = AppGlobal.Limit;

	public IReadOnlyList<JuchuKubunOption> KubunOptions { get; } = [
		new(null, "(全て)"),
		new((int)EnumJuchu.Juchu, "10 受注"),
		new((int)EnumJuchu.Henpin, "20 受注返品"),
		new((int)EnumJuchu.Nebiki, "30 値引"),
		new((int)EnumJuchu.Other, "99 その他"),
	];

	public IReadOnlyList<string> HaibunJokyoOptions { get; } = ["全て", "未配分のみ", "配分ありのみ"];

	// ===== タブ1: 一覧 =====

	[ObservableProperty]
	public partial ObservableCollection<JuchuHaibunListRow> SearchRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteCommand))]
	public partial JuchuHaibunListRow? SelectedSearchRow { get; set; }

	[ObservableProperty]
	public partial int SearchCount { get; set; }

	// ===== タブ2: ヘッダ =====

	[ObservableProperty]
	public partial long JuchuNo { get; set; }

	[ObservableProperty]
	public partial string JuchuDayDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string KubunDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? ShijiDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? NouhinDay { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial long Id_Shain { get; set; }

	[ObservableProperty]
	public partial string ShainDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Memo { get; set; } = string.Empty;

	/// <summary>受注全体の受注数合計</summary>
	[ObservableProperty]
	public partial int JuchuTotalSu { get; set; }

	/// <summary>受注全体の受注残合計（配分できる残）</summary>
	[ObservableProperty]
	public partial int JuchuZanTotalSu { get; set; }

	/// <summary>入力中の配分数合計</summary>
	[ObservableProperty]
	public partial int GrandTotalSu { get; set; }

	/// <summary>同数展開で入れる数量（旧画面の「同数展開」）</summary>
	[ObservableProperty]
	public partial int SpreadSu { get; set; }

	// ===== タブ2: 明細 =====

	[ObservableProperty]
	public partial ObservableCollection<JuchuHaibunMeisaiRow> MeisaiRows { get; set; } = [];

	// ===== コマンド =====

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;
	bool CanGoToEdit() => IsListTabSelected() && SelectedSearchRow != null;
	bool CanDelete() => IsListTabSelected() && SelectedSearchRow is { HaibunSu: > 0 };

	[RelayCommand]
	Task Init(CancellationToken ct) => DoSearch(ct);

	/// <summary>一覧取得(F5)。受注を主に取得し、出荷済・配分の集計をクライアントで合成する。</summary>
	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		if (JuchuDayFrom != null && JuchuDayTo != null && JuchuDayFrom > JuchuDayTo) {
			MessageEx.ShowWarningDialog("受注日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}

		StartBusy("一覧取得中...");
		try {
			List<Tran12Jyuchu> juchuList = await LoadJuchuListAsync(ct);
			List<long> ids = [.. juchuList.Select(x => x.Id)];
			Dictionary<int, TranHaibun> haibunMap = await LoadHaibunSummaryAsync(ids, ct);
			Dictionary<int, TranHaibun> zanMap = await LoadJuchuZanSummaryAsync(ids, ct);

			ObservableCollection<JuchuHaibunListRow> rows = [];
			foreach (Tran12Jyuchu juchu in juchuList) {
				haibunMap.TryGetValue((int)juchu.Id, out TranHaibun? haibun);
				zanMap.TryGetValue((int)juchu.Id, out TranHaibun? zan);
				rows.Add(new JuchuHaibunListRow(juchu, haibun, zan));
			}
			// 配分状況の絞り込みは SQL 側（EXISTS / NOT EXISTS）で済んでいる。
			// 一覧の「配分状況」列は 未配分 / 一部配分 / 配分済 を未配分残から判定して表示する。
			SearchRows = rows;
			SearchCount = SearchRows.Count;
			SelectedSearchRow = SearchRows.FirstOrDefault();
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 受注を {SearchCount:N0} 件取得しました";
		}
		catch (OperationCanceledException) {
			Message = "一覧取得を中断しました";
		}
		catch (Exception ex) {
			Message = $"一覧取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void ClearConditions() {
		JuchuNoFrom = string.Empty;
		JuchuNoTo = string.Empty;
		JuchuDayFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		JuchuDayTo = null;
		ShijiDayFrom = null;
		ShijiDayTo = null;
		NouhinDayFrom = null;
		NouhinDayTo = null;
		CondId_Tokui = 0;
		CondTokuiDisplay = string.Empty;
		CondId_Shain = 0;
		CondShainDisplay = string.Empty;
		CondShohinCodeFrom = string.Empty;
		CondShohinCodeTo = string.Empty;
		CondKubun = null;
		CondHaibunJokyo = 0;
		CondMaxCount = AppGlobal.Limit;
	}

	[RelayCommand]
	void SelectCondTokui() {
		var tokui = ShowSelect<MasterTokui>(typeof(MasterTokui), string.Empty, "Code", CondId_Tokui);
		if (tokui == null) return;
		CondId_Tokui = tokui.Id;
		CondTokuiDisplay = CodeNameDisplay.Format(tokui.Id, tokui.Code, tokui.Name);
	}

	[RelayCommand]
	void SelectCondShain() {
		var shain = ShowSelect<MasterShain>(typeof(MasterShain), string.Empty, "Code", CondId_Shain);
		if (shain == null) return;
		CondId_Shain = shain.Id;
		CondShainDisplay = CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	/// <summary>配分入力へ(F6)。選択受注の明細と既存配分を読み込んでタブ2を構築する。</summary>
	[RelayCommand(CanExecute = nameof(CanGoToEdit), IncludeCancelCommand = true)]
	async Task GoToEdit(CancellationToken ct) {
		if (SelectedSearchRow == null) return;

		StartBusy("配分データ取得中...");
		try {
			await LoadEntryAsync(SelectedSearchRow.Id, ct);
			SelectedTabIndex = 1;
			Message = $"受注No {JuchuNo:N0} の配分入力を開始します";
		}
		catch (OperationCanceledException) {
			Message = "配分データ取得を中断しました";
		}
		catch (Exception ex) {
			Message = $"配分データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void GoToSearch() => SelectedTabIndex = 0;

	/// <summary>
	/// 受注No の選択。汎用伝票選択ダイアログ(<see cref="Views.Sub.SelectTranWinView"/>)から
	/// 受注を選び、その配分入力へ切り替える。一覧を経由せずに受注Noから直接入りたい場合の導線。
	/// </summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task SelectJuchu(CancellationToken ct) {
		var win = new Views.Sub.SelectTranWinView();
		if (win.DataContext is not Sub.SelectTranWinViewModel vm) return;
		vm.SetParam(typeof(Tran12Jyuchu), where: "CalcFlag <> 0 AND EndFlag = 0", order: "Id DESC",
			startPos: JuchuNo, title: "受注選択", torisakiHeader: "得意先", kubunLabels: JuchuKubunLabels);
		if (ClientLib.ShowDialogView(win, this) != true) return;
		if (vm.GetCurrent<Tran12Jyuchu>() is not { } juchu || juchu.Id == JuchuNo) return;

		// 切り替えると入力途中の配分数は失われるので、読み込み済みの伝票があるときは確認する。
		if (targetJuchu != null &&
			MessageEx.ShowQuestionDialog(
				$"入力中の内容は破棄されます。受注No {juchu.Id:N0} の配分入力に切り替えますか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		StartBusy("配分データ取得中...");
		try {
			await LoadEntryAsync(juchu.Id, ct);
			Message = $"受注No {JuchuNo:N0} の配分入力を開始します";
		}
		catch (OperationCanceledException) {
			Message = "配分データ取得を中断しました";
		}
		catch (Exception ex) {
			Message = $"配分データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>受注選択ダイアログへ渡す区分の表示名。</summary>
	static readonly Dictionary<int, string> JuchuKubunLabels = new() {
		[(int)EnumJuchu.Juchu] = "受注",
		[(int)EnumJuchu.Henpin] = "受注返品",
		[(int)EnumJuchu.Nebiki] = "値引",
		[(int)EnumJuchu.Other] = "その他",
	};

	/// <summary>削除(F7)。選択受注に紐づく編集対象の配分をまとめて削除する。</summary>
	[RelayCommand(CanExecute = nameof(CanDelete), IncludeCancelCommand = true)]
	async Task DoDelete(CancellationToken ct) {
		if (SelectedSearchRow is not { } row) return;
		if (MessageEx.ShowQuestionDialog(
				$"受注No {row.Id:N0} の配分（{row.HaibunSu:N0} 点）を削除します。よろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		StartBusy("配分データ削除中...");
		try {
			List<TranHaibun> targets = await LoadEditableHaibunAsync(row.Id, ct);
			if (targets.Count == 0) {
				Message = "削除できる配分（未送信・未確定）がありません";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}
			await DeleteHaibunRowsAsync(targets, ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 配分 {targets.Count:N0} 件を削除しました";
			await DoSearch(ct);
			MessageEx.ShowInformationDialog("削除しました。", owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "削除を中断しました";
		}
		catch (Exception ex) {
			Message = $"削除失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	void SelectShain() {
		var shain = ShowSelect<MasterShain>(typeof(MasterShain), string.Empty, "Code", Id_Shain);
		if (shain == null) return;
		Id_Shain = shain.Id;
		ShainDisplay = CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	/// <summary>受注残読込。全明細の配分数を受注残で上書きする。旧画面の［受注残読込］に対応する。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected))]
	void LoadJuchuZan() {
		if (MeisaiRows.Count == 0) return;
		if (MessageEx.ShowQuestionDialog("受注残数量を読込みます。入力済みの数量は上書きされますがよろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		foreach (JuchuHaibunMeisaiRow row in MeisaiRows) row.Su = row.JuchuZanSu;
		Message = $"受注残 {JuchuZanTotalSu:N0} 点を読込みました";
	}

	/// <summary>同数展開。全明細へ同じ数量を入れる。旧画面の［同数展開］［全SKU］に対応する。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected))]
	void SpreadSameSu() {
		if (MeisaiRows.Count == 0) return;
		foreach (JuchuHaibunMeisaiRow row in MeisaiRows) row.Su = SpreadSu;
		Message = $"全SKUへ {SpreadSu:N0} を展開しました";
	}

	/// <summary>全クリア(Shift+F6)。入力済みの配分数をすべて 0 に戻す。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected))]
	void ClearAll() {
		if (MeisaiRows.Count == 0) return;
		if (MessageEx.ShowQuestionDialog("入力した配分数をすべて 0 に戻します。よろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		foreach (JuchuHaibunMeisaiRow row in MeisaiRows) row.Su = 0;
		Message = "配分数をクリアしました";
	}

	/// <summary>登録(F2)。編集対象の配分を洗い替えし、配分数&gt;0 の明細を一括登録する。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (targetJuchu == null) return;
		if (ShijiDay == null) {
			MessageEx.ShowWarningDialog("配分指示日を入力してください", owner: ActiveWindow);
			return;
		}
		if (targetJuchu.Id_Tokui <= 0 || targetJuchu.Id_Soko <= 0) {
			MessageEx.ShowWarningDialog(
				"受注に得意先または倉庫が設定されていないため登録できません。受注入力で設定してください。",
				owner: ActiveWindow);
			return;
		}

		// 有効在庫割れは警告のみ（出荷指示確定でエラーになる）。詳細設計 2.5
		List<JuchuHaibunMeisaiRow> overStock = [.. MeisaiRows.Where(x => x.Su > x.HaibunKanoSu)];
		if (overStock.Count > 0 &&
			MessageEx.ShowQuestionDialog(
				$"配分可能数（有効在庫）を超えている色サイズが {overStock.Count:N0} 件あります"
				+ $"（超過 {overStock.Sum(x => x.Su - x.HaibunKanoSu):N0} 点）。このまま登録しますか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		// 受注残超過も警告のみ。超過分は RelateNo1 = 0 で登録する。詳細設計 2.5
		int overJuchu = MeisaiRows.Sum(x => Math.Max(x.Su - x.JuchuZanSu, 0));
		if (overJuchu > 0 &&
			MessageEx.ShowQuestionDialog(
				$"受注残を超える配分が {overJuchu:N0} 点あります。超過分は受注に紐づかない配分として登録されます。よろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		List<TranHaibun> newRecords = BuildNewRecords();
		if (newRecords.Count == 0 && loadedEditableRows.Count == 0) {
			MessageEx.ShowWarningDialog("配分数を入力してください", owner: ActiveWindow);
			return;
		}
		string confirm = newRecords.Count == 0
			? $"配分数が全て0のため、既存の配分 {loadedEditableRows.Count:N0} 件を削除します。よろしいですか？"
			: $"配分 {newRecords.Count:N0} 件（合計 {newRecords.Sum(x => x.Su):N0} 点）を登録します。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("配分データ登録中...");
		try {
			// 洗い替え: 読込済みの編集対象を削除してから一括Insertする
			await DeleteHaibunRowsAsync(loadedEditableRows, ct);

			if (newRecords.Count > 0) {
				var coreService = AppGlobal.GetGrpcService<ICoreService>();
				var insertMsg = new CvMsg {
					Code = 0,
					Flag = CvFlag.Msg201_Op_Execute,
					DataType = typeof(InsertBulkParam),
					DataMsg = Common.SerializeObject(new InsertBulkParam(typeof(TranHaibun), JsonConvert.SerializeObject(newRecords))),
				};
				CvMsg insertReply = await coreService.QueryMsgAsync(insertMsg, AppGlobal.GetDefaultCallContext(ct));
				if (insertReply.Code < 0) {
					throw new InvalidOperationException($"登録に失敗しました: {insertReply.Option ?? insertReply.DataMsg}");
				}
			}

			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 配分を {newRecords.Count:N0} 件登録しました";
			await LoadEntryAsync(targetJuchu.Id, ct);
			MessageEx.ShowInformationDialog("登録完了しました。", owner: ActiveWindow);
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

	// ===== タブ1: クエリ =====

	async Task<List<Tran12Jyuchu>> LoadJuchuListAsync(CancellationToken ct) {
		List<string> parameters = [];
		// 完了済みの受注は本画面の対象外（旧CV.netの「完了の効果」を維持）
		List<string> clauses = ["H.CalcFlag <> 0", "H.EndFlag = 0"];

		if (TryParseNo(JuchuNoFrom, out long noFrom)) clauses.Add($"H.Id >= {AddParameter(parameters, noFrom)}");
		if (TryParseNo(JuchuNoTo, out long noTo)) clauses.Add($"H.Id <= {AddParameter(parameters, noTo)}");
		AddYmdRange(clauses, parameters, "H.DenDay", JuchuDayFrom, JuchuDayTo);
		if (CondId_Tokui > 0) clauses.Add($"H.Id_Tokui = {AddParameter(parameters, CondId_Tokui)}");
		if (CondKubun is int kubun) clauses.Add($"H.Kubun = {AddParameter(parameters, kubun)}");

		// 商品CD範囲は受注明細(JSON)を展開して判定する。不正JSONは空配列として扱う。
		List<string> meisaiClauses = [];
		AddCodeRange(meisaiClauses, parameters, "json_extract(m.value, '$.Code_Shohin')", CondShohinCodeFrom, CondShohinCodeTo);
		if (meisaiClauses.Count > 0) {
			clauses.Add($"""
				EXISTS (SELECT 1 FROM json_each({SafeJmeisai("H")}) AS m WHERE {string.Join(" AND ", meisaiClauses)})
				""");
		}

		// 配分側の条件（指示日・納品日・入力者）と配分状況は EXISTS / NOT EXISTS で受注へ掛ける。
		List<string> haibunClauses = [
			$"T.Kubun = {AddParameter(parameters, KubunJuchu)}",
			"T.EndFlag = 0",
			"T.RelateNo1 = H.Id",
		];
		AddYmdRange(haibunClauses, parameters, "T.DenDay", ShijiDayFrom, ShijiDayTo);
		AddYmdRange(haibunClauses, parameters, "T.NouhinDay", NouhinDayFrom, NouhinDayTo);
		if (CondId_Shain > 0) haibunClauses.Add($"T.Id_Shain = {AddParameter(parameters, CondId_Shain)}");
		string existsSql = $"EXISTS (SELECT 1 FROM TranHaibun T WHERE {string.Join(" AND ", haibunClauses)})";

		// 配分状況が「全て」でも、配分側の条件が指定されていれば絞り込みは必要になる。
		bool hasHaibunCondition = ShijiDayFrom != null || ShijiDayTo != null
			|| NouhinDayFrom != null || NouhinDayTo != null || CondId_Shain > 0;
		switch (CondHaibunJokyo) {
			case 1:
				clauses.Add($"NOT {existsSql}");
				break;
			case 2:
				clauses.Add(existsSql);
				break;
			default:
				if (hasHaibunCondition) clauses.Add(existsSql);
				break;
		}

		string limit = CondMaxCount > 0 ? $"LIMIT {CondMaxCount}" : string.Empty;
		// 明細(Jmeisai)は一覧では使わないので取らない。タブ2で受注1件を読み直す
		string sql = $"""
			SELECT
				H.Id, H.Vdc, H.Vdu, H.DenDay, H.NouhinDay, H.Id_Tokui, H.VTokui, H.Id_Soko, H.VSoko,
				H.Kubun, H.SuTotal, H.KingakuTotal, H.Id_Shain, H.VShain, H.Memo
			FROM Tran12Jyuchu H
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY H.Id DESC
			{limit}
			""";
		return await QuerySqlListAsync<Tran12Jyuchu>(sql, parameters, ct);
	}

	/// <summary>受注Id別の配分サマリ。受け皿は <see cref="TranHaibun"/> へ別名射影する。</summary>
	async Task<Dictionary<int, TranHaibun>> LoadHaibunSummaryAsync(IReadOnlyCollection<long> juchuIds, CancellationToken ct) {
		if (juchuIds.Count == 0) return [];
		List<string> parameters = [];
		string inClause = BuildInClause("T.RelateNo1", juchuIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.RelateNo1,
				MIN(T.DenDay) AS DenDay,
				MIN(T.NouhinDay) AS NouhinDay,
				MIN(T.Id_Shain) AS Id_Shain,
				IFNULL(SUM(T.Su), 0) AS Su
			FROM TranHaibun T
			WHERE T.Kubun = {AddParameter(parameters, KubunJuchu)}
				AND T.EndFlag = 0
				AND {inClause}
			GROUP BY T.RelateNo1
			""";
		List<TranHaibun> rows = await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
		return rows.GroupBy(x => x.RelateNo1).ToDictionary(g => g.Key, g => g.First());
	}

	/// <summary>
	/// 受注Id別の出荷済数と未配分残。受け皿は <see cref="TranHaibun"/> で
	/// <c>JitsuSu</c>=出荷済数 / <c>Su</c>=未配分残 へ射影する（詳細設計 2.3）。
	/// </summary>
	async Task<Dictionary<int, TranHaibun>> LoadJuchuZanSummaryAsync(IReadOnlyCollection<long> juchuIds, CancellationToken ct) {
		if (juchuIds.Count == 0) return [];
		List<string> parameters = [];
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				Z.Id_Juchu AS RelateNo1,
				IFNULL(SUM(IFNULL(S.ShukkaSu, 0)), 0) AS JitsuSu,
				IFNULL(SUM(MAX(Z.JuchuSu - IFNULL(S.ShukkaSu, 0) - IFNULL(H.HaibunSu, 0), 0)), 0) AS Su
			FROM {JuchuSkuSql(parameters, juchuIds)}
			GROUP BY Z.Id_Juchu
			""";
		List<TranHaibun> rows = await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
		return rows.GroupBy(x => x.RelateNo1).ToDictionary(g => g.Key, g => g.First());
	}

	/// <summary>
	/// 受注明細(別名 Z)へ出荷済(S)・未完了配分(H)を突き合わせた FROM 句を作る。
	/// いずれも「受注Id × SKU」単位で結合する。
	/// </summary>
	string JuchuSkuSql(List<string> parameters, IReadOnlyCollection<long> juchuIds) {
		string idList = string.Join(",", juchuIds.Select(x => x.ToString(CultureInfo.InvariantCulture)));
		string kubun = AddParameter(parameters, KubunJuchu);
		return $"""
			(
					SELECT
						J.Id AS Id_Juchu,
						{MeisaiNum("m", "Id_Shohin")} AS Id_Shohin,
						{MeisaiNum("m", "Id_Col")} AS Id_Col,
						{MeisaiNum("m", "Id_Siz")} AS Id_Siz,
						SUM({MeisaiNum("m", "Su")} * J.CalcFlag) AS JuchuSu
					FROM Tran12Jyuchu J, json_each({SafeJmeisai("J")}) AS m
					WHERE J.Id IN ({idList})
					GROUP BY 1, 2, 3, 4
				) Z
				LEFT JOIN (
					SELECT
						U.RelateNo1 AS Id_Juchu,
						{MeisaiNum("um", "Id_Shohin")} AS Id_Shohin,
						{MeisaiNum("um", "Id_Col")} AS Id_Col,
						{MeisaiNum("um", "Id_Siz")} AS Id_Siz,
						SUM({MeisaiNum("um", "Su")} * U.CalcFlag) AS ShukkaSu
					FROM Tran00Uriage U, json_each({SafeJmeisai("U")}) AS um
						INNER JOIN MasterTokui UT ON UT.Id = U.Id_Tokui
							AND UT.TenType IN ({TranCalcBase.ShukkaTenTypes})
					WHERE U.CalcFlag <> 0 AND U.RelateNo1 IN ({idList})
					GROUP BY 1, 2, 3, 4
				) S ON S.Id_Juchu = Z.Id_Juchu AND S.Id_Shohin = Z.Id_Shohin
					AND S.Id_Col = Z.Id_Col AND S.Id_Siz = Z.Id_Siz
				LEFT JOIN (
					SELECT
						T.RelateNo1 AS Id_Juchu, T.Id_Shohin, T.Id_Col, T.Id_Siz,
						SUM(T.Su) AS HaibunSu
					FROM TranHaibun T
					WHERE T.Kubun = {kubun} AND T.EndFlag = 0 AND T.RelateNo1 IN ({idList})
					GROUP BY 1, 2, 3, 4
				) H ON H.Id_Juchu = Z.Id_Juchu AND H.Id_Shohin = Z.Id_Shohin
					AND H.Id_Col = Z.Id_Col AND H.Id_Siz = Z.Id_Siz
			""";
	}

	// ===== タブ2: 構築 =====

	async Task LoadEntryAsync(long juchuId, CancellationToken ct) {
		List<Tran12Jyuchu> found = await QueryListAsync<Tran12Jyuchu>($"Id = {juchuId}", "Id", ct);
		targetJuchu = found.FirstOrDefault()
			?? throw new InvalidOperationException($"受注No {juchuId} が見つかりません。");

		JuchuNo = targetJuchu.Id;
		JuchuDayDisplay = FormatYmd8(targetJuchu.DenDay);
		TokuiDisplay = FormatCodeName(targetJuchu.VTokui);
		SokoDisplay = FormatCodeName(targetJuchu.VSoko);
		KubunDisplay = FormatJuchuKubun(targetJuchu.Kubun);

		// 受注明細を SKU 単位へ正規化（同一SKUの複数行は合算）
		List<JuchuMeisaiSku> skus = NormalizeMeisai(targetJuchu.Jmeisai);
		JuchuTotalSu = skus.Sum(x => x.JuchuSu);

		// 既存配分（洗い替え対象）
		loadedEditableRows = await LoadEditableHaibunAsync(juchuId, ct);
		Dictionary<JuchuSkuKey, int> editingMap = loadedEditableRows
			.GroupBy(x => new JuchuSkuKey(x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		// SKU別の出荷済・確定済配分・在庫・引当
		Dictionary<JuchuSkuKey, TranHaibun> shukkaMap = await LoadSkuShukkaMapAsync(juchuId, ct);
		Dictionary<JuchuSkuKey, SummaryRealStock> realMap = await LoadSkuRealMapAsync(targetJuchu.Id_Soko, skus, ct);

		ObservableCollection<JuchuHaibunMeisaiRow> rows = [];
		foreach (JuchuMeisaiSku sku in skus) {
			JuchuSkuKey key = new(sku.Id_Shohin, sku.Id_Col, sku.Id_Siz);
			TranHaibun? actual = shukkaMap.GetValueOrDefault(key);
			SummaryRealStock? real = realMap.GetValueOrDefault(key);
			var row = new JuchuHaibunMeisaiRow(sku) {
				ShukkaSu = actual?.JitsuSu ?? 0,
				KakuteiHaibunSu = actual?.Su ?? 0,
				ZaikoSu = real?.Su ?? 0,
				ReserveSu = real?.ReserveQty ?? 0,
				EditingSu = editingMap.GetValueOrDefault(key),
				Changed = OnRowChanged,
			};
			row.Su = editingMap.GetValueOrDefault(key);
			rows.Add(row);
		}
		MeisaiRows = rows;

		// 受注明細に無いSKUの配分が残っていれば知らせる（受注が後から修正された場合）
		var validKeys = skus.Select(x => new JuchuSkuKey(x.Id_Shohin, x.Id_Col, x.Id_Siz)).ToHashSet();
		int orphan = loadedEditableRows
			.Count(x => !validKeys.Contains(new JuchuSkuKey(x.Id_Shohin, x.Id_Col, x.Id_Siz)));
		if (orphan > 0) {
			MessageEx.ShowWarningDialog(
				$"受注明細に存在しない配分が {orphan:N0} 件あります。受注が修正された可能性があります。\n登録すると、これらの配分は削除されます。",
				owner: ActiveWindow);
		}

		TranHaibun? first = loadedEditableRows.FirstOrDefault();
		ShijiDay = FromYmd8(first?.DenDay) ?? DateTime.Today;
		NouhinDay = FromYmd8(first?.NouhinDay) ?? FromYmd8(targetJuchu.NouhinDay) ?? DateTime.Today;
		Memo = first?.Memo ?? string.Empty;
		if (first is { Id_Shain: > 0 }) {
			Id_Shain = first.Id_Shain;
			ShainDisplay = await LoadShainDisplayAsync(first.Id_Shain, ct);
		}
		else if (targetJuchu.Id_Shain > 0) {
			Id_Shain = targetJuchu.Id_Shain;
			ShainDisplay = FormatCodeName(targetJuchu.VShain);
		}

		RefreshTotals();
		await WarnIfNotShukkaTargetAsync(ct);
	}

	/// <summary>
	/// 配分先・出庫元が決まらない受注、および出荷先が卸先・売仕店でない受注を知らせる。
	/// <para>
	/// 卸先(1)・売仕店(3)以外へ配分すると、出荷処理は移動伝票を作るため受注残が消化されない（決定 I4）。
	/// 配分自体は作れるので警告だけ出す。
	/// </para>
	/// </summary>
	async Task WarnIfNotShukkaTargetAsync(CancellationToken ct) {
		if (targetJuchu == null) return;
		if (targetJuchu.Id_Tokui <= 0 || targetJuchu.Id_Soko <= 0) {
			MessageEx.ShowWarningDialog(
				"受注に得意先または倉庫が設定されていないため、この受注は配分できません。受注入力で設定してください。",
				owner: ActiveWindow);
			return;
		}
		List<MasterTokui> tokuiList = await QueryListAsync<MasterTokui>($"Id = {targetJuchu.Id_Tokui}", "Id", ct);
		int tenType = tokuiList.FirstOrDefault()?.TenType ?? 0;
		if (tenType is (int)EnumTokui._1_Oroshi or (int)EnumTokui._3_UriShi) return;
		MessageEx.ShowWarningDialog(
			$"この受注の得意先は卸先・売仕店ではありません（店種区分 {tenType}）。\n"
			+ "出荷処理では移動伝票が作られ、受注残は消化されません。",
			owner: ActiveWindow);
	}

	/// <summary>修正できる配分（未送信・未確定・未完了）を取得する。Id/Vdu は洗い替え削除に使う。</summary>
	Task<List<TranHaibun>> LoadEditableHaibunAsync(long juchuId, CancellationToken ct) =>
		QueryListAsync<TranHaibun>(
			$"Kubun = {KubunJuchu} AND RelateNo1 = {juchuId} AND SendFlg = 0 AND KakuteiDay = '' AND EndFlag = 0",
			"Id_Shohin, Id_Col, Id_Siz, Id", ct);

	/// <summary>
	/// 選択受注のSKU別 出荷済数・確定済配分数。受け皿は <see cref="TranHaibun"/> で
	/// <c>JitsuSu</c>=出荷済数 / <c>Su</c>=確定済配分数 へ射影する。
	/// </summary>
	async Task<Dictionary<JuchuSkuKey, TranHaibun>> LoadSkuShukkaMapAsync(long juchuId, CancellationToken ct) {
		List<string> parameters = [];
		string juchu = AddParameter(parameters, juchuId);
		string kubun = AddParameter(parameters, KubunJuchu);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				A.Id_Shohin, A.Id_Col, A.Id_Siz,
				IFNULL(SUM(A.ShukkaSu), 0) AS JitsuSu,
				IFNULL(SUM(A.HaibunSu), 0) AS Su
			FROM (
				SELECT
					{MeisaiNum("um", "Id_Shohin")} AS Id_Shohin,
					{MeisaiNum("um", "Id_Col")} AS Id_Col,
					{MeisaiNum("um", "Id_Siz")} AS Id_Siz,
					{MeisaiNum("um", "Su")} * U.CalcFlag AS ShukkaSu,
					0 AS HaibunSu
				FROM Tran00Uriage U, json_each({SafeJmeisai("U")}) AS um
					INNER JOIN MasterTokui UT ON UT.Id = U.Id_Tokui
						AND UT.TenType IN ({TranCalcBase.ShukkaTenTypes})
				WHERE U.CalcFlag <> 0 AND U.RelateNo1 = {juchu}
				UNION ALL
				SELECT T.Id_Shohin, T.Id_Col, T.Id_Siz, 0, T.Su
				FROM TranHaibun T
				WHERE T.Kubun = {kubun} AND T.EndFlag = 0 AND T.RelateNo1 = {juchu}
					AND NOT ({EditableCondition})
			) A
			GROUP BY A.Id_Shohin, A.Id_Col, A.Id_Siz
			""";
		List<TranHaibun> rows = await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
		return rows
			.GroupBy(x => new JuchuSkuKey(x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.First());
	}

	/// <summary>受注ヘッダの倉庫のSKU別 在庫・引当。</summary>
	async Task<Dictionary<JuchuSkuKey, SummaryRealStock>> LoadSkuRealMapAsync(
		long idSoko, IReadOnlyCollection<JuchuMeisaiSku> skus, CancellationToken ct) {
		List<long> shohinIds = [.. skus.Select(x => x.Id_Shohin).Where(x => x > 0).Distinct()];
		if (idSoko <= 0 || shohinIds.Count == 0) return [];

		List<string> parameters = [];
		string inClause = BuildInClause("R.Id_Shohin", shohinIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Shohin, 0 AS Id_Soko, R.Id_Col, R.Id_Siz,
				IFNULL(SUM(R.Su), 0) AS Su,
				IFNULL(SUM(R.ReserveQty), 0) AS ReserveQty
			FROM SummaryRealStock R
			WHERE R.Id_Soko = {AddParameter(parameters, idSoko)}
				AND {inClause}
			GROUP BY R.Id_Shohin, R.Id_Col, R.Id_Siz
			""";
		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows
			.GroupBy(x => new JuchuSkuKey(x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.First());
	}

	async Task<string> LoadShainDisplayAsync(long idShain, CancellationToken ct) {
		List<MasterShain> rows = await QueryListAsync<MasterShain>($"Id = {idShain}", "Id", ct);
		MasterShain? shain = rows.FirstOrDefault();
		// 表示書式は受注ヘッダ由来の VShain と揃える（登録前後で見た目が変わらないようにする）
		return shain == null ? $"Id:{idShain}" : CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	/// <summary>
	/// 入力済みの明細を <see cref="TranHaibun"/> へ展開する。
	/// 受注残までは <c>RelateNo1</c> = 受注Id、超過分は <c>RelateNo1</c> = 0 の別行にする（詳細設計 2.5）。
	/// </summary>
	List<TranHaibun> BuildNewRecords() {
		List<TranHaibun> records = [];
		if (targetJuchu == null) return records;
		string denDay = ToYmd8(ShijiDay);
		string nouhinDay = ToYmd8(NouhinDay);
		foreach (JuchuHaibunMeisaiRow row in MeisaiRows.Where(x => x.Su > 0)) {
			int onJuchu = Math.Min(row.Su, row.JuchuZanSu);
			if (onJuchu > 0) records.Add(CreateRecord(row, denDay, nouhinDay, onJuchu, (int)targetJuchu.Id));
			int over = row.Su - onJuchu;
			if (over > 0) records.Add(CreateRecord(row, denDay, nouhinDay, over, 0));
		}
		return records;
	}

	TranHaibun CreateRecord(JuchuHaibunMeisaiRow row, string denDay, string nouhinDay, int su, int relateNo1) => new() {
		DenDay = denDay,
		NouhinDay = nouhinDay,
		Id_Soko = targetJuchu?.Id_Soko ?? 0,
		Id_Tenpo = targetJuchu?.Id_Tokui ?? 0,
		Kubun = KubunJuchu,
		SendFlg = 0,
		Id_Shohin = row.Id_Shohin,
		JanCode = row.JanCode,
		Id_Col = row.Id_Col,
		Id_Siz = row.Id_Siz,
		Su = su,
		Tanka = row.Tanka,
		Kingaku = su * row.Tanka,
		Jodai = row.Jodai,
		Gedai = row.Gedai,
		RelateNo1 = relateNo1,
		Memo = Memo,
		Id_Shain = Id_Shain,
	};

	/// <summary>既存配分を1往復でまとめて削除する。1件でも競合すればサーバ側で何も削除されない</summary>
	Task DeleteHaibunRowsAsync(IReadOnlyCollection<TranHaibun> rows, CancellationToken ct) =>
		CoreServiceClient.DeleteBulkAsync(typeof(TranHaibun), rows, "既存配分", ct);

	// ===== 画面状態の更新 =====

	void OnRowChanged(JuchuHaibunMeisaiRow row, int delta) => GrandTotalSu += delta;

	void RefreshTotals() {
		GrandTotalSu = MeisaiRows.Sum(x => x.Su);
		JuchuZanTotalSu = MeisaiRows.Sum(x => x.JuchuZanSu);
	}

	/// <summary>受注明細を SKU 単位へ正規化する。同一 商品×色×サイズ の複数行は数量を合算する。</summary>
	static List<JuchuMeisaiSku> NormalizeMeisai(List<Tran99Meisai>? meisai) {
		Dictionary<JuchuSkuKey, JuchuMeisaiSku> map = [];
		foreach (Tran99Meisai row in meisai ?? []) {
			if (row.Id_Shohin <= 0) continue;
			var key = new JuchuSkuKey(row.Id_Shohin, row.Id_Col, row.Id_Siz);
			if (map.TryGetValue(key, out JuchuMeisaiSku? found)) {
				found.JuchuSu += row.Su;
				// 単価は最小値を採る（同一SKUで単価が違う明細が混ざった場合の保険）
				found.Tanka = Math.Min(found.Tanka, row.Tanka);
				found.Jodai = Math.Min(found.Jodai, row.Jodai);
				found.Gedai = Math.Min(found.Gedai, row.Gedai);
				continue;
			}
			map[key] = new JuchuMeisaiSku {
				Id_Shohin = row.Id_Shohin,
				Code_Shohin = row.Code_Shohin,
				Mei_Shohin = row.Mei_Shohin,
				Id_Col = row.Id_Col,
				Code_Col = row.Code_Col,
				Mei_Col = row.Mei_Col,
				Id_Siz = row.Id_Siz,
				Code_Siz = row.Code_Siz,
				Mei_Siz = row.Mei_Siz,
				JanCode = row.JanCode,
				JuchuSu = row.Su,
				Tanka = row.Tanka,
				Jodai = row.Jodai,
				Gedai = row.Gedai,
			};
		}
		return [.. map.Values];
	}

	// ===== 通信・共通ヘルパー =====

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	Task<List<T>> QueryListAsync<T>(string where, string order, CancellationToken ct) =>
		CoreServiceClient.QueryListAsync<T>(where, order, ct);

	TResult? ShowSelect<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
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

	/// <summary>不正JSONを空配列として扱う <c>Jmeisai</c> の SQL 式（AGENTS.md の JSON 防御規約）。</summary>
	static string SafeJmeisai(string alias) =>
		$"CASE WHEN json_valid({alias}.Jmeisai) THEN {alias}.Jmeisai ELSE '[]' END";

	/// <summary>明細JSONの数値項目を取り出すSQL式。</summary>
	static string MeisaiNum(string alias, string property) =>
		$"CAST(IFNULL(json_extract({alias}.value, '$.{property}'), 0) AS INTEGER)";

	static void AddCodeRange(List<string> clauses, List<string> parameters, string column, string? from, string? to) {
		string normalizedFrom = Normalize(from);
		string normalizedTo = Normalize(to);
		if (!string.IsNullOrEmpty(normalizedFrom)) clauses.Add($"{column} >= {AddParameter(parameters, normalizedFrom)}");
		if (!string.IsNullOrEmpty(normalizedTo)) clauses.Add($"{column} <= {AddParameter(parameters, normalizedTo)}");
	}

	static void AddYmdRange(List<string> clauses, List<string> parameters, string column, DateTime? from, DateTime? to) {
		if (from is DateTime f) clauses.Add($"{column} >= {AddParameter(parameters, f.ToString("yyyyMMdd"))}");
		if (to is DateTime t) clauses.Add($"{column} <= {AddParameter(parameters, t.ToString("yyyyMMdd"))}");
	}

	static string BuildInClause(string column, IEnumerable<long> values, List<string> parameters) {
		string[] parameterNames = [.. values.Select(x => AddParameter(parameters, x))];
		return $"{column} IN ({string.Join(",", parameterNames)})";
	}

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	static string Normalize(string? value) => value?.Trim() ?? string.Empty;

	static bool TryParseNo(string? value, out long result) =>
		long.TryParse(Normalize(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

	static string ToYmd8(DateTime? value) => value?.ToString("yyyyMMdd") ?? string.Empty;

	internal static DateTime? FromYmd8(string? value) =>
		DateTime.TryParseExact(value, "yyyyMMdd", null, DateTimeStyles.None, out DateTime result) ? result : null;

	internal static string FormatYmd8(string? value) => FromYmd8(value)?.ToString("yyyy/MM/dd") ?? string.Empty;

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	internal static string FormatCodeName(CodeNameView? value) =>
		value == null ? string.Empty : CodeNameDisplay.Format(value.Sid, value.Cd, value.Mei);

	/// <summary>受注ヘッダの取引区分(<see cref="EnumJuchu"/>)を「10 受注」形式で表示する。</summary>
	internal static string FormatJuchuKubun(int kubun) => (EnumJuchu)kubun switch {
		EnumJuchu.Juchu => "10 受注",
		EnumJuchu.Henpin => "20 受注返品",
		EnumJuchu.Nebiki => "30 値引",
		EnumJuchu.Other => "99 その他",
		_ => kubun.ToString(CultureInfo.InvariantCulture),
	};
}

/// <summary>商品×色サイズ を一意にするキー。</summary>
readonly record struct JuchuSkuKey(long ShohinId, long ColId, long SizId);

/// <summary>コンボボックス用の値・表示名の組（受注の取引区分）。</summary>
public sealed record JuchuKubunOption(int? Value, string Label);

/// <summary>受注明細を SKU 単位へ正規化した中間データ。</summary>
public sealed class JuchuMeisaiSku {
	public long Id_Shohin { get; init; }
	public string Code_Shohin { get; init; } = string.Empty;
	public string Mei_Shohin { get; init; } = string.Empty;
	public long Id_Col { get; init; }
	public string Code_Col { get; init; } = string.Empty;
	public string Mei_Col { get; init; } = string.Empty;
	public long Id_Siz { get; init; }
	public string Code_Siz { get; init; } = string.Empty;
	public string Mei_Siz { get; init; } = string.Empty;
	public string JanCode { get; init; } = string.Empty;
	/// <summary>受注数（同一SKUの明細行を合算した値）</summary>
	public int JuchuSu { get; set; }
	public int Tanka { get; set; }
	public int Jodai { get; set; }
	public int Gedai { get; set; }
}

/// <summary>タブ1の一覧行（受注1件 = 配分1件）。</summary>
public sealed class JuchuHaibunListRow(Tran12Jyuchu juchu, TranHaibun? haibun, TranHaibun? zan) {
	public long Id => juchu.Id;
	public string JuchuDay => JuchuHaibunInputViewModel.FormatYmd8(juchu.DenDay);
	public string TokuiDisplay => JuchuHaibunInputViewModel.FormatCodeName(juchu.VTokui);
	public string SokoDisplay => JuchuHaibunInputViewModel.FormatCodeName(juchu.VSoko);
	public string KubunDisplay => JuchuHaibunInputViewModel.FormatJuchuKubun(juchu.Kubun);
	public string Memo => juchu.Memo;

	/// <summary>受注数（受注ヘッダの数量合計）</summary>
	public int JuchuSu => juchu.SuTotal;
	public long KingakuTotal => juchu.KingakuTotal;

	/// <summary>配分指示日（配分側の最小値）</summary>
	public string ShijiDay => JuchuHaibunInputViewModel.FormatYmd8(haibun?.DenDay);
	/// <summary>納品日（配分側の最小値。無ければ受注ヘッダの納品予定日）</summary>
	public string NouhinDay {
		get {
			string haibunDay = haibun?.NouhinDay ?? string.Empty;
			return JuchuHaibunInputViewModel.FormatYmd8(
				haibunDay.Length > 0 ? haibunDay : juchu.NouhinDay);
		}
	}

	/// <summary>配分数（未完了の受注配分）</summary>
	public int HaibunSu => haibun?.Su ?? 0;
	/// <summary>出荷済数（卸先・売仕店への出荷売上）</summary>
	public int ShukkaSu => zan?.JitsuSu ?? 0;
	/// <summary>未配分残 = Σ_SKU MAX(受注数 − 出荷済 − 配分, 0)</summary>
	public int ZanSu => zan?.Su ?? 0;

	public string JokyoDisplay => HaibunSu == 0 ? "未配分" : ZanSu == 0 ? "配分済" : "一部配分";
}

/// <summary>タブ2の配分明細行（商品 × 色サイズ）。</summary>
public sealed partial class JuchuHaibunMeisaiRow(JuchuMeisaiSku sku) : ObservableObject {
	public long Id_Shohin => sku.Id_Shohin;
	public long Id_Col => sku.Id_Col;
	public long Id_Siz => sku.Id_Siz;
	public string Code_Shohin => sku.Code_Shohin;
	public string Mei_Shohin => sku.Mei_Shohin;
	public string ColDisplay => JoinCodeName(sku.Code_Col, sku.Mei_Col);
	public string SizDisplay => JoinCodeName(sku.Code_Siz, sku.Mei_Siz);
	public string JanCode => sku.JanCode;
	public int Tanka => sku.Tanka;
	public int Jodai => sku.Jodai;
	public int Gedai => sku.Gedai;

	/// <summary>受注数</summary>
	public int JuchuSu => sku.JuchuSu;

	/// <summary>出荷済数（卸先・売仕店への出荷売上）</summary>
	public int ShukkaSu { get; init; }
	/// <summary>確定済配分数（本画面で修正できない配分）</summary>
	public int KakuteiHaibunSu { get; init; }
	/// <summary>在庫数（受注ヘッダの倉庫）</summary>
	public int ZaikoSu { get; init; }
	/// <summary>引当数（受注ヘッダの倉庫）</summary>
	public int ReserveSu { get; init; }
	/// <summary>洗い替え対象の配分数。引当に含まれているため配分可能数へ足し戻す</summary>
	public int EditingSu { get; init; }

	/// <summary>入力値が変わったときに ViewModel 側の合計を更新するためのコールバック。</summary>
	public Action<JuchuHaibunMeisaiRow, int>? Changed { get; init; }

	/// <summary>受注残 = MAX(受注数 − 出荷済 − 確定済配分, 0)</summary>
	public int JuchuZanSu => Math.Max(JuchuSu - ShukkaSu - KakuteiHaibunSu, 0);

	/// <summary>配分可能数 = 在庫 − 引当 + 洗い替え対象</summary>
	public int HaibunKanoSu => ZaikoSu - ReserveSu + EditingSu;

	/// <summary>配分数</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SuText))]
	[NotifyPropertyChangedFor(nameof(NokoriSu))]
	[NotifyPropertyChangedFor(nameof(Kingaku))]
	public partial int Su { get; set; }

	/// <summary>残 = 受注残 − 配分数（マイナスは受注残超過）</summary>
	public int NokoriSu => JuchuZanSu - Su;

	/// <summary>配分金額 = 配分数 × 単価</summary>
	public int Kingaku => Su * Tanka;

	/// <summary>
	/// 明細の配分数の表示・編集用の文字列。
	/// <para>
	/// 0 を空白で表示する（旧システムと同じく、入力の無い行を埋め尽くさないため）。
	/// 空文字を入力した場合は 0 として扱い、バインディングの検証エラーで止めない。
	/// </para>
	/// </summary>
	public string SuText {
		get => Su == 0 ? string.Empty : Su.ToString("#,##0", CultureInfo.InvariantCulture);
		set {
			string text = (value ?? string.Empty).Replace(",", string.Empty).Trim();
			Su = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
			OnPropertyChanged();
		}
	}

	partial void OnSuChanged(int oldValue, int newValue) => Changed?.Invoke(this, newValue - oldValue);

	static string JoinCodeName(string? code, string? name) {
		string cd = code?.Trim() ?? string.Empty;
		string mei = name?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}
}
