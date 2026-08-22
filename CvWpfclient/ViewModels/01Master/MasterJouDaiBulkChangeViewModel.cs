using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// 上代一括変更画面の ViewModel。
/// <para>
/// 抽出条件で対象商品を集め、率または金額で新販売価格をまとめて計算し、
/// 対象店舗（または卸先）と期間を指定して <see cref="TranJodai"/> を1件作る。
/// 確定すると <see cref="DerivedJodai"/> へ「対象 × 商品 × 期間」が展開され、
/// 売上・POS・在庫評価がその価格を引くようになる。
/// </para>
/// <para>
/// 【商品マスタは書き換えない】<see cref="MasterShohin.TankaJodai"/> は定価のまま維持し、
/// 期間つきのオーバーレイとして積む。セール終了で自動的に元価格へ戻るので戻し処理が要らない。
/// </para>
/// <para>
/// 【展開のタイミング】展開・取消は <see cref="TranJodai"/> が <c>IDerivedOrigin</c> を実装しているため
/// サーバが DerivedDb を Insert/Update/Delete と同一トランザクションで自動実行する。
/// この画面が <see cref="DerivedJodai"/> を直接触ることはない。
/// </para>
/// <para>
/// 【重複排除】<see cref="TranJodai.Normalize"/> を登録前に必ず呼ぶ。対象店舗・対象商品が重複すると
/// 展開時に <see cref="DerivedJodai"/> のユニークキー違反でトランザクションごと失敗する。
/// </para>
/// <para>設計は `.omo/20260811_jodai_table_design_plan.md`。</para>
/// </summary>
public partial class MasterJouDaiBulkChangeViewModel : BaseViewModel {

	/// <summary>コンボボックス用のマスタ1件。表示は「コード 名称」。</summary>
	public sealed record MasterOption(long Id, string Code, string Name) {
		public string Display => CodeNameDisplay.Format(Id, Code, Name, withId: false);
	}

	/// <summary>コード値と表示名の組（区分・丸め方法などの固定選択肢）。</summary>
	public sealed record CodeOption(int Value, string Name);

	/// <summary>抽出条件の検索項目。<see cref="Column"/> は商品検索SQLの列式。</summary>
	public sealed record FieldOption(string Name, string Column);

	// ===== 固定選択肢 =============================================================

	public IReadOnlyList<CodeOption> KubunOptions { get; } = [
		new((int)EnumJodaiKubun.Proper, "0 プロパー(P)"),
		new((int)EnumJodaiKubun.Sale, "1 セール(S)"),
	];

	public IReadOnlyList<CodeOption> TaishoOptions { get; } = [
		new((int)EnumJodaiTaisho.Tenpo, "0 店舗用（直営店）"),
		new((int)EnumJodaiTaisho.Honbu, "1 本部売上用（卸先・売仕店）"),
	];

	public IReadOnlyList<CodeOption> CalcTypeOptions { get; } = [
		new(0, "0 金額指定"),
		new(1, "1 率(OFF%)指定"),
	];

	public IReadOnlyList<CodeOption> RoundUnitOptions { get; } = [
		new(0, "0 1円"), new(1, "1 10円"), new(2, "2 百円"), new(3, "3 千円"),
	];

	public IReadOnlyList<CodeOption> RoundTypeOptions { get; } = [
		new(0, "0 切捨"), new(1, "1 四捨五入"), new(2, "2 切上"),
	];

	public IReadOnlyList<CodeOption> ZaikoOptions { get; } = [
		new(0, "0 在庫無視"), new(1, "1 在庫アリ"),
	];

	/// <summary>
	/// 抽出条件の検索項目。<b>DataGridComboBoxColumn は視覚ツリーの外にあり DataContext を辿れない</b>ため、
	/// 列の ItemsSource から <c>x:Static</c> で直接参照できるよう静的に公開する。
	/// </summary>
	public static IReadOnlyList<FieldOption> FieldOptionsStatic { get; } = [
		new("(未指定)", ""),
		new("商品CD", "M.Code"),
		new("メーカー品番", "M.MakerHin"),
		new("ブランド", "Brd.Code"),
		new("アイテム", "Item.Code"),
		new("メーカー", "Mkr.Code"),
		new("シーズン", "Sea.Code"),
	];

	public IReadOnlyList<FieldOption> FieldOptions => FieldOptionsStatic;

	// ===== 画面状態 ===============================================================

	[ObservableProperty]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	// ===== タブ1: 検索画面 ========================================================

	[ObservableProperty]
	public partial DateTime? SearchDayFrom { get; set; } = DateTime.Today.AddMonths(-3);

	[ObservableProperty]
	public partial DateTime? SearchDayTo { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial string SearchTitle { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<JodaiListRow> ListRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	public partial JodaiListRow? SelectedListRow { get; set; }

	// ===== タブ2: 修正・登録画面（ヘッダ） ========================================

	/// <summary>編集中の伝票Id。0 なら新規。</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DenNoText))]
	[NotifyCanExecuteChangedFor(nameof(DoFixCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoCancelDenCommand))]
	public partial long EditId { get; set; }

	/// <summary>楽観ロック用。読み込んだ伝票の Vdu。</summary>
	long editVdu;

	public string DenNoText => EditId > 0 ? EditId.ToString("N0", CultureInfo.InvariantCulture) : "(新規)";

	[ObservableProperty]
	public partial int EditKubun { get; set; } = (int)EnumJodaiKubun.Sale;

	[ObservableProperty]
	public partial int EditTaishoType { get; set; } = (int)EnumJodaiTaisho.Tenpo;

	[ObservableProperty]
	public partial MasterOption? SelectedSale { get; set; }

	[ObservableProperty]
	public partial string EditTitle { get; set; } = string.Empty;

	[ObservableProperty]
	public partial MasterOption? SelectedShain { get; set; }

	[ObservableProperty]
	public partial DateTime? EditDayFrom { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? EditDayTo { get; set; } = DateTime.Today.AddMonths(1);

	[ObservableProperty]
	public partial string EditMemo { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(StatusName))]
	[NotifyCanExecuteChangedFor(nameof(DoFixCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoCancelDenCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoMarkSentCommand))]
	public partial int EditStatus { get; set; }

	public string StatusName => StatusToName(EditStatus);

	/// <summary>
	/// 送信状態。店頭の値札・棚札を差し替えたかどうかの運用管理に使う。
	/// 価格そのものは POS がサーバの適用上代を直接引くので、配信処理は不要。
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SendFlgName))]
	[NotifyCanExecuteChangedFor(nameof(DoMarkSentCommand))]
	public partial int EditSendFlg { get; set; }

	public string SendFlgName => SendFlgToName(EditSendFlg);

	[ObservableProperty]
	public partial int EditExpandCnt { get; set; }

	// ===== タブ2: 一括変更条件 ====================================================

	[ObservableProperty]
	public partial int CalcType { get; set; } = 1;

	[ObservableProperty]
	public partial string CalcRateText { get; set; } = "0.00";

	[ObservableProperty]
	public partial string CalcValueText { get; set; } = "0";

	[ObservableProperty]
	public partial int RoundUnit { get; set; } = 2;

	[ObservableProperty]
	public partial int RoundType { get; set; }

	// ===== タブ2: 抽出条件 ========================================================

	[ObservableProperty]
	public partial ObservableCollection<JodaiCondRow> CondRows { get; set; } = [];

	[ObservableProperty]
	public partial int ZaikoJoken { get; set; }

	[ObservableProperty]
	public partial string MaxCountText { get; set; } = "1000";

	// ===== タブ2: 対象店舗 / 明細 =================================================

	[ObservableProperty]
	public partial ObservableCollection<JodaiShopRow> ShopRows { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<JodaiMeisaiRow> MeisaiRows { get; set; } = [];

	[ObservableProperty]
	public partial JodaiMeisaiRow? SelectedMeisaiRow { get; set; }

	/// <summary>店舗別期間の一括設定用。</summary>
	[ObservableProperty]
	public partial DateTime? ShopDayFrom { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? ShopDayTo { get; set; } = DateTime.Today.AddMonths(1);

	public int TargetShopCount => ShopRows.Count(x => x.IsTarget);
	public int MeisaiCount => MeisaiRows.Count;
	public long ExpandEstimate => (long)Math.Max(TargetShopCount, 0) * MeisaiRows.Count;

	// ===== マスタ選択肢 ===========================================================

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> SaleOptions { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> ShainOptions { get; set; } = [];

	/// <summary>消費税率(%)。税込価格の表示に使う。</summary>
	int taxRate = 10;

	public MasterJouDaiBulkChangeViewModel() {
		ResetCondRows();
	}

	// ===== 初期化 =================================================================

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		try {
			StartBusy("マスタ取得中...");
			SaleOptions = new ObservableCollection<MasterOption>(
				await LoadMeishoOptionsAsync("SLE", ct));
			ShainOptions = new ObservableCollection<MasterOption>(
				await LoadOptionsAsync<MasterShain>("MasterShain", string.Empty, ct));
			taxRate = await AppGlobal.LogicGetTax(1, ToDay(DateTime.Today));
			await LoadListAsync(ct);
			Message = "検索画面で伝票を選ぶか、[新規] で上代変更を作成してください";
		}
		catch (OperationCanceledException) {
			// 画面を閉じた等。何もしない
		}
		catch (Exception ex) {
			Message = $"初期化失敗: {ex.Message}";
		}
		finally {
			FinishBusy();
		}
	}

	// ===== タブ1: 検索 ============================================================

	[RelayCommand]
	async Task DoSearch(CancellationToken ct) {
		try {
			StartBusy("伝票検索中...");
			await LoadListAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 上代変更伝票 {ListRows.Count:N0} 件";
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

	/// <summary>
	/// 伝票一覧を読み込む。<b>JSON列(Jcond/Jshop/Jmeisai)は SELECT しない。</b>
	/// 明細数千件で数百KBになるため、一覧では件数列(ShopCnt/MeisaiCnt)だけを見る。
	/// <para>
	/// 展開数は <see cref="TranJodai.ExpandCnt"/> 列ではなく <see cref="DerivedJodai"/> を数えて出す。
	/// 展開はサーバが DerivedDb で自動実行するので、この画面から保存しても列側は更新されず
	/// 常に 0 のままになるため（列の更新は修復用の <c>JodaiDb.Rebuild()</c> のみが行う）。
	/// 相関サブクエリは <c>nk2(Id_Tran)</c> のインデックスで引ける。
	/// </para>
	/// </summary>
	async Task LoadListAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [];
		if (SearchDayFrom != null) clauses.Add($"J.DenDay >= {AddParameter(parameters, ToDay(SearchDayFrom.Value))}");
		if (SearchDayTo != null) clauses.Add($"J.DenDay <= {AddParameter(parameters, ToDay(SearchDayTo.Value))}");
		if (!string.IsNullOrWhiteSpace(SearchTitle)) clauses.Add($"J.Title LIKE {AddParameter(parameters, $"%{SearchTitle.Trim()}%")}");
		var where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		var sql = $@"
SELECT J.Id, J.Vdc, J.Vdu, J.DenDay, J.Kubun, J.TaishoType, J.Id_Sale, J.VSale, J.Title, J.Id_Shain, J.VShain,
       J.DayFrom, J.DayTo, J.Status, J.FixDay, J.SendFlg, J.ShopCnt, J.MeisaiCnt, J.Memo,
       (SELECT COUNT(*) FROM {nameof(DerivedJodai)} D WHERE D.Id_Tran = J.Id) AS ExpandCnt
FROM {nameof(TranJodai)} J
{where}
ORDER BY J.Id DESC
LIMIT 500";
		var list = await QuerySqlListAsync<TranJodai>(sql, parameters, ct);
		ListRows = [.. list.Select(x => new JodaiListRow(x))];
		SelectedListRow = null;
	}

	// ===== 新規・編集 =============================================================

	[RelayCommand]
	void DoNew() {
		ClearEdit();
		SelectedTabIndex = 1;
		Message = "抽出条件を指定して [明細取得] を実行し、対象店舗を選んでから [登録] してください";
	}

	bool CanGoToEdit() => SelectedListRow != null;

	[RelayCommand(CanExecute = nameof(CanGoToEdit))]
	async Task GoToEdit(CancellationToken ct) {
		if (SelectedListRow == null) return;
		try {
			StartBusy("伝票読込中...");
			await LoadEditAsync(SelectedListRow.Id, ct);
			SelectedTabIndex = 1;
			Message = $"伝票No {EditId:N0} を読み込みました（{StatusName}）";
		}
		catch (OperationCanceledException) {
			Message = "読込を中断しました";
		}
		catch (Exception ex) {
			Message = $"読込失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>伝票1件をJSON列まで含めて読み込み、編集画面へ展開する。</summary>
	async Task LoadEditAsync(long id, CancellationToken ct) {
		List<string> parameters = [];
		var sql = $"SELECT * FROM {nameof(TranJodai)} WHERE Id = {AddParameter(parameters, id)}";
		var list = await QuerySqlListAsync<TranJodai>(sql, parameters, ct);
		var den = list.FirstOrDefault() ?? throw new InvalidOperationException($"伝票が見つかりません（Id={id}）");

		EditId = den.Id;
		editVdu = den.Vdu;
		EditKubun = den.Kubun;
		EditTaishoType = den.TaishoType;
		EditTitle = den.Title;
		EditMemo = den.Memo;
		EditStatus = den.Status;
		EditSendFlg = den.SendFlg;
		EditDayFrom = ParseDay(den.DayFrom);
		EditDayTo = ParseDay(den.DayTo);
		CalcType = den.CalcType;
		CalcRateText = den.CalcRate.ToString("0.00", CultureInfo.InvariantCulture);
		CalcValueText = den.CalcValue.ToString(CultureInfo.InvariantCulture);
		RoundUnit = den.RoundUnit;
		RoundType = den.RoundType;
		SelectedSale = den.Id_Sale > 0 ? FindOrAdd(SaleOptions, den.Id_Sale, den.VSale.Cd, den.VSale.Mei) : null;
		SelectedShain = den.Id_Shain > 0 ? FindOrAdd(ShainOptions, den.Id_Shain, den.VShain.Cd, den.VShain.Mei) : null;

		CondRows = [.. den.Jcond.Select(c => new JodaiCondRow {
			No = c.No,
			Field = FieldOptions.FirstOrDefault(f => f.Name == c.Field) ?? FieldOptions[0],
			CdFrom = c.CdFrom,
			CdTo = c.CdTo,
		})];
		while (CondRows.Count < 3) CondRows.Add(new JodaiCondRow { No = CondRows.Count + 1, Field = FieldOptions[0] });
		ZaikoJoken = den.Jcond.FirstOrDefault()?.ZaikoJoken ?? 0;

		MeisaiRows = [.. den.Jmeisai.Select(m => new JodaiMeisaiRow {
			No = m.No,
			Id_Shohin = m.Id_Shohin,
			Code_Shohin = m.Code_Shohin,
			Mei_Shohin = m.Mei_Shohin,
			DayTento = m.DayTento,
			DayChange = m.DayChange,
			JodaiOld = m.JodaiOld,
			JodaiNew = m.JodaiNew,
			RateOff = m.RateOff,
			PriceInTax = m.PriceInTax,
			Status = m.Status,
		})];

		await LoadShopRowsAsync(den.Jshop, ct);
		// ExpandCnt 列は当てにならないので実際の DerivedJodai を数える
		await ReloadExpandCountAsync(ct);
		NotifyCounts();
	}

	void ClearEdit() {
		EditId = 0;
		editVdu = 0;
		EditKubun = (int)EnumJodaiKubun.Sale;
		EditTaishoType = (int)EnumJodaiTaisho.Tenpo;
		EditTitle = string.Empty;
		EditMemo = string.Empty;
		EditStatus = 0;
		EditSendFlg = 0;
		EditExpandCnt = 0;
		EditDayFrom = DateTime.Today;
		EditDayTo = DateTime.Today.AddMonths(1);
		CalcType = 1;
		CalcRateText = "0.00";
		CalcValueText = "0";
		RoundUnit = 2;
		RoundType = 0;
		SelectedSale = null;
		ZaikoJoken = 0;
		ResetCondRows();
		MeisaiRows = [];
		ShopRows = [];
		NotifyCounts();
	}

	void ResetCondRows() {
		CondRows = [
			new JodaiCondRow { No = 1, Field = FieldOptions[0] },
			new JodaiCondRow { No = 2, Field = FieldOptions[0] },
			new JodaiCondRow { No = 3, Field = FieldOptions[0] },
		];
	}

	// ===== 対象店舗 ===============================================================

	/// <summary>
	/// 対象一覧を読み込む。<see cref="EditTaishoType"/> により
	/// 店舗用は直営店(TenType=6)、本部売上用は卸先・売仕店(TenType in (1,3))を並べる。
	/// </summary>
	[RelayCommand]
	async Task LoadShops(CancellationToken ct) {
		try {
			StartBusy("対象一覧取得中...");
			await LoadShopRowsAsync([], ct);
			Message = $"対象候補 {ShopRows.Count:N0} 件を表示しました";
		}
		catch (OperationCanceledException) {
			Message = "取得を中断しました";
		}
		catch (Exception ex) {
			Message = $"対象一覧取得失敗: {ex.Message}";
		}
		finally {
			FinishBusy();
		}
	}

	async Task LoadShopRowsAsync(List<TranJodaiShop> selected, CancellationToken ct) {
		var where = EditTaishoType == (int)EnumJodaiTaisho.Honbu
			? "WHERE TenType IN (1, 3)"
			: "WHERE TenType = 6";
		var options = await LoadOptionsAsync<MasterTokui>("MasterTokui", where, ct);
		var selectedMap = selected.ToDictionary(x => x.Id_Tenpo, x => x);
		var rows = options.Select(o => {
			selectedMap.TryGetValue(o.Id, out var hit);
			return new JodaiShopRow {
				Id_Tenpo = o.Id,
				Code_Tenpo = o.Code,
				Mei_Tenpo = o.Name,
				IsTarget = hit != null,
				DayFrom = hit?.DayFrom ?? ToDay(EditDayFrom ?? DateTime.Today),
				DayTo = hit?.DayTo ?? ToDay(EditDayTo ?? DateTime.Today),
			};
		}).ToList();

		// マスタから消えた店舗が伝票に残っている場合も落とさずに見せる（監査値として残す）
		foreach (var miss in selected.Where(s => !rows.Any(r => r.Id_Tenpo == s.Id_Tenpo))) {
			rows.Add(new JodaiShopRow {
				Id_Tenpo = miss.Id_Tenpo,
				Code_Tenpo = miss.Code_Tenpo,
				Mei_Tenpo = string.IsNullOrEmpty(miss.Mei_Tenpo) ? "(マスタ未登録)" : miss.Mei_Tenpo,
				IsTarget = true,
				DayFrom = miss.DayFrom,
				DayTo = miss.DayTo,
			});
		}
		foreach (var row in rows) row.PropertyChanged += (_, _) => NotifyCounts();
		ShopRows = [.. rows];
		NotifyCounts();
	}

	partial void OnEditTaishoTypeChanged(int value) {
		// 系統を切り替えたら対象候補が全く別物になるので選択を捨てる
		if (ShopRows.Count > 0) ShopRows = [];
		NotifyCounts();
	}

	[RelayCommand]
	void ShopAllOn() {
		foreach (var row in ShopRows) row.IsTarget = true;
		NotifyCounts();
	}

	[RelayCommand]
	void ShopAllOff() {
		foreach (var row in ShopRows) row.IsTarget = false;
		NotifyCounts();
	}

	/// <summary>チェック済みの店舗へ期間をまとめて設定する（画面の「店舗セール期間設定」）。</summary>
	[RelayCommand]
	void ApplyShopPeriod() {
		var from = ToDay(ShopDayFrom ?? DateTime.Today);
		var to = ToDay(ShopDayTo ?? DateTime.Today);
		var cnt = 0;
		foreach (var row in ShopRows.Where(x => x.IsTarget)) {
			row.DayFrom = from;
			row.DayTo = to;
			cnt++;
		}
		Message = $"対象 {cnt:N0} 件の期間を {from}～{to} に設定しました";
	}

	// ===== 明細取得 ===============================================================

	/// <summary>
	/// 抽出条件に一致する商品を集めて明細を作り直す。上代は商品マスタの現在値を旧上代として取り込む。
	/// </summary>
	[RelayCommand]
	async Task GetMeisai(CancellationToken ct) {
		try {
			StartBusy("対象商品取得中...");
			var rows = await LoadMeisaiRowsAsync(ct);
			if (rows.Count == 0) {
				MessageEx.ShowInformationDialog("該当する商品がありませんでした。", owner: ActiveWindow);
				Message = "該当する商品がありません";
				return;
			}
			MeisaiRows = [.. rows];
			ApplyCalc();
			NotifyCounts();
			var capped = TryGetMaxCount(out var max) && rows.Count >= max
				? $" ※取得件数上限({max:N0})に達しています"
				: string.Empty;
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 対象商品 {rows.Count:N0} 件{capped}";
		}
		catch (OperationCanceledException) {
			Message = "取得を中断しました";
		}
		catch (Exception ex) {
			Message = $"明細取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	async Task<List<JodaiMeisaiRow>> LoadMeisaiRowsAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = [];
		foreach (var cond in CondRows) {
			if (string.IsNullOrEmpty(cond.Field?.Column)) continue;
			if (!string.IsNullOrWhiteSpace(cond.CdFrom))
				clauses.Add($"{cond.Field.Column} >= {AddParameter(parameters, cond.CdFrom.Trim())}");
			if (!string.IsNullOrWhiteSpace(cond.CdTo))
				clauses.Add($"{cond.Field.Column} <= {AddParameter(parameters, cond.CdTo.Trim())}");
		}
		if (ZaikoJoken == 1)
			clauses.Add("EXISTS (SELECT 1 FROM SummaryRealStock Z WHERE Z.Id_Shohin = M.Id AND Z.Su > 0)");
		var where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		TryGetMaxCount(out var maxCount);
		var sql = $@"
SELECT M.Id, M.Vdc, M.Vdu, M.Code, M.Name, M.Ryaku, M.Kana, M.MakerHin,
       M.TankaJodai, M.TankaJodaiOrg, M.TankaGenka, M.DayTento
FROM MasterShohin M
     LEFT JOIN MasterMeisho Brd  ON Brd.Id  = M.Id_Brand
     LEFT JOIN MasterMeisho Item ON Item.Id = M.Id_Item
     LEFT JOIN MasterMeisho Mkr  ON Mkr.Id  = M.Id_Maker
     LEFT JOIN MasterMeisho Sea  ON Sea.Id  = M.Id_Season
{where}
ORDER BY M.Code
LIMIT {maxCount}";
		var list = await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
		var today = ToDay(DateTime.Today);
		var no = 0;
		return [.. list.Select(m => new JodaiMeisaiRow {
			No = ++no,
			Id_Shohin = m.Id,
			Code_Shohin = m.Code ?? string.Empty,
			Mei_Shohin = m.Name ?? string.Empty,
			DayTento = m.DayTento,
			DayChange = today,
			JodaiOld = m.TankaJodai,
			JodaiNew = m.TankaJodai,
			RateOff = 0m,
			PriceInTax = CalcPriceInTax(m.TankaJodai),
			Status = 0,
		})];
	}

	// ===== 一括計算 ===============================================================

	/// <summary>率または金額と丸め条件から、全明細の新販売価格を計算し直す。</summary>
	[RelayCommand]
	void ApplyCalcAll() {
		if (MeisaiRows.Count == 0) {
			MessageEx.ShowWarningDialog("先に [明細取得] で対象商品を表示してください。", owner: ActiveWindow);
			return;
		}
		ApplyCalc();
		Message = CalcType == 1
			? $"上代から {ParseDecimal(CalcRateText):0.00}% OFF（{RoundUnitName(RoundUnit)} {RoundTypeName(RoundType)}）で {MeisaiRows.Count:N0} 件を再計算しました"
			: $"新販売価格を {ParseInt(CalcValueText):N0} 円に設定しました（{MeisaiRows.Count:N0} 件）";
	}

	void ApplyCalc() {
		var rate = ParseDecimal(CalcRateText);
		var value = ParseInt(CalcValueText);
		var today = ToDay(DateTime.Today);
		foreach (var row in MeisaiRows) {
			row.JodaiNew = CalcType == 1
				? ApplyRound((double)row.JodaiOld * (1.0 - (double)rate / 100.0), RoundUnit, RoundType)
				: value;
			row.RateOff = row.JodaiOld > 0
				? Math.Round((1m - (decimal)row.JodaiNew / row.JodaiOld) * 100m, 2, MidpointRounding.AwayFromZero)
				: 0m;
			row.PriceInTax = CalcPriceInTax(row.JodaiNew);
			row.DayChange = today;
		}
	}

	/// <summary>丸め単位と丸め方法を適用する。単位0=1円/1=10円/2=百円/3=千円。</summary>
	static int ApplyRound(double value, int unit, int type) {
		var scale = unit switch { 1 => 10.0, 2 => 100.0, 3 => 1000.0, _ => 1.0 };
		var quotient = value / scale;
		var rounded = type switch {
			1 => Math.Round(quotient, MidpointRounding.AwayFromZero),
			2 => Math.Ceiling(quotient),
			_ => Math.Floor(quotient),
		};
		var result = rounded * scale;
		return result < 0 ? 0 : (int)result;
	}

	int CalcPriceInTax(int price) => (int)Math.Round(price * (100.0 + taxRate) / 100.0, MidpointRounding.AwayFromZero);

	// ===== 登録 ===================================================================

	[RelayCommand]
	async Task DoRegister(CancellationToken ct) {
		var den = BuildDenpyo(out var error);
		if (den == null) {
			MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
			return;
		}
		var confirm = EditId > 0
			? $"伝票No {EditId:N0} を更新します。対象 {den.ShopCnt:N0} 件 × 明細 {den.MeisaiCnt:N0} 件。よろしいですか？"
			: $"上代変更伝票を登録します。対象 {den.ShopCnt:N0} 件 × 明細 {den.MeisaiCnt:N0} 件。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ActiveWindow) != MessageBoxResult.Yes) return;

		try {
			StartBusy("上代変更伝票を登録中...");
			var saved = await SaveDenpyoAsync(den, ct);
			EditId = saved.Id;
			editVdu = saved.Vdu;
			EditStatus = saved.Status;
			// ExpandCnt 列は保存では更新されないので、実際の DerivedJodai を数え直す
			await ReloadExpandCountAsync(ct);
			await LoadListAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 伝票No {saved.Id:N0} を登録しました（{StatusToName(saved.Status)}）";
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

	bool CanFix() => EditId > 0 && EditStatus == 0;

	/// <summary>
	/// 確定する。Status=1 にして保存すると、サーバ側の DerivedDb が
	/// <see cref="DerivedJodai"/> へ展開する（この画面から展開処理は呼ばない）。
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanFix))]
	async Task DoFix(CancellationToken ct) {
		var den = BuildDenpyo(out var error);
		if (den == null) {
			MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
			return;
		}
		var estimate = (long)den.ShopCnt * den.MeisaiCnt;
		if (MessageEx.ShowQuestionDialog(
				$"伝票No {EditId:N0} を確定します。\n適用上代 {estimate:N0} 行が作成され、売上・POS・在庫評価に反映されます。\nよろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) return;

		den.Status = 1;
		den.FixDay = ToDay(DateTime.Today);
		// 価格が変わったので値札・棚札の差し替えが必要。確定のたびに未送信へ戻す
		den.SendFlg = 0;
		try {
			StartBusy("確定して適用上代を展開中...");
			var saved = await SaveDenpyoAsync(den, ct);
			EditId = saved.Id;
			editVdu = saved.Vdu;
			EditStatus = saved.Status;
			EditSendFlg = saved.SendFlg;
			await ReloadExpandCountAsync(ct);
			await LoadListAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 伝票No {saved.Id:N0} を確定しました（展開 {EditExpandCnt:N0} 行）";
			MessageEx.ShowInformationDialog($"確定しました。適用上代 {EditExpandCnt:N0} 行を作成しました。", owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "確定を中断しました";
		}
		catch (Exception ex) {
			Message = $"確定失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	bool CanCancelDen() => EditId > 0 && EditStatus == 1;

	/// <summary>取消する。Status=2 で保存すると展開済みの適用上代が消える。</summary>
	[RelayCommand(CanExecute = nameof(CanCancelDen))]
	async Task DoCancelDen(CancellationToken ct) {
		if (MessageEx.ShowQuestionDialog(
				$"伝票No {EditId:N0} を取消します。\n展開済みの適用上代 {EditExpandCnt:N0} 行が削除され、価格は商品マスタの定価に戻ります。\nよろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) return;
		var den = BuildDenpyo(out var error);
		if (den == null) {
			MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
			return;
		}
		den.Status = 2;
		try {
			StartBusy("取消中...");
			var saved = await SaveDenpyoAsync(den, ct);
			editVdu = saved.Vdu;
			EditStatus = saved.Status;
			await ReloadExpandCountAsync(ct);
			await LoadListAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 伝票No {saved.Id:N0} を取消しました";
			MessageEx.ShowInformationDialog("取消しました。", owner: ActiveWindow);
		}
		catch (OperationCanceledException) {
			Message = "取消を中断しました";
		}
		catch (Exception ex) {
			Message = $"取消失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	bool CanMarkSent() => EditId > 0 && EditStatus == 1 && EditSendFlg != 2;

	/// <summary>
	/// 送信済みにする。<b>価格の配信処理ではない。</b>
	/// <para>
	/// cv10 の POS はサーバの適用上代を直接引くため価格配信は不要で、この操作は
	/// 「店頭の値札・棚札を差し替え終わった」ことを記録する運用管理用のマーク。
	/// 確定し直すと未送信へ戻る（価格が変わったので貼り替えが再度必要になるため）。
	/// </para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanMarkSent))]
	async Task DoMarkSent(CancellationToken ct) {
		if (MessageEx.ShowQuestionDialog(
				$"伝票No {EditId:N0} を送信済みにします。\n（値札・棚札の差し替えが完了した記録です。価格自体はPOSがサーバから直接引きます）\nよろしいですか？",
				owner: ActiveWindow) != MessageBoxResult.Yes) return;
		var den = BuildDenpyo(out var error);
		if (den == null) {
			MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
			return;
		}
		den.SendFlg = 2;
		try {
			StartBusy("送信済みに更新中...");
			var saved = await SaveDenpyoAsync(den, ct);
			editVdu = saved.Vdu;
			EditSendFlg = saved.SendFlg;
			await LoadListAsync(ct);
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 伝票No {saved.Id:N0} を送信済みにしました";
		}
		catch (OperationCanceledException) {
			Message = "更新を中断しました";
		}
		catch (Exception ex) {
			Message = $"送信済み更新失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>画面の入力から伝票を組み立てる。検証に失敗したら null と理由を返す。</summary>
	TranJodai? BuildDenpyo(out string error) {
		error = string.Empty;
		if (EditDayFrom == null || EditDayTo == null) {
			error = "適用期間を入力してください。";
			return null;
		}
		if (ToDay(EditDayFrom.Value).CompareTo(ToDay(EditDayTo.Value)) > 0) {
			error = "適用期間の開始日が終了日より後になっています。";
			return null;
		}
		var shops = ShopRows.Where(x => x.IsTarget).ToList();
		if (shops.Count == 0) {
			error = "対象を1件以上チェックしてください。";
			return null;
		}
		if (MeisaiRows.Count == 0) {
			error = "[明細取得] で対象商品を表示してください。";
			return null;
		}
		var badPeriod = shops.FirstOrDefault(s => string.Compare(s.DayFrom, s.DayTo, StringComparison.Ordinal) > 0);
		if (badPeriod != null) {
			error = $"対象 {badPeriod.Code_Tenpo} {badPeriod.Mei_Tenpo} の期間が逆転しています（{badPeriod.DayFrom}～{badPeriod.DayTo}）。";
			return null;
		}

		var den = new TranJodai {
			Id = EditId,
			Vdu = editVdu,
			DenDay = ToDay(DateTime.Today),
			Kubun = EditKubun,
			TaishoType = EditTaishoType,
			Id_Sale = SelectedSale?.Id ?? 0,
			VSale = new CodeNameView(SelectedSale?.Id ?? 0, SelectedSale?.Code ?? string.Empty, SelectedSale?.Name ?? string.Empty),
			Title = EditTitle,
			Id_Shain = SelectedShain?.Id ?? 0,
			VShain = new CodeNameView(SelectedShain?.Id ?? 0, SelectedShain?.Code ?? string.Empty, SelectedShain?.Name ?? string.Empty),
			DayFrom = ToDay(EditDayFrom.Value),
			// プロパー(P)は無期限オーバーレイとして扱うので終了日を 99991231 に寄せる
			DayTo = EditKubun == (int)EnumJodaiKubun.Proper ? "99991231" : ToDay(EditDayTo.Value),
			CalcType = CalcType,
			CalcRate = ParseDecimal(CalcRateText),
			CalcValue = ParseInt(CalcValueText),
			RoundUnit = RoundUnit,
			RoundType = RoundType,
			Status = EditStatus,
			FixDay = EditStatus == 1 ? ToDay(DateTime.Today) : string.Empty,
			SendFlg = EditSendFlg,
			Memo = EditMemo,
			Jcond = [.. CondRows.Where(c => !string.IsNullOrEmpty(c.Field?.Column)).Select((c, i) => new TranJodaiCond {
				No = i + 1,
				Field = c.Field!.Name,
				CdFrom = c.CdFrom,
				CdTo = c.CdTo,
				ZaikoJoken = ZaikoJoken,
				TenkaiTani = 0,
			})],
			Jshop = [.. shops.Select(s => new TranJodaiShop {
				Id_Tenpo = s.Id_Tenpo,
				Code_Tenpo = s.Code_Tenpo,
				Mei_Tenpo = s.Mei_Tenpo,
				DayFrom = s.DayFrom,
				DayTo = EditKubun == (int)EnumJodaiKubun.Proper ? "99991231" : s.DayTo,
			})],
			Jmeisai = [.. MeisaiRows.Select(m => new TranJodaiMeisai {
				No = m.No,
				Id_Shohin = m.Id_Shohin,
				Code_Shohin = m.Code_Shohin,
				Mei_Shohin = m.Mei_Shohin,
				JodaiOld = m.JodaiOld,
				JodaiNew = m.JodaiNew,
				RateOff = m.RateOff,
				PriceInTax = m.PriceInTax,
				DayTento = m.DayTento,
				DayChange = m.DayChange,
				Status = m.Status,
			})],
		};

		// 重複したまま確定すると DerivedJodai のユニークキー違反で保存自体が失敗するので、必ず取り除く
		var duplicates = den.FindDuplicates();
		if (duplicates.Count > 0) {
			var head = string.Join("\n", duplicates.Take(5));
			var more = duplicates.Count > 5 ? $"\n… 他 {duplicates.Count - 5} 件" : string.Empty;
			if (MessageEx.ShowQuestionDialog(
					$"重複があります。後に指定した内容を残して取り除きます。続行しますか？\n{head}{more}",
					owner: ActiveWindow) != MessageBoxResult.Yes) {
				error = "重複があるため登録を中止しました。";
				return null;
			}
		}
		den.Normalize();
		return den;
	}

	/// <summary>伝票を新規登録または更新し、サーバが返した最新の伝票を返す。</summary>
	async Task<TranJodai> SaveDenpyoAsync(TranJodai den, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var isNew = den.Id <= 0;
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = isNew ? typeof(InsertParam) : typeof(UpdateParam),
			DataMsg = isNew
				? Common.SerializeObject(new InsertParam(typeof(TranJodai), JsonConvert.SerializeObject(den)))
				: Common.SerializeObject(new UpdateParam(typeof(TranJodai), JsonConvert.SerializeObject(den))),
		};
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバ登録でエラーが発生しました");
		}
		var saved = Common.DeserializeObject(reply.DataMsg ?? "{}", typeof(TranJodai)) as TranJodai;
		return saved ?? den;
	}

	/// <summary>
	/// 確定・取消のあと、実際に展開された適用上代の行数を数えて画面へ反映する。
	/// 展開はサーバ側で自動実行されるため、件数は <see cref="DerivedJodai"/> を数えるのが確実。
	/// </summary>
	async Task ReloadExpandCountAsync(CancellationToken ct) {
		List<string> parameters = [];
		var sql = $"SELECT Id, Vdc, Vdu, Status FROM {nameof(TranJodai)} WHERE Id = {AddParameter(parameters, EditId)}";
		var list = await QuerySqlListAsync<TranJodai>(sql, parameters, ct);
		if (list.FirstOrDefault() is TranJodai den) EditStatus = den.Status;

		parameters.Clear();
		sql = $"SELECT Id, Vdc, Vdu, Id_Tran FROM {nameof(DerivedJodai)} WHERE Id_Tran = {AddParameter(parameters, EditId)}";
		var rows = await QuerySqlListAsync<DerivedJodai>(sql, parameters, ct);
		EditExpandCnt = rows.Count;
	}

	// ===== 選択ダイアログ =========================================================

	[RelayCommand]
	void SelectSaleDialog() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterMeisho>(this, typeof(MasterMeisho), "Kubun='SLE'", "Code",
			startPos: SelectedSale?.Id ?? 0);
		if (selected == null) return;
		SelectedSale = FindOrAdd(SaleOptions, selected.Id, selected.Code, selected.Name);
	}

	[RelayCommand]
	void SelectShainDialog() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterShain>(this, typeof(MasterShain), "", "Code",
			startPos: SelectedShain?.Id ?? 0);
		if (selected == null) return;
		SelectedShain = FindOrAdd(ShainOptions, selected.Id, selected.Code, selected.Name);
	}

	// ===== 共通ヘルパ =============================================================

	void NotifyCounts() {
		OnPropertyChanged(nameof(TargetShopCount));
		OnPropertyChanged(nameof(MeisaiCount));
		OnPropertyChanged(nameof(ExpandEstimate));
	}

	static MasterOption FindOrAdd(ObservableCollection<MasterOption> options, long id, string? code, string? name) {
		var found = options.FirstOrDefault(x => x.Id == id);
		if (found != null) return found;
		var added = new MasterOption(id, code ?? string.Empty, name ?? string.Empty);
		options.Add(added);
		return added;
	}

	async Task<List<MasterOption>> LoadOptionsAsync<T>(string tableName, string where, CancellationToken ct)
		where T : BaseDbClass, IBaseCodeName {
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM {tableName}
{where}
ORDER BY Code";
		var list = await QuerySqlListAsync<T>(sql, [], ct);
		return [.. list.Select(x => new MasterOption(x.Id, x.Code ?? string.Empty, x.Name ?? string.Empty))];
	}

	async Task<List<MasterOption>> LoadMeishoOptionsAsync(string kubun, CancellationToken ct) {
		List<string> parameters = [];
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM MasterMeisho
WHERE Kubun = {AddParameter(parameters, kubun)}
ORDER BY Odr, Code";
		var list = await QuerySqlListAsync<MasterMeisho>(sql, parameters, ct);
		return [.. list.Select(x => new MasterOption(x.Id, x.Code ?? string.Empty, x.Name ?? string.Empty))];
	}

	Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	bool TryGetMaxCount(out int maxCount) {
		maxCount = int.TryParse(MaxCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
			? Math.Min(v, 20000) : 1000;
		return true;
	}

	static string ToDay(DateTime value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	static DateTime? ParseDay(string? day) =>
		DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
			? value : null;

	static decimal ParseDecimal(string? text) =>
		decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

	static int ParseInt(string? text) =>
		int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

	internal static string StatusToName(int status) => status switch {
		1 => "確定",
		2 => "取消",
		_ => "入力中",
	};

	internal static string SendFlgToName(int sendFlg) => sendFlg switch {
		1 => "送信中",
		2 => "送信済",
		_ => "未送信",
	};

	internal static string KubunToName(int kubun) => kubun == (int)EnumJodaiKubun.Proper ? "プロパー" : "セール";

	internal static string TaishoToName(int taisho) => taisho == (int)EnumJodaiTaisho.Honbu ? "本部売上" : "店舗";

	static string RoundUnitName(int unit) => unit switch { 1 => "10円", 2 => "百円", 3 => "千円", _ => "1円" };

	static string RoundTypeName(int type) => type switch { 1 => "四捨五入", 2 => "切上", _ => "切捨" };

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
}

/// <summary>タブ1の一覧行。JSON列は読まないので件数列で規模を示す。</summary>
public sealed class JodaiListRow {
	public long Id { get; }
	public string DenDay { get; }
	public string KubunName { get; }
	public string TaishoName { get; }
	public string SaleName { get; }
	public string Title { get; }
	public string Period { get; }
	public int ShopCnt { get; }
	public int MeisaiCnt { get; }
	public int ExpandCnt { get; }
	public string StatusName { get; }
	public string SendFlgName { get; }
	public string ShainName { get; }

	public JodaiListRow(TranJodai den) {
		Id = den.Id;
		DenDay = den.DenDay;
		KubunName = MasterJouDaiBulkChangeViewModel.KubunToName(den.Kubun);
		TaishoName = MasterJouDaiBulkChangeViewModel.TaishoToName(den.TaishoType);
		SaleName = string.IsNullOrEmpty(den.VSale.Cd) ? string.Empty : $"{den.VSale.Cd} {den.VSale.Mei}";
		Title = den.Title;
		Period = $"{den.DayFrom}～{den.DayTo}";
		ShopCnt = den.ShopCnt;
		MeisaiCnt = den.MeisaiCnt;
		ExpandCnt = den.ExpandCnt;
		StatusName = MasterJouDaiBulkChangeViewModel.StatusToName(den.Status);
		SendFlgName = MasterJouDaiBulkChangeViewModel.SendFlgToName(den.SendFlg);
		ShainName = string.IsNullOrEmpty(den.VShain.Cd) ? string.Empty : $"{den.VShain.Cd} {den.VShain.Mei}";
	}
}

/// <summary>抽出条件の1行。</summary>
public partial class JodaiCondRow : ObservableObject {
	[ObservableProperty]
	public partial int No { get; set; }

	[ObservableProperty]
	public partial MasterJouDaiBulkChangeViewModel.FieldOption? Field { get; set; }

	[ObservableProperty]
	public partial string CdFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string CdTo { get; set; } = string.Empty;
}

/// <summary>対象店舗（または卸先）の1行。期間は店舗ごとに持つ。</summary>
public partial class JodaiShopRow : ObservableObject {
	[ObservableProperty]
	public partial bool IsTarget { get; set; }

	[ObservableProperty]
	public partial long Id_Tenpo { get; set; }

	[ObservableProperty]
	public partial string Code_Tenpo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Mei_Tenpo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DayFrom { get; set; } = "19010101";

	[ObservableProperty]
	public partial string DayTo { get; set; } = "99991231";
}

/// <summary>対象明細の1行（商品マスタ単位）。</summary>
public partial class JodaiMeisaiRow : ObservableObject {
	[ObservableProperty]
	public partial int No { get; set; }

	[ObservableProperty]
	public partial long Id_Shohin { get; set; }

	[ObservableProperty]
	public partial string Code_Shohin { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Mei_Shohin { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DayTento { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DayChange { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int JodaiOld { get; set; }

	[ObservableProperty]
	public partial int JodaiNew { get; set; }

	[ObservableProperty]
	public partial decimal RateOff { get; set; }

	[ObservableProperty]
	public partial int PriceInTax { get; set; }

	[ObservableProperty]
	public partial int Status { get; set; }
}
