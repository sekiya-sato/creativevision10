using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 発注配分入力画面の ViewModel。
/// <para>
/// 発注データ(<see cref="Tran13Hachu"/>)を発注No(=Id)で特定し、その明細(入荷予定)を
/// 各入庫先へ振り分けて <see cref="TranHaibun"/> を作成・修正する。
/// 配分区分は <see cref="EnumHaibun.Hatsukai"/>(初回配分)、<see cref="TranHaibun.RelateNo1"/> に発注Id を入れる。
/// </para>
/// <para>
/// 旧システムは「配分No = 発注No × 商品1件」を単位としていたが、CV10 は発注伝票まるごとを
/// 1配分として扱う（ユーザー確定 2026-08-13）。このため配分Noは新設せず、発注Noで配分を識別する。
/// 設計の詳細は `.omo/HachuHaibunInput_plan.md` を参照。
/// </para>
/// </summary>
public partial class HachuHaibunInputViewModel : BaseViewModel {
	/// <summary>配分区分。本画面は初回配分(発注に対する配分)のみを作る。</summary>
	public const int KubunHatsukai = (int)EnumHaibun.Hatsukai;

	/// <summary>編集・集計対象とする配分の送信フラグ条件。送信済(2)は出荷処理へ渡ったものとして除外する。</summary>
	const string HaibunSendFlgCondition = "T.SendFlg < 2";

	/// <summary>
	/// 「商品Id × 入庫先Id」を1つの long キーへ合成するときの桁上げ。
	/// 上代解決(<see cref="LoadJodaiByTenpoAsync"/>)の結果を1本のクエリで受けるために使う。
	/// </summary>
	const long JodaiKeyScale = 100000000L;

	Tran13Hachu? targetHachu;
	/// <summary>修正対象として読み込んだ既存配分。登録時の洗い替え削除に使う（Id/Vdu 保持）。</summary>
	List<TranHaibun> loadedEditableRows = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoSearchCommand))]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoRegisterCommand))]
	[NotifyCanExecuteChangedFor(nameof(ClearAllCommand))]
	[NotifyCanExecuteChangedFor(nameof(SelectHachuCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== タブ1: 検索条件 =====

	[ObservableProperty]
	public partial string HachuNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string HachuNoTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? HachuDayFrom { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

	[ObservableProperty]
	public partial DateTime? HachuDayTo { get; set; }

	[ObservableProperty]
	public partial DateTime? ShijiDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? ShijiDayTo { get; set; }

	[ObservableProperty]
	public partial DateTime? NouhinDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? NouhinDayTo { get; set; }

	[ObservableProperty]
	public partial long CondId_Shiire { get; set; }

	[ObservableProperty]
	public partial string CondShiireDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial long CondId_Shain { get; set; }

	[ObservableProperty]
	public partial string CondShainDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CondShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CondShohinCodeTo { get; set; } = string.Empty;

	/// <summary>取引区分（発注ヘッダ <see cref="Tran13Hachu.Kubun"/>）。null は全て。</summary>
	[ObservableProperty]
	public partial int? CondKubun { get; set; }

	/// <summary>配分状況（<see cref="HaibunJokyoOptions"/> のインデックス）。0:全て 1:未配分 2:配分済</summary>
	[ObservableProperty]
	public partial int CondHaibunJokyo { get; set; }

	[ObservableProperty]
	public partial int CondMaxCount { get; set; } = AppGlobal.Limit;

	public IReadOnlyList<CodeLabelOption> KubunOptions { get; } = [
		new(null, "(全て)"),
		new((int)EnumHachu.Hachu, "10 発注"),
		new((int)EnumHachu.Henpin, "20 返品"),
		new((int)EnumHachu.Nebiki, "30 値引"),
		new((int)EnumHachu.Other, "99 その他"),
	];

	public IReadOnlyList<string> HaibunJokyoOptions { get; } = ["全て", "未配分のみ", "配分済のみ"];

	// ===== タブ1: 一覧 =====

	[ObservableProperty]
	public partial ObservableCollection<HachuHaibunListRow> SearchRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteCommand))]
	public partial HachuHaibunListRow? SelectedSearchRow { get; set; }

	[ObservableProperty]
	public partial int SearchCount { get; set; }

	// ===== タブ2: ヘッダ =====

	[ObservableProperty]
	public partial long HachuNo { get; set; }

	[ObservableProperty]
	public partial string HachuDayDisplay { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireDisplay { get; set; } = string.Empty;

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

	/// <summary>発注全体の発注数合計</summary>
	[ObservableProperty]
	public partial int HachuTotalSu { get; set; }

	/// <summary>発注全体の配分数合計（全商品×全入庫先）</summary>
	[ObservableProperty]
	public partial int GrandTotalSu { get; set; }

	// ===== タブ2: 商品セレクタ =====

	[ObservableProperty]
	public partial ObservableCollection<HachuHaibunShohinRow> ShohinRows { get; set; } = [];

	[ObservableProperty]
	public partial HachuHaibunShohinRow? SelectedShohin { get; set; }

	// ===== タブ2: 配分クロス表 =====

	/// <summary>入庫先の行（<see cref="MasterTokui"/> TenType IN (0,3,6) の全件）</summary>
	[ObservableProperty]
	public partial ObservableCollection<HachuHaibunTenpoRow> TenpoRows { get; set; } = [];

	/// <summary>選択中商品の SKU 列。View 側の動的列生成はこのコレクションの変更を契機に行う。</summary>
	[ObservableProperty]
	public partial ObservableCollection<HachuHaibunSkuSummary> SkuColumns { get; set; } = [];

	/// <summary>展開基準。true:色優先で列を並べる / false:サイズ優先。</summary>
	[ObservableProperty]
	public partial bool IsTenkaiByColor { get; set; } = true;

	/// <summary>画面下部に出す選択セルの色・サイズ表示。</summary>
	[ObservableProperty]
	public partial string SelectedCellInfo { get; set; } = string.Empty;

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;
	bool CanGoToEdit() => IsListTabSelected() && SelectedSearchRow != null;
	bool CanDelete() => IsListTabSelected() && SelectedSearchRow is { HaibunSu: > 0 };

	// ===== コマンド =====

	[RelayCommand]
	Task Init(CancellationToken ct) => DoSearch(ct);

	/// <summary>一覧取得(F5)。発注を主に取得し、配分側の集計をクライアントで合成する。</summary>
	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		StartBusy("一覧取得中...");
		try {
			List<Tran13Hachu> hachuList = await LoadHachuListAsync(ct);
			var ids = hachuList.Select(x => x.Id).ToList();
			Dictionary<int, TranHaibun> haibunMap = await LoadHaibunSummaryAsync(ids, ct);

			ObservableCollection<HachuHaibunListRow> rows = [];
			foreach (Tran13Hachu hachu in hachuList) {
				haibunMap.TryGetValue((int)hachu.Id, out TranHaibun? summary);
				rows.Add(new HachuHaibunListRow(hachu, summary));
			}
			SearchRows = rows;
			SearchCount = rows.Count;
			SelectedSearchRow = rows.FirstOrDefault();
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 発注を {SearchCount:N0} 件取得しました";
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
		HachuNoFrom = string.Empty;
		HachuNoTo = string.Empty;
		HachuDayFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		HachuDayTo = null;
		ShijiDayFrom = null;
		ShijiDayTo = null;
		NouhinDayFrom = null;
		NouhinDayTo = null;
		CondId_Shiire = 0;
		CondShiireDisplay = string.Empty;
		CondId_Shain = 0;
		CondShainDisplay = string.Empty;
		CondShohinCodeFrom = string.Empty;
		CondShohinCodeTo = string.Empty;
		CondKubun = null;
		CondHaibunJokyo = 0;
		CondMaxCount = AppGlobal.Limit;
	}

	[RelayCommand]
	void SelectCondShiire() {
		var shiire = ShowSelect<MasterShiire>(typeof(MasterShiire), string.Empty, "Code", CondId_Shiire);
		if (shiire == null) return;
		CondId_Shiire = shiire.Id;
		CondShiireDisplay = CodeNameDisplay.Format(shiire.Id, shiire.Code, shiire.Name);
	}

	[RelayCommand]
	void SelectCondShain() {
		var shain = ShowSelect<MasterShain>(typeof(MasterShain), string.Empty, "Code", CondId_Shain);
		if (shain == null) return;
		CondId_Shain = shain.Id;
		CondShainDisplay = CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	/// <summary>配分入力へ(F6)。選択発注の明細と既存配分を読み込んでタブ2を構築する。</summary>
	[RelayCommand(CanExecute = nameof(CanGoToEdit), IncludeCancelCommand = true)]
	async Task GoToEdit(CancellationToken ct) {
		if (SelectedSearchRow == null) return;

		StartBusy("配分データ取得中...");
		try {
			await LoadEntryAsync(SelectedSearchRow.Id, ct);
			SelectedTabIndex = 1;
			Message = $"発注No {HachuNo:N0} の配分入力を開始します";
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
	/// 発注No の選択。汎用伝票選択ダイアログ(<see cref="Views.Sub.SelectTranWinView"/>)から
	/// 発注を選び、その配分入力へ切り替える。一覧を経由せずに発注Noから直接入りたい場合の導線。
	/// </summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task SelectHachu(CancellationToken ct) {
		var win = new Views.Sub.SelectTranWinView();
		if (win.DataContext is not Sub.SelectTranWinViewModel vm) return;
		vm.SetParam(typeof(Tran13Hachu), where: "CalcFlag <> 0", order: "Id DESC",
			startPos: HachuNo, title: "発注選択", torisakiHeader: "仕入先", kubunLabels: HachuKubunLabels);
		if (ClientLib.ShowDialogView(win, this) != true) return;
		if (vm.GetCurrent<Tran13Hachu>() is not { } hachu || hachu.Id == HachuNo) return;

		// 切り替えると入力途中の配分数は失われるので、読み込み済みの伝票があるときは確認する。
		if (targetHachu != null &&
			MessageEx.ShowQuestionDialog(
				$"入力中の内容は破棄されます。発注No {hachu.Id:N0} の配分入力に切り替えますか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		StartBusy("配分データ取得中...");
		try {
			await LoadEntryAsync(hachu.Id, ct);
			Message = $"発注No {HachuNo:N0} の配分入力を開始します";
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

	/// <summary>
	/// 発注選択ダイアログへ渡す区分の表示名。
	/// <see cref="EnumHachu"/> の発注用表示名を明示する。
	/// </summary>
	static readonly Dictionary<int, string> HachuKubunLabels = new() {
		[(int)EnumHachu.Hachu] = "発注",
		[(int)EnumHachu.Henpin] = "返品",
		[(int)EnumHachu.Nebiki] = "値引",
		[(int)EnumHachu.Other] = "その他",
	};

	/// <summary>削除(F7)。選択発注に紐づく未送信の配分をまとめて削除する。</summary>
	[RelayCommand(CanExecute = nameof(CanDelete), IncludeCancelCommand = true)]
	async Task DoDelete(CancellationToken ct) {
		if (SelectedSearchRow is not { } row) return;
		if (MessageEx.ShowQuestionDialog(
				$"発注No {row.Id:N0} の配分（{row.HaibunSu:N0} 点）を削除します。よろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		StartBusy("配分データ削除中...");
		try {
			List<TranHaibun> targets = await LoadEditableHaibunAsync(row.Id, ct);
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

	/// <summary>全クリア(Shift+F6)。入力済みの配分数をすべて 0 に戻す。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected))]
	void ClearAll() {
		if (TenpoRows.Count == 0) return;
		if (MessageEx.ShowQuestionDialog("入力した配分数をすべて 0 に戻します。よろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		foreach (HachuHaibunTenpoRow tenpo in TenpoRows) tenpo.ClearAll();
		Message = "配分数をクリアしました";
	}

	/// <summary>登録(F2)。既存配分を洗い替えし、配分数>0 のセルを一括登録する。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (targetHachu == null) return;
		if (ShijiDay == null) {
			MessageEx.ShowWarningDialog("配分指示日を入力してください", owner: ActiveWindow);
			return;
		}

		// 発注数超過は警告のみで続行できる（ユーザー確定 2026-08-13）。
		List<HachuHaibunSkuSummary> over = [.. AllSkuSummaries().Where(x => x.NokoriSu < 0)];
		if (over.Count > 0 &&
			MessageEx.ShowQuestionDialog(
				$"発注数を超えている色サイズが {over.Count:N0} 件あります（超過 {over.Sum(x => -x.NokoriSu):N0} 点）。このまま登録しますか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		Dictionary<long, int> jodaiByTenpo = await LoadJodaiByTenpoAsync(ct);
		List<TranHaibun> newRecords = BuildNewRecords(jodaiByTenpo);
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
			// 洗い替え: 読込済みの既存配分を削除してから一括Insertする。
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
			await LoadEntryAsync(targetHachu.Id, ct);
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

	// TODO(将来実装): 配分補助機能。ユーザー了承のうえ初版では見送った（2026-08-13）。
	//   - 均等配分     : 選択商品の発注数を配分先へ均等割りし、余りを先頭から配る
	//   - 前回パターン複写: 同一商品の直近配分（店舗別構成比）を今回の発注数へ按分する
	//   - 店舗ランク別配分: MasterTokui のランクに応じた重み付け配分
	// TODO(将来実装): 旧システムの「入力基準（店舗毎／色サイズ毎）」による行列転置。
	//   動的列生成を2系統持つことになるため初版では見送った。

	// ===== タブ1: クエリ =====

	async Task<List<Tran13Hachu>> LoadHachuListAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = ["H.CalcFlag <> 0"];

		if (TryParseNo(HachuNoFrom, out long noFrom)) clauses.Add($"H.Id >= {AddParameter(parameters, noFrom)}");
		if (TryParseNo(HachuNoTo, out long noTo)) clauses.Add($"H.Id <= {AddParameter(parameters, noTo)}");
		AddYmdRange(clauses, parameters, "H.DenDay", HachuDayFrom, HachuDayTo);
		if (CondId_Shiire > 0) clauses.Add($"H.Id_Shiire = {AddParameter(parameters, CondId_Shiire)}");
		if (CondKubun is int kubun) clauses.Add($"H.Kubun = {AddParameter(parameters, kubun)}");

		// 商品CD範囲は発注明細(JSON)を展開して判定する。不正JSONは空配列として扱う。
		List<string> meisaiClauses = [];
		AddCodeRange(meisaiClauses, parameters, "json_extract(m.value, '$.Code_Shohin')", CondShohinCodeFrom, CondShohinCodeTo);
		if (meisaiClauses.Count > 0) {
			clauses.Add($"""
				EXISTS (SELECT 1 FROM json_each({SafeJmeisai("H")}) AS m WHERE {string.Join(" AND ", meisaiClauses)})
				""");
		}

		// 配分側の条件（指示日・納品日・入力者）と配分状況は EXISTS / NOT EXISTS で発注へ掛ける。
		List<string> haibunClauses = [
			$"T.Kubun = {AddParameter(parameters, KubunHatsukai)}",
			HaibunSendFlgCondition,
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
		string sql = $"""
			SELECT
				H.Id, H.Vdc, H.Vdu, H.DenDay, H.Id_Shiire, H.VShiire, H.Id_Soko, H.VSoko,
				H.Kubun, H.SuTotal, H.KingakuTotal, H.Id_Shain, H.VShain, H.Memo
			FROM Tran13Hachu H
			WHERE {string.Join(" AND ", clauses)}
			ORDER BY H.Id DESC
			{limit}
			""";
		return await QuerySqlListAsync<Tran13Hachu>(sql, parameters, ct);
	}

	/// <summary>発注Id別の配分サマリ。受け皿は <see cref="TranHaibun"/> へ別名射影する。</summary>
	async Task<Dictionary<int, TranHaibun>> LoadHaibunSummaryAsync(IReadOnlyCollection<long> hachuIds, CancellationToken ct) {
		if (hachuIds.Count == 0) return [];
		List<string> parameters = [];
		string inClause = BuildInClause("T.RelateNo1", hachuIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.RelateNo1,
				MIN(T.DenDay) AS DenDay,
				MIN(T.NouhinDay) AS NouhinDay,
				MIN(T.Id_Shain) AS Id_Shain,
				IFNULL(SUM(T.Su), 0) AS Su,
				COUNT(DISTINCT T.Id_Tenpo) AS JitsuSu
			FROM TranHaibun T
			WHERE T.Kubun = {AddParameter(parameters, KubunHatsukai)}
				AND {HaibunSendFlgCondition}
				AND {inClause}
			GROUP BY T.RelateNo1
			""";
		List<TranHaibun> rows = await QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
		return rows.GroupBy(x => x.RelateNo1).ToDictionary(g => g.Key, g => g.First());
	}

	// ===== タブ2: 構築 =====

	async Task LoadEntryAsync(long hachuId, CancellationToken ct) {
		List<Tran13Hachu> found = await QueryListAsync<Tran13Hachu>($"Id = {hachuId}", "Id", ct);
		targetHachu = found.FirstOrDefault()
			?? throw new InvalidOperationException($"発注No {hachuId} が見つかりません。");

		HachuNo = targetHachu.Id;
		HachuDayDisplay = FormatYmd8(targetHachu.DenDay);
		ShiireDisplay = FormatCodeName(targetHachu.VShiire);
		SokoDisplay = FormatCodeName(targetHachu.VSoko);
		KubunDisplay = FormatHachuKubun(targetHachu.Kubun);

		// 発注明細を SKU 単位へ正規化（同一SKUの複数行は合算）
		List<HachuMeisaiSku> skus = NormalizeMeisai(targetHachu.Jmeisai);
		HachuTotalSu = skus.Sum(x => x.HachuSu);

		// 既存配分（修正対象）
		loadedEditableRows = await LoadEditableHaibunAsync(hachuId, ct);
		var existing = loadedEditableRows
			.GroupBy(x => new CellKey(x.Id_Tenpo, x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		// 商品行と SKU サマリを構築
		Dictionary<long, MasterShohin> shohinMap = await LoadShohinMapAsync(skus.Select(x => x.Id_Shohin), ct);
		ObservableCollection<HachuHaibunShohinRow> shohinRows = [];
		foreach (IGrouping<long, HachuMeisaiSku> group in skus.GroupBy(x => x.Id_Shohin)) {
			shohinMap.TryGetValue(group.Key, out MasterShohin? shohin);
			HachuMeisaiSku head = group.First();
			var shohinRow = new HachuHaibunShohinRow(group.Key, head.Code_Shohin, head.Mei_Shohin, shohin);
			foreach (HachuMeisaiSku sku in SortSkus(group)) {
				shohinRow.Skus.Add(new HachuHaibunSkuSummary(sku));
			}
			shohinRows.Add(shohinRow);
		}
		ShohinRows = shohinRows;

		// 入庫先の行（TenType IN (0,3,6) の全件を常時表示する）
		List<MasterTokui> tenpoList = await QueryListAsync<MasterTokui>("TenType IN (0,3,6)", "Code", ct);
		ObservableCollection<HachuHaibunTenpoRow> tenpoRows = [];
		int lineNo = 1;
		foreach (MasterTokui tenpo in tenpoList) {
			var tenpoRow = new HachuHaibunTenpoRow(lineNo++, tenpo);
			foreach (HachuHaibunShohinRow shohinRow in shohinRows) {
				List<HachuHaibunCell> cells = [];
				foreach (HachuHaibunSkuSummary summary in shohinRow.Skus) {
					var cell = new HachuHaibunCell(shohinRow.Id_Shohin, summary, tenpoRow) {
						Changed = OnCellChanged,
					};
					int su = existing.GetValueOrDefault(new CellKey(tenpo.Id, shohinRow.Id_Shohin, summary.Id_Col, summary.Id_Siz));
					if (su != 0) cell.Su = su;
					cells.Add(cell);
				}
				tenpoRow.SetCells(shohinRow.Id_Shohin, cells);
			}
			tenpoRows.Add(tenpoRow);
		}
		TenpoRows = tenpoRows;

		// 発注明細に無い SKU の配分が残っていれば知らせる（発注が後から修正された場合）
		var validKeys = shohinRows
			.SelectMany(s => s.Skus.Select(k => new CellKey(0, s.Id_Shohin, k.Id_Col, k.Id_Siz)))
			.ToHashSet();
		int orphan = loadedEditableRows
			.Count(x => !validKeys.Contains(new CellKey(0, x.Id_Shohin, x.Id_Col, x.Id_Siz)));
		if (orphan > 0) {
			MessageEx.ShowWarningDialog(
				$"発注明細に存在しない配分が {orphan:N0} 件あります。発注が修正された可能性があります。\n登録すると、これらの配分は削除されます。",
				owner: ActiveWindow);
		}

		TranHaibun? first = loadedEditableRows.FirstOrDefault();
		ShijiDay = FromYmd8(first?.DenDay) ?? DateTime.Today;
		NouhinDay = FromYmd8(first?.NouhinDay) ?? FromYmd8(targetHachu.DenDay) ?? DateTime.Today;
		Memo = first?.Memo ?? string.Empty;
		if (first is { Id_Shain: > 0 }) {
			Id_Shain = first.Id_Shain;
			ShainDisplay = await LoadShainDisplayAsync(first.Id_Shain, ct);
		}
		else if (targetHachu.Id_Shain > 0) {
			Id_Shain = targetHachu.Id_Shain;
			ShainDisplay = FormatCodeName(targetHachu.VShain);
		}

		SelectedShohin = shohinRows.FirstOrDefault();
		SelectedCellInfo = string.Empty;
		RefreshGrandTotal();
	}

	/// <summary>修正できる配分（未送信かつ未確定）を取得する。Id/Vdu は洗い替え削除に使う。</summary>
	Task<List<TranHaibun>> LoadEditableHaibunAsync(long hachuId, CancellationToken ct) =>
		QueryListAsync<TranHaibun>(
			$"Kubun = {KubunHatsukai} AND RelateNo1 = {hachuId} AND SendFlg = 0 AND KakuteiDay = ''",
			"Id_Tenpo, Id_Shohin, Id_Col, Id_Siz, Id", ct);

	async Task<Dictionary<long, MasterShohin>> LoadShohinMapAsync(IEnumerable<long> shohinIds, CancellationToken ct) {
		var ids = shohinIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) return [];
		List<MasterShohin> rows = await QueryListAsync<MasterShohin>(
			$"Id IN ({string.Join(",", ids)})", "Code", ct);
		return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
	}

	async Task<string> LoadShainDisplayAsync(long idShain, CancellationToken ct) {
		List<MasterShain> rows = await QueryListAsync<MasterShain>($"Id = {idShain}", "Id", ct);
		MasterShain? shain = rows.FirstOrDefault();
		// 表示書式は発注ヘッダ由来の VShain と揃える（登録前後で見た目が変わらないようにする）
		return shain == null ? $"Id:{idShain}" : CodeNameDisplay.Format(shain.Id, shain.Code, shain.Name);
	}

	/// <summary>
	/// 入庫先ごとの適用上代（<see cref="DerivedJodai"/>）を商品×入庫先で引く。
	/// 該当行が無い組み合わせは商品マスタの上代が返る。
	/// </summary>
	async Task<Dictionary<long, int>> LoadJodaiByTenpoAsync(CancellationToken ct) {
		List<long> tenpoIds = [.. TenpoRows.Where(x => x.AllTotalSu > 0).Select(x => x.Id_Tenpo)];
		List<long> shohinIds = [.. ShohinRows.Select(x => x.Id_Shohin)];
		if (tenpoIds.Count == 0 || shohinIds.Count == 0) return [];

		List<string> parameters = [];
		string jodaiDay = JodaiDayExpr(parameters);
		string tenpoList = string.Join(",", tenpoIds.Select(x => x.ToString(CultureInfo.InvariantCulture)));
		string shohinList = string.Join(",", shohinIds.Select(x => x.ToString(CultureInfo.InvariantCulture)));
		// 受け皿は MasterShohin。Id へ「商品Id × 入庫先Id」の合成キーを載せて1本で引く。
		string sql = $"""
			SELECT
				(M.Id * {JodaiKeyScale} + T.Id) AS Id,
				{DerivedJodai.FinalJodaiSql("M.Id", TenpoTaishoExpr, "T.Id", jodaiDay, "M")} AS TankaJodai
			FROM MasterShohin M, MasterTokui T
			WHERE M.Id IN ({shohinList}) AND T.Id IN ({tenpoList})
			""";
		List<MasterShohin> rows = await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
		return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().TankaJodai);
	}

	/// <summary>上代解決の対象系統。配分先は直営店なので店舗系で固定。</summary>
	static string TenpoTaishoExpr => ((int)EnumJodaiTaisho.Tenpo).ToString(CultureInfo.InvariantCulture);

	/// <summary>上代解決の判定日SQL式。配分指示日を使い、未入力なら今日。</summary>
	string JodaiDayExpr(List<string> parameters) {
		string ymd = ToYmd8(ShijiDay);
		return ymd.Length == 8 ? AddParameter(parameters, ymd) : DerivedJodai.TodaySql;
	}

	List<TranHaibun> BuildNewRecords(IReadOnlyDictionary<long, int> jodaiByTenpo) {
		List<TranHaibun> records = [];
		if (targetHachu == null) return records;
		string denDay = ToYmd8(ShijiDay);
		string nouhinDay = ToYmd8(NouhinDay);
		foreach (HachuHaibunTenpoRow tenpo in TenpoRows) {
			if (tenpo.AllTotalSu == 0) continue;
			foreach (HachuHaibunShohinRow shohin in ShohinRows) {
				foreach (HachuHaibunCell cell in tenpo.GetCells(shohin.Id_Shohin).Where(x => x.Su > 0)) {
					int jodai = jodaiByTenpo.TryGetValue(shohin.Id_Shohin * JodaiKeyScale + tenpo.Id_Tenpo, out int resolved)
						? resolved
						: cell.Summary.Jodai;
					records.Add(new TranHaibun {
						DenDay = denDay,
						NouhinDay = nouhinDay,
						Id_Soko = targetHachu.Id_Soko,
						Id_Tenpo = tenpo.Id_Tenpo,
						Kubun = KubunHatsukai,
						SendFlg = 0,
						Id_Shohin = shohin.Id_Shohin,
						JanCode = cell.Summary.JanCode,
						Id_Col = cell.Summary.Id_Col,
						Id_Siz = cell.Summary.Id_Siz,
						Su = cell.Su,
						Tanka = jodai,
						Kingaku = cell.Su * jodai,
						Jodai = jodai,
						Gedai = cell.Summary.Gedai,
						RelateNo1 = (int)targetHachu.Id,
						Memo = Memo,
						Id_Shain = Id_Shain,
					});
				}
			}
		}
		return records;
	}

	/// <summary>既存配分を1往復でまとめて削除する。1件でも競合すればサーバ側で何も削除されない</summary>
	Task DeleteHaibunRowsAsync(IReadOnlyCollection<TranHaibun> rows, CancellationToken ct) =>
		CoreServiceClient.DeleteBulkAsync(typeof(TranHaibun), rows, "既存配分", ct);

	// ===== 画面状態の更新 =====

	partial void OnSelectedShohinChanged(HachuHaibunShohinRow? value) {
		foreach (HachuHaibunTenpoRow tenpo in TenpoRows) tenpo.SwitchShohin(value?.Id_Shohin ?? 0);
		SkuColumns = value == null ? [] : [.. value.Skus];
		SelectedCellInfo = string.Empty;
	}

	partial void OnIsTenkaiByColorChanged(bool value) {
		// 展開基準は列の並び順のみを変える。入力値はセルが保持しているので作り直しは不要。
		foreach (HachuHaibunShohinRow shohin in ShohinRows) shohin.SortSkus(value);
		foreach (HachuHaibunTenpoRow tenpo in TenpoRows) {
			foreach (HachuHaibunShohinRow shohin in ShohinRows) {
				tenpo.ReorderCells(shohin.Id_Shohin, shohin.Skus);
			}
			tenpo.SwitchShohin(SelectedShohin?.Id_Shohin ?? 0);
		}
		SkuColumns = SelectedShohin == null ? [] : [.. SelectedShohin.Skus];
	}

	void OnCellChanged(HachuHaibunCell cell, int delta) {
		GrandTotalSu += delta;
		HachuHaibunShohinRow? shohin = ShohinRows.FirstOrDefault(x => x.Id_Shohin == cell.Id_Shohin);
		shohin?.AddDelta(delta);
	}

	void RefreshGrandTotal() {
		GrandTotalSu = TenpoRows.Sum(x => x.AllTotalSu);
		foreach (HachuHaibunShohinRow shohin in ShohinRows) shohin.RefreshTotal();
	}

	IEnumerable<HachuHaibunSkuSummary> AllSkuSummaries() => ShohinRows.SelectMany(x => x.Skus);

	IEnumerable<HachuMeisaiSku> SortSkus(IEnumerable<HachuMeisaiSku> source) => IsTenkaiByColor
		? source.OrderBy(x => x.Code_Col, StringComparer.Ordinal).ThenBy(x => x.Code_Siz, StringComparer.Ordinal)
		: source.OrderBy(x => x.Code_Siz, StringComparer.Ordinal).ThenBy(x => x.Code_Col, StringComparer.Ordinal);

	/// <summary>発注明細を SKU 単位へ正規化する。同一 商品×色×サイズ の複数行は数量を合算する。</summary>
	static List<HachuMeisaiSku> NormalizeMeisai(List<Tran99Meisai>? meisai) {
		Dictionary<CellKey, HachuMeisaiSku> map = [];
		foreach (Tran99Meisai row in meisai ?? []) {
			if (row.Id_Shohin <= 0) continue;
			var key = new CellKey(0, row.Id_Shohin, row.Id_Col, row.Id_Siz);
			if (map.TryGetValue(key, out HachuMeisaiSku? found)) {
				found.HachuSu += row.Su;
				continue;
			}
			map[key] = new HachuMeisaiSku {
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
				HachuSu = row.Su,
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
		string[] parameterNames = values.Select(x => AddParameter(parameters, x)).ToArray();
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

	/// <summary>発注ヘッダの取引区分(<see cref="EnumHachu"/>)を「10 発注」形式で表示する。</summary>
	internal static string FormatHachuKubun(int kubun) => (EnumHachu)kubun switch {
		EnumHachu.Hachu => "10 発注",
		EnumHachu.Henpin => "20 返品",
		EnumHachu.Nebiki => "30 値引",
		EnumHachu.Other => "99 その他",
		_ => kubun.ToString(CultureInfo.InvariantCulture),
	};
}

/// <summary>入庫先×商品×色サイズ を一意にするキー。入庫先を問わない用途では TenpoId に 0 を入れる。</summary>
readonly record struct CellKey(long TenpoId, long ShohinId, long ColId, long SizId);

/// <summary>コンボボックス用の値・表示名の組。</summary>
public sealed record CodeLabelOption(int? Value, string Label);

/// <summary>発注明細を SKU 単位へ正規化した中間データ。</summary>
public sealed class HachuMeisaiSku {
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
	/// <summary>発注数（同一SKUの明細行を合算した値）</summary>
	public int HachuSu { get; set; }
	public int Jodai { get; init; }
	public int Gedai { get; init; }
}

/// <summary>タブ1の一覧行（発注1件 = 配分1件）。</summary>
public sealed class HachuHaibunListRow(Tran13Hachu hachu, TranHaibun? summary) {
	public long Id => hachu.Id;
	public string HachuDay => HachuHaibunInputViewModel.FormatYmd8(hachu.DenDay);
	public string ShiireDisplay => HachuHaibunInputViewModel.FormatCodeName(hachu.VShiire);
	public string SokoDisplay => HachuHaibunInputViewModel.FormatCodeName(hachu.VSoko);
	public string KubunDisplay => HachuHaibunInputViewModel.FormatHachuKubun(hachu.Kubun);
	public string Memo => hachu.Memo;

	/// <summary>発注数（発注ヘッダの数量合計）</summary>
	public int HachuSu => hachu.SuTotal;
	public long KingakuTotal => hachu.KingakuTotal;

	/// <summary>配分指示日（配分側の最小値）</summary>
	public string ShijiDay => HachuHaibunInputViewModel.FormatYmd8(summary?.DenDay);
	/// <summary>納品日（配分側の最小値）</summary>
	public string NouhinDay => HachuHaibunInputViewModel.FormatYmd8(summary?.NouhinDay);
	/// <summary>配分数</summary>
	public int HaibunSu => summary?.Su ?? 0;
	/// <summary>配分先の入庫先数</summary>
	public int TenpoCount => summary?.JitsuSu ?? 0;
	/// <summary>未配分数 = 発注数 − 配分数</summary>
	public int ZanSu => HachuSu - HaibunSu;
	public string JokyoDisplay => HaibunSu == 0 ? "未配分" : ZanSu == 0 ? "配分済" : "一部配分";
}

/// <summary>タブ2の商品セレクタ行。発注明細に含まれる商品1件分。</summary>
public sealed partial class HachuHaibunShohinRow(long idShohin, string code, string name, MasterShohin? shohin) : ObservableObject {
	public long Id_Shohin { get; } = idShohin;
	public string Code { get; } = code;
	public string Name { get; } = name;
	/// <summary>メーカー品番（商品マスタ由来。旧システムのヘッダ表示に対応）</summary>
	public string MakerHin { get; } = shohin?.MakerHin ?? string.Empty;
	public int TankaJodai { get; } = shohin?.TankaJodai ?? 0;
	public int TankaGenka { get; } = shohin?.TankaGenka ?? 0;

	public List<HachuHaibunSkuSummary> Skus { get; } = [];

	/// <summary>この商品の発注数合計</summary>
	public int HachuSu => Skus.Sum(x => x.HachuSu);

	/// <summary>この商品の配分数合計（全入庫先）</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ZanSu))]
	[NotifyPropertyChangedFor(nameof(JodaiKingaku))]
	[NotifyPropertyChangedFor(nameof(GenkaKingaku))]
	public partial int HaibunSu { get; set; }

	/// <summary>残 = 発注数 − 配分数</summary>
	public int ZanSu => HachuSu - HaibunSu;
	/// <summary>上代金額 = 上代 × 配分数</summary>
	public int JodaiKingaku => TankaJodai * HaibunSu;
	/// <summary>原価金額 = 原価 × 配分数</summary>
	public int GenkaKingaku => TankaGenka * HaibunSu;

	public void AddDelta(int delta) => HaibunSu += delta;

	public void RefreshTotal() => HaibunSu = Skus.Sum(x => x.HaibunTotalSu);

	/// <summary>展開基準に合わせて SKU の並び順を入れ替える。</summary>
	public void SortSkus(bool byColor) {
		List<HachuHaibunSkuSummary> sorted = byColor
			? [.. Skus.OrderBy(x => x.Code_Col, StringComparer.Ordinal).ThenBy(x => x.Code_Siz, StringComparer.Ordinal)]
			: [.. Skus.OrderBy(x => x.Code_Siz, StringComparer.Ordinal).ThenBy(x => x.Code_Col, StringComparer.Ordinal)];
		Skus.Clear();
		Skus.AddRange(sorted);
	}
}

/// <summary>SKU（商品+色+サイズ）単位の配分サマリ。クロス表の列見出しに使い、全入庫先の入力に連動する。</summary>
public sealed partial class HachuHaibunSkuSummary(HachuMeisaiSku sku) : ObservableObject {
	public long Id_Col => sku.Id_Col;
	public long Id_Siz => sku.Id_Siz;
	public string Code_Col => sku.Code_Col;
	public string Code_Siz => sku.Code_Siz;
	public string ColDisplay => JoinCodeName(sku.Code_Col, sku.Mei_Col);
	public string SizDisplay => JoinCodeName(sku.Code_Siz, sku.Mei_Siz);
	public string JanCode => sku.JanCode;
	public int Jodai => sku.Jodai;
	public int Gedai => sku.Gedai;

	/// <summary>発注数</summary>
	public int HachuSu => sku.HachuSu;

	/// <summary>全入庫先の配分合計（旧システムの「計」）</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(NokoriSu))]
	public partial int HaibunTotalSu { get; set; }

	/// <summary>残 = 発注数 − 配分計（旧システムの「残」）</summary>
	public int NokoriSu => HachuSu - HaibunTotalSu;

	static string JoinCodeName(string? code, string? name) {
		string cd = code?.Trim() ?? string.Empty;
		string mei = name?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}
}

/// <summary>クロス表の行（入庫先1件）。商品ごとのセル列を保持し、選択商品に応じて <see cref="Cells"/> を差し替える。</summary>
public sealed partial class HachuHaibunTenpoRow(int lineNo, MasterTokui tenpo) : ObservableObject {
	readonly Dictionary<long, List<HachuHaibunCell>> cellsByShohin = [];
	long currentShohinId;

	public int LineNo { get; } = lineNo;
	public long Id_Tenpo { get; } = tenpo.Id;
	public string TenpoCode { get; } = tenpo.Code;
	public string TenpoName { get; } = tenpo.Name;
	public string TenpoDisplay => $"{TenpoCode} {TenpoName}";

	/// <summary>選択中商品のセル列。動的生成した DataGrid 列は <c>Cells[i].Su</c> をバインドする。</summary>
	[ObservableProperty]
	public partial ObservableCollection<HachuHaibunCell> Cells { get; set; } = [];

	/// <summary>選択中商品の配分合計（クロス表の「合計」列）</summary>
	[ObservableProperty]
	public partial int TotalSu { get; set; }

	/// <summary>全商品の配分合計</summary>
	[ObservableProperty]
	public partial int AllTotalSu { get; set; }

	public void SetCells(long idShohin, List<HachuHaibunCell> cells) => cellsByShohin[idShohin] = cells;

	public IReadOnlyList<HachuHaibunCell> GetCells(long idShohin) =>
		cellsByShohin.TryGetValue(idShohin, out List<HachuHaibunCell>? cells) ? cells : [];

	public void SwitchShohin(long idShohin) {
		currentShohinId = idShohin;
		Cells = [.. GetCells(idShohin)];
		TotalSu = Cells.Sum(x => x.Su);
	}

	/// <summary>展開基準の変更に合わせてセルの並び順を SKU の並びへ揃える。</summary>
	public void ReorderCells(long idShohin, IReadOnlyList<HachuHaibunSkuSummary> order) {
		if (!cellsByShohin.TryGetValue(idShohin, out List<HachuHaibunCell>? cells)) return;
		cellsByShohin[idShohin] = [.. order
			.Select(s => cells.FirstOrDefault(c => c.Summary == s))
			.OfType<HachuHaibunCell>()];
	}

	public void AddDelta(long idShohin, int delta) {
		AllTotalSu += delta;
		if (idShohin == currentShohinId) TotalSu += delta;
	}

	public void ClearAll() {
		foreach (List<HachuHaibunCell> cells in cellsByShohin.Values) {
			foreach (HachuHaibunCell cell in cells) cell.Su = 0;
		}
	}
}

/// <summary>クロス表のセル1つ（入庫先 × SKU）。</summary>
public sealed partial class HachuHaibunCell(long idShohin, HachuHaibunSkuSummary summary, HachuHaibunTenpoRow owner) : ObservableObject {
	public long Id_Shohin { get; } = idShohin;
	public HachuHaibunSkuSummary Summary { get; } = summary;

	/// <summary>入力値が変わったときに ViewModel 側の合計を更新するためのコールバック。</summary>
	public Action<HachuHaibunCell, int>? Changed { get; init; }

	/// <summary>配分数</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SuText))]
	public partial int Su { get; set; }

	/// <summary>
	/// クロス表のセル表示・編集用の文字列。
	/// <para>
	/// 0 を空白で表示する（旧システムと同じく、入力の無いセルを埋め尽くさないため）。
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

	partial void OnSuChanged(int oldValue, int newValue) {
		int delta = newValue - oldValue;
		Summary.HaibunTotalSu += delta;
		owner.AddDelta(Id_Shohin, delta);
		Changed?.Invoke(this, delta);
	}
}
