using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 店舗配分入力画面の ViewModel。
/// 入庫予定数量を各店舗へ振り分ける。全体把握は商品Id単位（タブ1）、
/// 実際の振り分けは商品Id+色+サイズ（SKU）×店舗単位（タブ2）で行い、TranHaibun を作成・修正する。
/// </summary>
public partial class ShopHaibunInputViewModel : BaseViewModel {
	// 配分区分は CvBase の EnumHaibun へ集約した（他の配分画面と値が食い違わないようにするため）。
	// 値そのものは従来と同じ 0 / 1 なので既存データはそのまま読める。
	public const int KubunHatsukai = (int)EnumHaibun.Hatsukai;
	public const int KubunZaiko = (int)EnumHaibun.Zaiko;

	/// <summary>
	/// 発注データ（Tran13Hachu、別名 H）の「済フラグがたっていない＝未済」を表す SQL 条件。
	/// TODO: 発注データの「済フラグ」は未実装のため仮実装。済フラグ実装後はフラグ判定へ置き換えること。
	/// 現状は仕入 Tran03Shiire.RelateNo1 に発注Id が参照されていない（未消込）ことを未済とみなす。
	/// </summary>
	const string HachuMizumiCondition = "NOT EXISTS (SELECT 1 FROM Tran03Shiire S WHERE S.RelateNo1 = H.Id)";

	ShopHaibunSearchParameter? searchParam;
	MasterShohin? targetShohin;
	List<DerivedShohinColSiz> targetSkuList = [];
	List<TranHaibun> loadedEditableRows = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoSearchCommand))]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoRegisterCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== タブ1: 商品一覧 =====

	[ObservableProperty]
	public partial ObservableCollection<ShopHaibunShohinRow> SearchRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	public partial ShopHaibunShohinRow? SelectedSearchRow { get; set; }

	[ObservableProperty]
	public partial int SearchCount { get; set; }

	[ObservableProperty]
	public partial string SearchConditionText { get; set; } = string.Empty;

	// ===== タブ2: 配分入力 =====

	[ObservableProperty]
	public partial string TargetShohinCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TargetShohinName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int TargetJodai { get; set; }

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
	public partial ObservableCollection<ShopHaibunTenpoEntry> TenpoEntries { get; set; } = [];

	[ObservableProperty]
	public partial ShopHaibunTenpoEntry? SelectedTenpo { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<ShopHaibunSkuSummary> SkuSummaries { get; set; } = [];

	/// <summary>全店舗×全SKU の指示数合計</summary>
	[ObservableProperty]
	public partial int GrandTotalSu { get; set; }

	/// <summary>商品全体の配分可能数（現在庫 − 指示数 + 入荷予定数）</summary>
	[ObservableProperty]
	public partial int HaibunKanoSu { get; set; }

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;
	bool CanGoToEdit() => IsListTabSelected() && SelectedSearchRow != null;

	// ===== コマンド =====

	[RelayCommand]
	Task Init() => DoSearch(CancellationToken.None);

	/// <summary>一覧取得。条件選択ダイアログを別ウィンドウで表示してから検索する。</summary>
	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		var win = new Views.Sub.ShopHaibunSearchParamView();
		if (win.DataContext is not ShopHaibunSearchParamViewModel vm) return;
		vm.Initialize(searchParam ?? new ShopHaibunSearchParameter { MaxCount = AppGlobal.Limit });
		if (ClientLib.ShowDialogView(win, this, true) != true) {
			searchParam = vm.Parameter;
			Message = "一覧取得を中断しました";
			return;
		}
		searchParam = vm.Parameter;

		StartBusy("一覧取得中...");
		try {
			List<MasterShohin> shohinList = await LoadShohinListAsync(searchParam, ct);
			List<long> ids = shohinList.Select(x => x.Id).ToList();
			Dictionary<long, int> uriageMap = await LoadUriageTotalsAsync(ids, ct);
			Dictionary<long, int> zaikoMap = await LoadZaikoTotalsAsync(searchParam.Id_Soko, ids, ct);
			Dictionary<long, int> shijiMap = await LoadShijiTotalsAsync(searchParam.Id_Soko, ids, ct);
			Dictionary<long, int> nyukaMap = await LoadNyukaYoteiTotalsAsync(searchParam.Id_Soko, ids, ct);

			ObservableCollection<ShopHaibunShohinRow> rows = [];
			foreach (MasterShohin shohin in shohinList) {
				rows.Add(new ShopHaibunShohinRow(shohin) {
					UriageSu = uriageMap.GetValueOrDefault(shohin.Id),
					ZaikoSu = zaikoMap.GetValueOrDefault(shohin.Id),
					ShijiSu = shijiMap.GetValueOrDefault(shohin.Id),
					NyukaYoteiSu = nyukaMap.GetValueOrDefault(shohin.Id),
				});
			}
			SearchRows = rows;
			SearchCount = rows.Count;
			SelectedSearchRow = rows.FirstOrDefault();
			SearchConditionText = BuildConditionText(searchParam);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 対象商品を {SearchCount:N0} 件取得しました";
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

	/// <summary>配分画面へ。選択商品の SKU（商品+色+サイズ）を確定し、店舗×SKU の入力状態を構築する。</summary>
	[RelayCommand(CanExecute = nameof(CanGoToEdit), IncludeCancelCommand = true)]
	async Task GoToEdit(CancellationToken ct) {
		if (SelectedSearchRow == null || searchParam == null) return;

		StartBusy("配分データ取得中...");
		try {
			await LoadEntryAsync(SelectedSearchRow, searchParam, ct);
			SelectedTabIndex = 1;
			Message = $"{TargetShohinCode} {TargetShohinName} の配分入力を開始します";
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

	/// <summary>配分先店舗の選択（複数）。選択結果と店舗リストを同期する。</summary>
	[RelayCommand]
	void SelectTenpo() {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return;
		vm.SetParam(typeof(MasterTokui), "TenType IN (3,6)", "Code",
			selectedIds: TenpoEntries.Select(x => x.Id_Tenpo));
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		IReadOnlyList<MasterTokui>? selected = vm.GetSelectedItems<MasterTokui>();
		if (selected == null) return;
		SyncTenpoEntries(selected);
	}

	[RelayCommand]
	void RemoveTenpo(ShopHaibunTenpoEntry? entry) {
		entry ??= SelectedTenpo;
		if (entry == null) return;
		if (entry.TotalSu > 0 &&
			MessageEx.ShowQuestionDialog($"店舗 {entry.TenpoDisplay} には指示数が入力されています。削除しますか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		foreach (ShopHaibunEntryRow row in entry.Rows) row.Su = 0;
		entry.PropertyChanged -= OnTenpoEntryPropertyChanged;
		TenpoEntries.Remove(entry);
		SelectedTenpo = TenpoEntries.FirstOrDefault();
		RefreshGrandTotal();
	}

	[RelayCommand]
	void SelectShain() {
		var shain = ShowSelect<MasterShain>(typeof(MasterShain), string.Empty, "Code", Id_Shain);
		if (shain == null) return;
		Id_Shain = shain.Id;
		ShainDisplay = $"{shain.Code} {shain.Name}";
	}

	/// <summary>登録（F2）。既存の未送信 TranHaibun を洗い替えし、指示数>0 の店舗×SKU を一括登録する。</summary>
	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoRegister(CancellationToken ct) {
		if (targetShohin == null || searchParam == null) return;
		if (ShijiDay == null) {
			MessageEx.ShowWarningDialog("配分指示日を入力してください", owner: ActiveWindow);
			return;
		}
		Dictionary<long, int> jodaiByTenpo =
			await LoadJodaiByTenpoAsync(targetShohin.Id, TenpoEntries.Select(x => x.Id_Tenpo), ct);
		List<TranHaibun> newRecords = BuildNewRecords(jodaiByTenpo);
		if (newRecords.Count == 0 && loadedEditableRows.Count == 0) {
			MessageEx.ShowWarningDialog("店舗を追加し、指示数を入力してください", owner: ActiveWindow);
			return;
		}
		string confirm = newRecords.Count == 0
			? $"指示数が全て0のため、既存の配分指示 {loadedEditableRows.Count:N0} 件を削除します。よろしいですか？"
			: $"配分指示 {newRecords.Count:N0} 件（合計 {newRecords.Sum(x => x.Su):N0} 点）を登録します。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		StartBusy("配分データ登録中...");
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();

			// 洗い替え: 読込済みの未送信指示を1往復でまとめて削除（行単位の楽観ロック付き）
			await CoreServiceClient.DeleteBulkAsync(typeof(TranHaibun), loadedEditableRows, "既存指示", ct);

			if (newRecords.Count > 0) {
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

			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 配分指示を {newRecords.Count:N0} 件登録しました";
			if (SelectedSearchRow != null) {
				await LoadEntryAsync(SelectedSearchRow, searchParam, ct);
			}
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

	async Task<List<MasterShohin>> LoadShohinListAsync(ShopHaibunSearchParameter param, CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [];
		AddCodeRange(clauses, parameters, "M.Code", param.ShohinCodeFrom, param.ShohinCodeTo);
		AddLike(clauses, parameters, "M.Name", param.ShohinName);
		AddCodeRange(clauses, parameters, JsonCd("M.VBrand"), param.BrandFrom, param.BrandTo);
		AddCodeRange(clauses, parameters, JsonCd("M.VItem"), param.ItemFrom, param.ItemTo);
		AddCodeRange(clauses, parameters, JsonCd("M.VSeason"), param.SeasonFrom, param.SeasonTo);
		string where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		string limit = param.MaxCount is int max and > 0 ? $"LIMIT {max}" : string.Empty;
		// 配分先は直営店なので店舗系で解決する。一覧の時点では配分先が決まっていないため
		// 店舗系の全件行(Id_Tenpo=0)を代表値として表示し、店舗別の価格は登録時に引き直す
		// （LoadJodaiByTenpoAsync）。適用行が無ければ商品マスタの上代が返る。
		string jodaiDay = JodaiDayExpr(parameters);
		string sql = $"""
			SELECT
				M.Id, M.Vdc, M.Vdu, M.Code, M.Name,
				{DerivedJodai.FinalJodaiSql("M.Id", TenpoTaishoExpr, "0", jodaiDay, "M")} AS TankaJodai,
				M.TankaGenka,
				M.VBrand, M.VItem, M.VSeason
			FROM MasterShohin M
			{where}
			ORDER BY M.Code
			{limit}
			""";
		return await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
	}

	/// <summary>上代解決の対象系統。店舗配分の配分先は直営店なので店舗系で固定。</summary>
	static string TenpoTaishoExpr => ((int)EnumJodaiTaisho.Tenpo).ToString(CultureInfo.InvariantCulture);

	/// <summary>上代解決の判定日SQL式。配分指示日を使い、未入力なら今日。</summary>
	string JodaiDayExpr(List<string> parameters) {
		string ymd = ToYmd8(ShijiDay);
		return ymd.Length == 8 ? AddParameter(parameters, ymd) : DerivedJodai.TodaySql;
	}

	/// <summary>
	/// 配分先店舗ごとの適用上代（<see cref="DerivedJodai"/>）を引く。
	/// <para>
	/// 明細ごとに配分先店舗が違うため商品1件に価格を決め打ちできない。登録の直前に
	/// 「店舗Id → 適用上代」の対応表を1本のクエリで作り、<see cref="BuildNewRecords"/> へ渡す。
	/// 該当行が無い店舗は商品マスタの上代が返るので、既存の動作は変わらない。
	/// </para>
	/// </summary>
	async Task<Dictionary<long, int>> LoadJodaiByTenpoAsync(long idShohin, IEnumerable<long> tenpoIds, CancellationToken ct) {
		List<long> ids = tenpoIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) return [];

		List<string> parameters = [];
		string shohin = AddParameter(parameters, idShohin);
		string jodaiDay = JodaiDayExpr(parameters);
		string idList = string.Join(",", ids.Select(x => x.ToString(CultureInfo.InvariantCulture)));
		string sql = $"""
			SELECT
				T.Id AS Id,
				{DerivedJodai.FinalJodaiSql(shohin, TenpoTaishoExpr, "T.Id", jodaiDay, "M")} AS TankaJodai
			FROM MasterTokui T, MasterShohin M
			WHERE M.Id = {shohin} AND T.Id IN ({idList})
			""";
		List<MasterShohin> rows = await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
		return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().TankaJodai);
	}

	/// <summary>累計売上（Tran00/Tran01 の JSON 明細を CalcFlag 考慮で商品Id別に集計）</summary>
	async Task<Dictionary<long, int>> LoadUriageTotalsAsync(IReadOnlyCollection<long> shohinIds, CancellationToken ct) {
		if (shohinIds.Count == 0) return [];
		List<string> parameters = [];
		string inClause = BuildInClause("T.Id_Shohin", shohinIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.Id_Shohin, 0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(T.Su), 0) AS Su
			FROM (
				SELECT CAST(json_extract(m.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin,
					CAST(json_extract(m.value, '$.Su') AS INTEGER) * H.CalcFlag AS Su
				FROM Tran00Uriage H, json_each(H.Jmeisai) AS m
				WHERE H.CalcFlag <> 0
				UNION ALL
				SELECT CAST(json_extract(m.value, '$.Id_Shohin') AS INTEGER),
					CAST(json_extract(m.value, '$.Su') AS INTEGER) * H.CalcFlag
				FROM Tran01Tenuri H, json_each(H.Jmeisai) AS m
				WHERE H.CalcFlag <> 0
			) T
			WHERE {inClause}
			GROUP BY T.Id_Shohin
			""";
		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	/// <summary>現在庫（配分元倉庫の SummaryRealStock を商品Id別に集計）</summary>
	async Task<Dictionary<long, int>> LoadZaikoTotalsAsync(long idSoko, IReadOnlyCollection<long> shohinIds, CancellationToken ct) {
		if (shohinIds.Count == 0) return [];
		List<string> parameters = [];
		string inClause = BuildInClause("R.Id_Shohin", shohinIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Shohin, 0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(R.Su), 0) AS Su
			FROM SummaryRealStock R
			WHERE R.Id_Soko = {AddParameter(parameters, idSoko)}
				AND {inClause}
			GROUP BY R.Id_Shohin
			""";
		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	/// <summary>現在指示数（配分元倉庫の未送信・送信中 TranHaibun を商品Id別に集計）</summary>
	async Task<Dictionary<long, int>> LoadShijiTotalsAsync(long idSoko, IReadOnlyCollection<long> shohinIds, CancellationToken ct) {
		if (shohinIds.Count == 0) return [];
		List<string> parameters = [];
		string inClause = BuildInClause("T.Id_Shohin", shohinIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.Id_Shohin, 0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(T.Su), 0) AS Su
			FROM TranHaibun T
			WHERE T.Id_Soko = {AddParameter(parameters, idSoko)}
				AND T.SendFlg < 2
				AND {inClause}
			GROUP BY T.Id_Shohin
			""";
		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	/// <summary>
	/// 入荷予定数（済フラグがたっていない発注 Tran13Hachu を商品Id別に集計）。
	/// TODO: 発注データの「済フラグ」は未実装のため仮実装。
	/// 現状は仕入 Tran03Shiire.RelateNo1 に発注Id が参照されていないことを「未済」とみなしている。
	/// 済フラグ実装後は <see cref="HachuMizumiCondition"/> をフラグ判定に置き換えること。
	/// </summary>
	async Task<Dictionary<long, int>> LoadNyukaYoteiTotalsAsync(long idSoko, IReadOnlyCollection<long> shohinIds, CancellationToken ct) {
		if (shohinIds.Count == 0) return [];
		List<string> parameters = [];
		string inClause = BuildInClause("T.Id_Shohin", shohinIds, parameters);
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.Id_Shohin, 0 AS Id_Soko, 0 AS Id_Col, 0 AS Id_Siz,
				IFNULL(SUM(T.Su), 0) AS Su
			FROM (
				SELECT CAST(json_extract(m.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin,
					CAST(json_extract(m.value, '$.Su') AS INTEGER) * H.CalcFlag AS Su
				FROM Tran13Hachu H, json_each(H.Jmeisai) AS m
				WHERE H.CalcFlag <> 0
					AND H.Id_Soko = {AddParameter(parameters, idSoko)}
					AND {HachuMizumiCondition}
			) T
			WHERE {inClause}
			GROUP BY T.Id_Shohin
			""";
		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);
		return rows.ToDictionary(x => x.Id_Shohin, x => x.Su);
	}

	// ===== タブ2: 構築 =====

	async Task LoadEntryAsync(ShopHaibunShohinRow row, ShopHaibunSearchParameter param, CancellationToken ct) {
		targetShohin = row.Shohin;
		TargetShohinCode = row.Code;
		TargetShohinName = row.Name;
		TargetJodai = row.TankaJodai;
		// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
		SokoDisplay = CodeNameDisplay.Format(param.Id_Soko, param.SokoCode, param.SokoName);
		KubunDisplay = param.Kubun == KubunZaiko ? "在庫配分" : "初回配分";
		HaibunKanoSu = row.HaibunKanoSu;

		// SKU 一覧（DerivedShohinColSiz レコード単位 = MasterShohin.Jcolsiz 配列単位）
		targetSkuList = await LoadSkuListAsync(row.Id, ct);

		// SKU 別の参考値（対象倉庫）
		Dictionary<SkuKey, int> zaikoMap = await LoadSkuTotalsAsync(SkuZaikoSql(row.Id, param.Id_Soko), ct);
		Dictionary<SkuKey, int> shijiMap = await LoadSkuTotalsAsync(SkuShijiSql(row.Id, param.Id_Soko), ct);
		Dictionary<SkuKey, int> nyukaMap = await LoadSkuTotalsAsync(SkuNyukaSql(row.Id, param.Id_Soko), ct);

		// 既存の未送信指示（修正対象）
		loadedEditableRows = await QueryListAsync<TranHaibun>(
			$"Id_Soko = {param.Id_Soko} AND Id_Shohin = {row.Id} AND Kubun = {param.Kubun} AND SendFlg = 0",
			"Id_Tenpo, Id_Col, Id_Siz, Id", ct);

		// 修正対象分を差し引いた「他指示数」を SKU サマリへ設定
		Dictionary<SkuKey, int> editingMap = loadedEditableRows
			.GroupBy(x => new SkuKey(x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		ObservableCollection<ShopHaibunSkuSummary> summaries = [];
		Dictionary<SkuKey, ShopHaibunSkuSummary> summaryMap = [];
		foreach (DerivedShohinColSiz sku in targetSkuList) {
			SkuKey key = new(sku.Id_Col, sku.Id_Siz);
			var summary = new ShopHaibunSkuSummary(sku) {
				ZaikoSu = zaikoMap.GetValueOrDefault(key),
				NyukaYoteiSu = nyukaMap.GetValueOrDefault(key),
				OtherShijiSu = shijiMap.GetValueOrDefault(key) - editingMap.GetValueOrDefault(key),
			};
			summaries.Add(summary);
			summaryMap[key] = summary;
		}
		SkuSummaries = summaries;

		// 既存指示の店舗を復元
		foreach (ShopHaibunTenpoEntry old in TenpoEntries) old.PropertyChanged -= OnTenpoEntryPropertyChanged;
		TenpoEntries = [];
		List<long> tenpoIds = loadedEditableRows.Select(x => x.Id_Tenpo).Distinct().ToList();
		if (tenpoIds.Count > 0) {
			List<MasterTokui> tenpoList = await QueryListAsync<MasterTokui>(
				$"Id IN ({string.Join(",", tenpoIds)})", "Code", ct);
			SyncTenpoEntries(tenpoList);
			foreach (TranHaibun old in loadedEditableRows) {
				ShopHaibunTenpoEntry? entry = TenpoEntries.FirstOrDefault(x => x.Id_Tenpo == old.Id_Tenpo);
				ShopHaibunEntryRow? entryRow = entry?.Rows.FirstOrDefault(x => x.Id_Col == old.Id_Col && x.Id_Siz == old.Id_Siz);
				if (entryRow != null) entryRow.Su += old.Su;
			}
		}
		SelectedTenpo = TenpoEntries.FirstOrDefault();

		TranHaibun? first = loadedEditableRows.FirstOrDefault();
		ShijiDay = FromYmd8(first?.DenDay) ?? DateTime.Today;
		NouhinDay = FromYmd8(first?.NouhinDay) ?? DateTime.Today;
		if (first is { Id_Shain: > 0 }) {
			Id_Shain = first.Id_Shain;
			if (string.IsNullOrEmpty(ShainDisplay)) ShainDisplay = $"Id:{first.Id_Shain}";
		}
		RefreshGrandTotal();
	}

	async Task<List<DerivedShohinColSiz>> LoadSkuListAsync(long shohinId, CancellationToken ct) {
		List<string> parameters = [];
		string sql = $"""
			SELECT
				D.Id, D.Id_Shohin, D.RowIdx, D.Code,
				D.Id_Col, D.Code_Col, D.Mei_Col,
				D.Id_Siz, D.Code_Siz, D.Mei_Siz,
				D.Jan1, D.Jan2, D.Jan3
			FROM DerivedShohinColSiz D
			WHERE D.Id_Shohin = {AddParameter(parameters, shohinId)}
			ORDER BY D.RowIdx, D.Code_Col, D.Code_Siz
			""";
		return await QuerySqlListAsync<DerivedShohinColSiz>(sql, parameters, ct);
	}

	(string sql, List<string> parameters) SkuZaikoSql(long shohinId, long idSoko) {
		List<string> parameters = [];
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				R.Id_Shohin, 0 AS Id_Soko, R.Id_Col, R.Id_Siz,
				IFNULL(SUM(R.Su), 0) AS Su
			FROM SummaryRealStock R
			WHERE R.Id_Soko = {AddParameter(parameters, idSoko)}
				AND R.Id_Shohin = {AddParameter(parameters, shohinId)}
			GROUP BY R.Id_Col, R.Id_Siz
			""";
		return (sql, parameters);
	}

	(string sql, List<string> parameters) SkuShijiSql(long shohinId, long idSoko) {
		List<string> parameters = [];
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.Id_Shohin, 0 AS Id_Soko, T.Id_Col, T.Id_Siz,
				IFNULL(SUM(T.Su), 0) AS Su
			FROM TranHaibun T
			WHERE T.Id_Soko = {AddParameter(parameters, idSoko)}
				AND T.Id_Shohin = {AddParameter(parameters, shohinId)}
				AND T.SendFlg < 2
			GROUP BY T.Id_Col, T.Id_Siz
			""";
		return (sql, parameters);
	}

	/// <summary>
	/// SKU 別入荷予定数の SQL（TODO: 済フラグ未実装のため <see cref="HachuMizumiCondition"/> による仮実装）。
	/// </summary>
	(string sql, List<string> parameters) SkuNyukaSql(long shohinId, long idSoko) {
		List<string> parameters = [];
		string sql = $"""
			SELECT
				0 AS Id, 0 AS Vdc, 0 AS Vdu,
				T.Id_Shohin, 0 AS Id_Soko, T.Id_Col, T.Id_Siz,
				IFNULL(SUM(T.Su), 0) AS Su
			FROM (
				SELECT CAST(json_extract(m.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin,
					CAST(json_extract(m.value, '$.Id_Col') AS INTEGER) AS Id_Col,
					CAST(json_extract(m.value, '$.Id_Siz') AS INTEGER) AS Id_Siz,
					CAST(json_extract(m.value, '$.Su') AS INTEGER) * H.CalcFlag AS Su
				FROM Tran13Hachu H, json_each(H.Jmeisai) AS m
				WHERE H.CalcFlag <> 0
					AND H.Id_Soko = {AddParameter(parameters, idSoko)}
					AND {HachuMizumiCondition}
			) T
			WHERE T.Id_Shohin = {AddParameter(parameters, shohinId)}
			GROUP BY T.Id_Col, T.Id_Siz
			""";
		return (sql, parameters);
	}

	async Task<Dictionary<SkuKey, int>> LoadSkuTotalsAsync((string sql, List<string> parameters) query, CancellationToken ct) {
		List<SummaryRealStock> rows = await QuerySqlListAsync<SummaryRealStock>(query.sql, query.parameters, ct);
		return rows.ToDictionary(x => new SkuKey(x.Id_Col, x.Id_Siz), x => x.Su);
	}

	/// <summary>店舗選択結果と TenpoEntries を同期する（既存店舗の入力値は維持）。</summary>
	void SyncTenpoEntries(IReadOnlyList<MasterTokui> selected) {
		Dictionary<SkuKey, ShopHaibunSkuSummary> summaryMap =
			SkuSummaries.ToDictionary(x => new SkuKey(x.Id_Col, x.Id_Siz));

		// 選択から外れた店舗を除去（指示数は 0 に戻してサマリへ反映）
		foreach (ShopHaibunTenpoEntry entry in TenpoEntries.Where(x => selected.All(s => s.Id != x.Id_Tenpo)).ToList()) {
			foreach (ShopHaibunEntryRow entryRow in entry.Rows) entryRow.Su = 0;
			entry.PropertyChanged -= OnTenpoEntryPropertyChanged;
			TenpoEntries.Remove(entry);
		}

		// 追加された店舗の行を構築
		foreach (MasterTokui tenpo in selected) {
			if (TenpoEntries.Any(x => x.Id_Tenpo == tenpo.Id)) continue;
			var entry = new ShopHaibunTenpoEntry(tenpo);
			foreach (DerivedShohinColSiz sku in targetSkuList) {
				if (!summaryMap.TryGetValue(new SkuKey(sku.Id_Col, sku.Id_Siz), out ShopHaibunSkuSummary? summary)) continue;
				entry.Rows.Add(new ShopHaibunEntryRow(sku, summary, entry));
			}
			entry.PropertyChanged += OnTenpoEntryPropertyChanged;
			TenpoEntries.Add(entry);
		}
		SelectedTenpo ??= TenpoEntries.FirstOrDefault();
		RefreshGrandTotal();
	}

	void OnTenpoEntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(ShopHaibunTenpoEntry.TotalSu)) RefreshGrandTotal();
	}

	void RefreshGrandTotal() => GrandTotalSu = TenpoEntries.Sum(x => x.TotalSu);

	/// <param name="jodaiByTenpo">
	/// 配分先店舗ごとの適用上代（<see cref="LoadJodaiByTenpoAsync"/>）。
	/// 該当が無い店舗は一覧と同じ代表値（<c>targetShohin.TankaJodai</c>）を使う。
	/// </param>
	List<TranHaibun> BuildNewRecords(IReadOnlyDictionary<long, int> jodaiByTenpo) {
		List<TranHaibun> records = [];
		if (targetShohin == null || searchParam == null) return records;
		foreach (ShopHaibunTenpoEntry entry in TenpoEntries) {
			int jodai = jodaiByTenpo.TryGetValue(entry.Id_Tenpo, out int resolved)
				? resolved
				: targetShohin.TankaJodai;
			foreach (ShopHaibunEntryRow entryRow in entry.Rows.Where(x => x.Su > 0)) {
				records.Add(new TranHaibun {
					DenDay = ToYmd8(ShijiDay),
					NouhinDay = ToYmd8(NouhinDay),
					Id_Soko = searchParam.Id_Soko,
					Id_Tenpo = entry.Id_Tenpo,
					Kubun = searchParam.Kubun,
					SendFlg = 0,
					Id_Shohin = targetShohin.Id,
					JanCode = entryRow.Jan1,
					Id_Col = entryRow.Id_Col,
					Id_Siz = entryRow.Id_Siz,
					Su = entryRow.Su,
					Tanka = jodai,
					Kingaku = entryRow.Su * jodai,
					Jodai = jodai,
					Gedai = targetShohin.TankaGenka,
					Id_Shain = Id_Shain,
				});
			}
		}
		return records;
	}

	// ===== 通信・共通ヘルパー =====

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	Task<List<T>> QueryListAsync<T>(string where, string order, CancellationToken ct) =>
		CoreServiceClient.QueryListAsync<T>(where, order, ct);

	TResult? ShowSelect<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	static string BuildConditionText(ShopHaibunSearchParameter param) {
		List<string> parts = [$"配分元倉庫: {CodeNameDisplay.Format(param.Id_Soko, param.SokoCode, param.SokoName)}", $"区分: {(param.Kubun == KubunZaiko ? "在庫配分" : "初回配分")}"];
		if (!string.IsNullOrWhiteSpace(param.ShohinCodeFrom) || !string.IsNullOrWhiteSpace(param.ShohinCodeTo))
			parts.Add($"商品CD: {param.ShohinCodeFrom}～{param.ShohinCodeTo}");
		if (!string.IsNullOrWhiteSpace(param.ShohinName)) parts.Add($"商品名: {param.ShohinName}");
		if (!string.IsNullOrWhiteSpace(param.BrandFrom) || !string.IsNullOrWhiteSpace(param.BrandTo))
			parts.Add($"ブランド: {param.BrandFrom}～{param.BrandTo}");
		if (!string.IsNullOrWhiteSpace(param.ItemFrom) || !string.IsNullOrWhiteSpace(param.ItemTo))
			parts.Add($"アイテム: {param.ItemFrom}～{param.ItemTo}");
		if (!string.IsNullOrWhiteSpace(param.SeasonFrom) || !string.IsNullOrWhiteSpace(param.SeasonTo))
			parts.Add($"シーズン: {param.SeasonFrom}～{param.SeasonTo}");
		return string.Join("　", parts);
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

	static void AddCodeRange(List<string> clauses, List<string> parameters, string column, string? from, string? to) {
		string normalizedFrom = Normalize(from);
		string normalizedTo = Normalize(to);
		if (!string.IsNullOrEmpty(normalizedFrom)) clauses.Add($"{column} >= {AddParameter(parameters, normalizedFrom)}");
		if (!string.IsNullOrEmpty(normalizedTo)) clauses.Add($"{column} <= {AddParameter(parameters, normalizedTo)}");
	}

	static void AddLike(List<string> clauses, List<string> parameters, string column, string? value) {
		string normalized = Normalize(value);
		if (string.IsNullOrEmpty(normalized)) return;
		clauses.Add($"{column} LIKE {AddParameter(parameters, $"%{normalized}%")}");
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

	static string JsonCd(string column) =>
		$"IFNULL(json_extract(CASE WHEN json_valid({column}) THEN {column} ELSE '{{}}' END, '$.Cd'), '')";

	static string ToYmd8(DateTime? value) => value?.ToString("yyyyMMdd") ?? string.Empty;

	static DateTime? FromYmd8(string? value) =>
		DateTime.TryParseExact(value, "yyyyMMdd", null, DateTimeStyles.None, out DateTime result) ? result : null;
}

readonly record struct SkuKey(long IdCol, long IdSiz);

/// <summary>タブ1の商品一覧行（商品Id 単位の全体把握用）。</summary>
public sealed class ShopHaibunShohinRow(MasterShohin shohin) {
	public MasterShohin Shohin { get; } = shohin;
	public long Id => Shohin.Id;
	public string Code => Shohin.Code;
	public string Name => Shohin.Name;
	public int TankaJodai => Shohin.TankaJodai;
	public string BrandDisplay => FormatCodeName(Shohin.VBrand);
	public string ItemDisplay => FormatCodeName(Shohin.VItem);
	public string SeasonDisplay => FormatCodeName(Shohin.VSeason);

	/// <summary>累計売上（Tran00/Tran01 CalcFlag 考慮）</summary>
	public int UriageSu { get; init; }
	/// <summary>現在庫（配分元倉庫）</summary>
	public int ZaikoSu { get; init; }
	/// <summary>現在指示数（TranHaibun SendFlg&lt;2）</summary>
	public int ShijiSu { get; init; }
	/// <summary>入荷予定数（未消込発注）</summary>
	public int NyukaYoteiSu { get; init; }
	/// <summary>配分可能数 = 現在庫 − 指示数 + 入荷予定数</summary>
	public int HaibunKanoSu => ZaikoSu - ShijiSu + NyukaYoteiSu;

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	static string FormatCodeName(CodeNameView? value) =>
		value == null ? string.Empty : CodeNameDisplay.Format(value.Sid, value.Cd, value.Mei);
}

/// <summary>SKU（商品+色+サイズ）単位の配分状況サマリ。全店舗の入力に即時連動する。</summary>
public sealed partial class ShopHaibunSkuSummary(DerivedShohinColSiz sku) : ObservableObject {
	public long Id_Col => sku.Id_Col;
	public long Id_Siz => sku.Id_Siz;
	public string ColDisplay => JoinCodeName(sku.Code_Col, sku.Mei_Col);
	public string SizDisplay => JoinCodeName(sku.Code_Siz, sku.Mei_Siz);
	public string Jan1 => sku.Jan1;

	/// <summary>対象倉庫の現在庫</summary>
	public int ZaikoSu { get; init; }
	/// <summary>入荷予定数（未消込発注）</summary>
	public int NyukaYoteiSu { get; init; }
	/// <summary>修正対象以外の指示数</summary>
	public int OtherShijiSu { get; init; }

	/// <summary>全店舗の今回入力合計</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(NokoriSu))]
	public partial int HaibunTotalSu { get; set; }

	/// <summary>残 = 在庫 + 入荷予定 − 他指示 − 今回配分合計</summary>
	public int NokoriSu => ZaikoSu + NyukaYoteiSu - OtherShijiSu - HaibunTotalSu;

	static string JoinCodeName(string? code, string? name) {
		string cd = code?.Trim() ?? string.Empty;
		string mei = name?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}
}

/// <summary>配分先店舗1件分の入力状態（SKU 行の集合）。</summary>
public sealed partial class ShopHaibunTenpoEntry(MasterTokui tenpo) : ObservableObject {
	public long Id_Tenpo => tenpo.Id;
	public string TenpoCode => tenpo.Code;
	public string TenpoName => tenpo.Name;
	public string TenpoDisplay => $"{tenpo.Code} {tenpo.Name}";

	public ObservableCollection<ShopHaibunEntryRow> Rows { get; } = [];

	/// <summary>この店舗の指示数合計</summary>
	[ObservableProperty]
	public partial int TotalSu { get; set; }

	public void RefreshTotal() => TotalSu = Rows.Sum(x => x.Su);
}

/// <summary>店舗×SKU 1行分の配分入力行。</summary>
public sealed partial class ShopHaibunEntryRow(DerivedShohinColSiz sku, ShopHaibunSkuSummary summary, ShopHaibunTenpoEntry owner) : ObservableObject {
	public long Id_Col => sku.Id_Col;
	public long Id_Siz => sku.Id_Siz;
	public string ColDisplay => summary.ColDisplay;
	public string SizDisplay => summary.SizDisplay;
	public string Jan1 => sku.Jan1;

	public ShopHaibunSkuSummary Summary => summary;

	/// <summary>指示数（配分数）</summary>
	[ObservableProperty]
	public partial int Su { get; set; }

	partial void OnSuChanged(int oldValue, int newValue) {
		summary.HaibunTotalSu += newValue - oldValue;
		owner.RefreshTotal();
	}
}
