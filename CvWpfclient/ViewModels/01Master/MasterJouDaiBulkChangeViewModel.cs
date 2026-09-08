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
using System.Collections.Specialized;
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

	/// <summary>
	/// 抽出条件の検索項目。<see cref="Column"/> は商品検索SQLの列式。
	/// <see cref="IsNumeric"/> が true の項目は数値としてパラメータ化して比較する（文字列比較にすると
	/// "9800" &gt; "12800" のように桁数で逆転するため）。
	/// <see cref="JsubKb"/> が設定されている項目は <see cref="MasterShohin.Jsub"/>（商品分類の枠）に対する
	/// 条件で、<see cref="Column"/> は使わず <c>json_each</c> の EXISTS で組み立てる。
	/// </summary>
	public sealed record FieldOption(string Name, string Column, bool IsNumeric = false, string? JsubKb = null) {
		/// <summary>「(未指定)」以外で、実際に条件を構成できる項目か。</summary>
		public bool IsSelectable => !string.IsNullOrEmpty(Column) || !string.IsNullOrEmpty(JsubKb);
	}

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
	/// 抽出条件の検索項目の固定分。商品分類(<c>Jsub</c>)の枠は <see cref="MasterMeisho"/> の登録内容に
	/// よって増減するため、ここには含めず <see cref="LoadJsubFieldOptionsAsync"/> で動的に追加する。
	/// </summary>
	/// <remarks>
	/// 【仕入先は実装しない】設計書 5.2 は「仕入先 Sir.Code」を挙げているが、<see cref="MasterShohin"/> には
	/// 一般の仕入先への外部キーが無い。あるのは <c>Id_ConsignmentShiire</c>（消化仕入専用）のみで、
	/// 他は <c>Jgenka</c> サブテーブル内（原価4項目設計により「新しい原価解決には使用しない」とされた
	/// 移行保持データ）。実装せず検索項目にも追加しない。設計書は別途更新される。
	/// </remarks>
	static readonly IReadOnlyList<FieldOption> BaseFieldOptions = [
		new("(未指定)", ""),
		new("商品CD", "M.Code"),
		new("メーカー品番", "M.MakerHin"),
		new("ブランド", "Brd.Code"),
		new("アイテム", "Item.Code"),
		new("メーカー", "Mkr.Code"),
		new("シーズン", "Sea.Code"),
		new("素材", "Szi.Code"),
		new("原産国", "Gen.Code"),
		new("発売日(店頭投入日)", "M.DayTento"),
		new("現在上代", "M.TankaJodai", IsNumeric: true),
	];

	/// <summary>
	/// 抽出条件の検索項目。<b>DataGridComboBoxColumn は視覚ツリーの外にあり DataContext を辿れない</b>ため、
	/// XAML 側は <c>DataGridTemplateColumn</c> + <c>ComboBox</c> から
	/// <c>DataContext.FieldOptions, RelativeSource={RelativeSource AncestorType=DataGrid}</c> で参照する。
	/// これにより Jsub の枠のように実行時に増減する選択肢を持てる。
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<FieldOption> FieldOptions { get; set; } = new(BaseFieldOptions);

	public IReadOnlyList<CodeOption> OpeOptions { get; } = [
		new(0, "AND"),
		new(1, "OR"),
	];

	// ===== ② 適用範囲（Scope）固定選択肢 ==========================================

	public IReadOnlyList<CodeOption> RangeTypeOptions { get; } = [
		new((int)EnumJodaiRangeType.All, "0 全店"),
		new((int)EnumJodaiRangeType.PriceGroup, "1 価格グループ"),
		new((int)EnumJodaiRangeType.Store, "2 個別店舗"),
	];

	public IReadOnlyList<CodeOption> GroupAxisOptions { get; } = [
		new((int)EnumJodaiGroupAxis.PriceGroup, "0 価格グループ"),
		new((int)EnumJodaiGroupAxis.PriceArea, "1 地域"),
		new((int)EnumJodaiGroupAxis.PriceChannel, "2 チャネル"),
	];

	public IReadOnlyList<CodeOption> IncExcOptions { get; } = [
		new((int)EnumJodaiIncExc.Include, "0 対象"),
		new((int)EnumJodaiIncExc.Exclude, "1 除外"),
	];

	public IReadOnlyList<CodeOption> PriceMethodOptions { get; } = [
		new((int)EnumJodaiPriceMethod.FixedPrice, "0 固定額"),
		new((int)EnumJodaiPriceMethod.RateOff, "1 値下率"),
		new((int)EnumJodaiPriceMethod.Amount, "2 値引額"),
		new((int)EnumJodaiPriceMethod.RateOn, "3 掛率"),
		new((int)EnumJodaiPriceMethod.RateOffFromEffective, "4 実効上代からの値下率"),
		new((int)EnumJodaiPriceMethod.PricePoint, "5 価格ポイント"),
	];

	// ===== タブ2: ③ 価格（Price Matrix）固定選択肢 =================================

	/// <summary>
	/// セル一括操作（設計書5.4）の方式選択肢。<see cref="PriceMethodOptions"/>から
	/// 方式4（実効上代からの値下率）だけを除いたもの。方式4は発効日時点の実効上代解決（非同期DBアクセス）が
	/// 要り、セル単位の即時プレビューにそぐわないため対象外とする（「一括計算」で扱う）。
	/// </summary>
	public IReadOnlyList<CodeOption> BulkPriceMethodOptions { get; } = [
		new((int)EnumJodaiPriceMethod.FixedPrice, "固定額"),
		new((int)EnumJodaiPriceMethod.RateOff, "値下率(%)"),
		new((int)EnumJodaiPriceMethod.Amount, "値引額(円)"),
		new((int)EnumJodaiPriceMethod.RateOn, "掛率(%)"),
		new((int)EnumJodaiPriceMethod.PricePoint, "価格ポイント"),
	];

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

	// ===== タブ2: ② 適用範囲（Scope） =============================================

	[ObservableProperty]
	public partial ObservableCollection<JodaiScopeRow> ScopeRows { get; set; } = [];

	/// <summary>
	/// <see cref="ScopeRows"/>が丸ごと差し替えられた（<see cref="ResetScopeRows"/>・<see cref="LoadEditAsync"/>）
	/// 直後の購読先。Scopeの増減（<see cref="AddScopeRow"/>等、コレクションへの直接Add/Remove）は
	/// プロパティの再代入を経ないため、<see cref="CollectionChanged"/>を別途購読して拾う必要がある。
	/// </summary>
	ObservableCollection<JodaiScopeRow>? subscribedScopeRows;

	/// <summary>
	/// <see cref="ScopeRows"/>の増減・差し替えに合わせて Price Matrix（③価格タブ）の
	/// <see cref="JodaiMeisaiRow.Cells"/>を再同期する（設計書5.4「Jscope の変更でリビルド」）。
	/// 値そのものの再計算はしない（既存セルの値は温存する）。
	/// </summary>
	partial void OnScopeRowsChanged(ObservableCollection<JodaiScopeRow> value) {
		if (subscribedScopeRows != null) subscribedScopeRows.CollectionChanged -= ScopeRows_CollectionChanged;
		subscribedScopeRows = value;
		subscribedScopeRows.CollectionChanged += ScopeRows_CollectionChanged;
		SyncAllRowsCells();
	}

	void ScopeRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncAllRowsCells();

	[ObservableProperty]
	public partial JodaiScopeRow? SelectedScopeRow { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> PriceGroupOptions { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> PriceAreaOptions { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> PriceChannelOptions { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> PricePointOptions { get; set; } = [];

	/// <summary>Scope（RangeType=個別店舗）の店舗選択肢。<see cref="EditTaishoType"/>により対象系統が変わる。</summary>
	[ObservableProperty]
	public partial ObservableCollection<MasterOption> ScopeStoreOptions { get; set; } = [];

	/// <summary>
	/// <see cref="MasterConfig.NameJodaiMaxCells"/>のキャッシュ値（画面操作時の簡易チェック用）。
	/// 保存直前の最終判定は <see cref="BuildDenpyoAsync"/> で都度DBから読み直す。
	/// </summary>
	int cachedJodaiMaxCells = 30000;

	/// <summary>
	/// <see cref="MasterConfig.NameJodaiMinPrice"/>のキャッシュ値（設計3.8・2.8のC8）。
	/// Price Matrix（③価格タブ）のセル背景警告に使う。0なら判定しない。
	/// <c>JodaiConflictChecker.GetJodaiMinPrice</c>（CvDomainLogic）と同じ既定（未設定・不正値は0）。
	/// </summary>
	int cachedJodaiMinPrice;

	/// <summary>
	/// <see cref="LoadEditAsync"/>で読み込んだ直後の<c>Jshop</c>。<see cref="BuildDenpyoAsync"/>で
	/// 解決結果とマージし、店舗ごとの期間微調整（設計書3.3・U5）を保存のたびに失わないようにする。
	/// </summary>
	List<TranJodaiShop> loadedJshop = [];

	/// <summary>
	/// <see cref="LoadShopRowsAsync"/>で取得した<see cref="MasterTokui"/>本体のキャッシュ（価格グループ3軸を含む）。
	/// <see cref="JodaiScopeResolver.Resolve"/>に渡す店舗一覧はこのキャッシュから作る
	/// （<see cref="JodaiShopRow"/>はUI表示専用で価格グループ列を持たないため）。
	/// </summary>
	List<MasterTokui> shopMasters = [];

	// ===== タブ2: 対象店舗 / 明細 =================================================

	[ObservableProperty]
	public partial ObservableCollection<JodaiShopRow> ShopRows { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<JodaiMeisaiRow> MeisaiRows { get; set; } = [];

	[ObservableProperty]
	public partial JodaiMeisaiRow? SelectedMeisaiRow { get; set; }

	// ===== タブ2: ③ 価格（Price Matrix）============================================

	/// <summary>Price Matrixで選択中のセル数（画面表示・一括操作ボタンのCanExecute用）。</summary>
	[ObservableProperty]
	public partial int SelectedMatrixCellCount { get; set; }

	/// <summary>セル一括操作（設計書5.4）の方式。既定は値下率。</summary>
	[ObservableProperty]
	public partial int BulkMethod { get; set; } = (int)EnumJodaiPriceMethod.RateOff;

	/// <summary>セル一括操作の値（方式により率/額/固定額の意味が変わる）。</summary>
	[ObservableProperty]
	public partial string BulkValueText { get; set; } = "30";

	/// <summary>セル一括操作で方式=価格ポイントのときに使う価格ポイント表。</summary>
	[ObservableProperty]
	public partial MasterOption? BulkPricePoint { get; set; }

	/// <summary>
	/// Price Matrix（<see cref="MasterJouDaiBulkChangeView"/>のDataGrid）で選択中のセル
	/// （商品行×Scope）。<c>DataGrid.SelectedCells</c>はバインドできないため、View側の
	/// <c>SelectedCellsChanged</c>イベントから<see cref="SetSelectedMatrixCells"/>経由で受け取る。
	/// </summary>
	List<(JodaiMeisaiRow Row, int No_Scope)> selectedMatrixCells = [];

	/// <summary>View側から選択セルの一覧を受け取る（コードビハインドはUI固有の取得のみ担当し、業務ロジックは持たない）。</summary>
	public void SetSelectedMatrixCells(IReadOnlyList<(JodaiMeisaiRow Row, int No_Scope)> cells) {
		selectedMatrixCells = [.. cells];
		SelectedMatrixCellCount = selectedMatrixCells.Count;
		ApplyBulkOperationCommand.NotifyCanExecuteChanged();
	}

	/// <summary>対象SKU数（DerivedShohinColSiz＝色×サイズ展開の件数）。抽出後、確定前に表示する。</summary>
	[ObservableProperty]
	public partial int TargetSkuCount { get; set; }

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
		ResetScopeRows();
	}

	// ===== 初期化 =================================================================

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		try {
			StartBusy("マスタ取得中...");
			SaleOptions = new ObservableCollection<MasterOption>(
				await LoadMeishoOptionsAsync(MasterMeisho.KubunSale, ct));
			ShainOptions = new ObservableCollection<MasterOption>(
				await LoadOptionsAsync<MasterShain>("MasterShain", string.Empty, ct));
			PriceGroupOptions = new ObservableCollection<MasterOption>(
				await LoadMeishoOptionsAsync(MasterMeisho.KubunPriceGroup, ct));
			PriceAreaOptions = new ObservableCollection<MasterOption>(
				await LoadMeishoOptionsAsync(MasterMeisho.KubunPriceArea, ct));
			PriceChannelOptions = new ObservableCollection<MasterOption>(
				await LoadMeishoOptionsAsync(MasterMeisho.KubunPriceChannel, ct));
			PricePointOptions = new ObservableCollection<MasterOption>(
				await LoadMeishoOptionsAsync(MasterMeisho.KubunPricePoint, ct));
			await LoadJsubFieldOptionsAsync(ct);
			taxRate = await AppGlobal.LogicGetTax(1, ToDay(DateTime.Today));
			cachedJodaiMaxCells = await GetConfigIntAsync(MasterConfig.NameJodaiMaxCells, 30000, ct);
			cachedJodaiMinPrice = await GetConfigIntAsync(MasterConfig.NameJodaiMinPrice, 0, ct);
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
		// Scope概念導入前の既存伝票（Jscopeが空）は、メモリ上でだけ全店Scope1件を補う（設計3.10）。
		// DBは一切書き換わらない。保存して初めて永続化される
		den.NormalizeLegacyScope();

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
			Ope = c.Ope,
		})];
		// 既存伝票の行数をそのまま表示する（3行への埋め直しはしない）。0件なら1行だけ用意する
		if (CondRows.Count == 0) CondRows.Add(new JodaiCondRow { No = 1, Field = FieldOptions[0] });
		ZaikoJoken = den.Jcond.FirstOrDefault()?.ZaikoJoken ?? 0;

		// 既存伝票のJmeisaiは「商品×Scope」のセル単位（設計3.4）。商品(Id_Shohin)ごとにグループ化し、
		// Price Matrix（③価格タブ）が期待する「商品1行×Scopeごとのセル」の形へ復元する。
		MeisaiRows = [.. den.Jmeisai.GroupBy(m => m.Id_Shohin).Select(g => {
			var first = g.First();
			var row = new JodaiMeisaiRow {
				No = first.No,
				Id_Shohin = first.Id_Shohin,
				Code_Shohin = first.Code_Shohin,
				Mei_Shohin = first.Mei_Shohin,
				DayTento = first.DayTento,
				DayChange = first.DayChange,
				JodaiOld = first.JodaiOld,
				JodaiNew = first.JodaiNew,
				RateOff = first.RateOff,
				PriceInTax = first.PriceInTax,
				Status = first.Status,
				TankaGenka = first.TankaGenka,
			};
			// ApplyComputedValueで積む（IsManuallyEditedはfalseのまま。既存伝票の再保存時、Matrixを
			// 一切触らなければ現行どおりScopeの現在値で再計算される後方互換を保つため）
			row.Cells = [.. g.Select(m => {
				var cell = new JodaiPriceCell(row) { No_Scope = m.No_Scope };
				cell.ApplyComputedValue(m.JodaiNew, m.JodaiBase);
				return cell;
			})];
			return row;
		})];

		ScopeRows = [.. den.Jscope.Select(ToScopeRow)];
		// 復元したCellsをScopeの並び順へ揃える（値は温存。既存伝票の読み込みからMatrixを復元する）
		SyncAllRowsCells();
		// 保存直前(BuildDenpyoAsync)で解決結果とマージし、店舗ごとの期間微調整(設計3.3・U5)を残すための元値
		loadedJshop = [.. den.Jshop];

		await LoadShopRowsAsync(den.Jshop, ct);
		// ExpandCnt 列は当てにならないので実際の DerivedJodai を数える
		await ReloadExpandCountAsync(ct);
		TargetSkuCount = await CountSkuAsync(MeisaiRows.ToList(), ct);
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
		ResetScopeRows();
		loadedJshop = [];
		MeisaiRows = [];
		ShopRows = [];
		TargetSkuCount = 0;
		ClearPreviewAndConflicts();
		TimelineShohinOptions = [];
		TimelineTenpoOptions = [];
		SelectedTimelineShohin = null;
		SelectedTimelineTenpo = null;
		TimelineSegments = [];
		NotifyCounts();
	}

	void ResetCondRows() {
		CondRows = [new JodaiCondRow { No = 1, Field = FieldOptions[0] }];
	}

	/// <summary>
	/// Scope一覧を「全店 / 対象 / ヘッダ既定期間 / ヘッダ既定価格ルール」の1件だけに戻す
	/// （設計書5.3「現行と同じ操作感」）。
	/// </summary>
	void ResetScopeRows() {
		ScopeRows = [ToScopeRow(DefaultHeaderScope())];
	}

	/// <summary>
	/// ヘッダの現在値（<see cref="CalcType"/>等）から、新規Scope1件ぶんの既定値を作る
	/// （設計3.6「新規Scope作成時の初期値」）。<see cref="TranJodai.NormalizeLegacyScope"/>の
	/// 補完ロジックと同じ変換規則（CalcType0→固定額 / 1→値下率）に揃える。
	/// </summary>
	TranJodaiScope DefaultHeaderScope() => new() {
		No = 1,
		Name = "全店",
		RangeType = (int)EnumJodaiRangeType.All,
		IncExc = (int)EnumJodaiIncExc.Include,
		DayFrom = ToDay(EditDayFrom ?? DateTime.Today),
		DayTo = EditKubun == (int)EnumJodaiKubun.Proper ? "99991231" : ToDay(EditDayTo ?? DateTime.Today.AddMonths(1)),
		PriceMethod = CalcType == 1 ? (int)EnumJodaiPriceMethod.RateOff : (int)EnumJodaiPriceMethod.FixedPrice,
		FixedPrice = ParseInt(CalcValueText),
		RateOff = ParseDecimal(CalcRateText),
		RoundUnit = RoundUnit,
		RoundType = RoundType,
	};

	/// <summary>抽出条件行を追加する（末尾に1行）。</summary>
	[RelayCommand]
	void AddCondRow() {
		CondRows.Add(new JodaiCondRow { No = CondRows.Count + 1, Field = FieldOptions[0] });
	}

	/// <summary>抽出条件行を削除する。最後の1行は残す（画面から条件行が無くなるのを避ける）。</summary>
	[RelayCommand]
	void RemoveCondRow(JodaiCondRow? row) {
		if (row == null || CondRows.Count <= 1) return;
		CondRows.Remove(row);
		RenumberCondRows();
	}

	/// <summary>
	/// 行Noを1から振り直す。1行目のNoは「繋ぐ相手がいないので1行目のOpeを無視する」判定
	/// （<see cref="LoadMeisaiRowsAsync"/> 側のXAML表示上の無効化）に使うため、削除後は必ず詰め直す。
	/// </summary>
	void RenumberCondRows() {
		var no = 0;
		foreach (var row in CondRows) row.No = ++no;
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
		// 価格グループ3軸(Id_PriceGroup/Area/Channel)も併せて取得し、JodaiScopeResolver.Resolveへ渡す
		// 店舗本体としてキャッシュする(JodaiShopRowはUI表示専用で3軸を持たないため)。
		List<string> masterParams = [];
		var mastersSql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana, TenType, Id_PriceGroup, Id_PriceArea, Id_PriceChannel
FROM MasterTokui
{where}
ORDER BY Code";
		shopMasters = await QuerySqlListAsync<MasterTokui>(mastersSql, masterParams, ct);
		ScopeStoreOptions = new ObservableCollection<MasterOption>(
			shopMasters.Select(m => new MasterOption(m.Id, m.Code ?? string.Empty, m.Name ?? string.Empty)));

		var options = shopMasters.Select(m => new MasterOption(m.Id, m.Code ?? string.Empty, m.Name ?? string.Empty)).ToList();
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
		foreach (var row in rows) row.PropertyChanged += (_, e) => {
			NotifyCounts();
			if (e.PropertyName == nameof(JodaiShopRow.IsTarget)) RefreshTimelineOptions();
		};
		ShopRows = [.. rows];
		NotifyCounts();
		RefreshTimelineOptions();
	}

	partial void OnEditTaishoTypeChanged(int value) {
		// 系統を切り替えたら対象候補が全く別物になるので選択を捨てる
		if (ShopRows.Count > 0) ShopRows = [];
		shopMasters = [];
		ScopeStoreOptions = [];
		NotifyCounts();
	}

	[RelayCommand]
	void ShopAllOn() {
		foreach (var row in ShopRows) row.IsTarget = true;
		NotifyCounts();
		RefreshTimelineOptions();
	}

	[RelayCommand]
	void ShopAllOff() {
		foreach (var row in ShopRows) row.IsTarget = false;
		NotifyCounts();
		RefreshTimelineOptions();
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
				MeisaiRows = [];
				TargetSkuCount = 0;
				NotifyCounts();
				MessageEx.ShowInformationDialog("該当する商品がありませんでした。", owner: ActiveWindow);
				Message = "該当する商品がありません";
				return;
			}
			// 商品数×Scope数の上限判定（設計3.11）。抽出時点で超えるなら実行前に警告して中止する
			if (!CheckMaxCellsCached(rows.Count, Math.Max(ScopeRows.Count, 1))) {
				return;
			}
			MeisaiRows = [.. rows];
			ApplyCalc();
			// Price Matrix（③価格タブ）のセルをScope数ぶん複製し（設計5.6「対象取得」）、初期値を計算する
			await RecalcCellsAsync(onlyIfNotManuallyEdited: false, ct);
			// SKU数はDerivedShohinColSiz（色×サイズ展開）の件数。抽出結果と対応するIdだけを数える
			TargetSkuCount = await CountSkuAsync(rows, ct);
			NotifyCounts();
			RefreshTimelineOptions();
			var capped = TryGetMaxCount(out var max) && rows.Count >= max
				? $" ※取得件数上限({max:N0})に達しています"
				: string.Empty;
			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 対象商品 {rows.Count:N0} 件{capped}（Style {rows.Count:N0} / SKU {TargetSkuCount:N0}）";
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

	/// <summary>
	/// 抽出条件行から WHERE 句を組み立てる。
	/// <para>
	/// 【括弧の入れ方】1行が生む &gt;= と &lt;= は必ず1単位として括弧でくくる（<c>(col &gt;= @a AND col &lt;= @b)</c>）。
	/// 行同士は左結合で、各行の <see cref="JodaiCondRow.Ope"/> がその行を直前までの式にどう繋ぐかを決める。
	/// SQLのAND/OR優先順位に振り回されないよう、都度 <c>((直前までの式) AND/OR (この行の式))</c> の形に
	/// 明示的に括弧を入れて畳み込む。1行目（＝最初に条件を持つ行）の Ope は繋ぐ相手が無いため無視する。
	/// </para>
	/// <para>
	/// 【在庫条件】<see cref="ZaikoJoken"/>=1 の EXISTS は常に最後に AND で繋ぐ（現行の意味を変えない）。
	/// </para>
	/// </summary>
	(string Where, List<string> Parameters) BuildCondWhere() {
		List<string> parameters = [];
		string? acc = null;
		foreach (var cond in CondRows) {
			var rowClause = BuildCondRowClause(cond, parameters);
			if (rowClause == null) continue;
			acc = acc == null
				? rowClause
				: $"({acc} {(cond.Ope == 1 ? "OR" : "AND")} {rowClause})";
		}
		if (ZaikoJoken == 1) {
			const string zaiko = "EXISTS (SELECT 1 FROM SummaryRealStock Z WHERE Z.Id_Shohin = M.Id AND Z.Su > 0)";
			acc = acc == null ? zaiko : $"{acc} AND {zaiko}";
		}
		return (acc == null ? string.Empty : $"WHERE {acc}", parameters);
	}

	/// <summary>条件行1行分の式を作る。条件が無い行（Field未指定またはFROM/TOとも空）は null を返す。</summary>
	static string? BuildCondRowClause(JodaiCondRow cond, List<string> parameters) {
		var field = cond.Field;
		if (field == null || !field.IsSelectable) return null;
		var from = string.IsNullOrWhiteSpace(cond.CdFrom) ? null : cond.CdFrom.Trim();
		var to = string.IsNullOrWhiteSpace(cond.CdTo) ? null : cond.CdTo.Trim();
		if (from == null && to == null) return null;

		if (!string.IsNullOrEmpty(field.JsubKb)) {
			// 商品分類(Jsub)の枠。MasterShohin.Jsub は [{Kb,Sid,Cd,Mei,Kbname}, ...] のJSON配列なので、
			// 対象の枠(Kb)を絞ったうえで、その1要素のCdを範囲比較する。従来どおり数値ではなく文字列比較でよい
			// （枠のコード値はマスタのCode文字列であり、桁揃えの数値ではないため）。
			var kbParam = AddParameter(parameters, field.JsubKb);
			var sub = $"json_extract(J.value,'$.Kb') = {kbParam}";
			if (from != null) sub += $" AND json_extract(J.value,'$.Cd') >= {AddParameter(parameters, from)}";
			if (to != null) sub += $" AND json_extract(J.value,'$.Cd') <= {AddParameter(parameters, to)}";
			// M.Jsub が null または不正JSONの商品もある（未分類）。json_each(NULL) は0件で安全だが、
			// 不正な非NULL文字列は json_each がエラーになるため、CvDomainLogic/MasterCascadeDb.SafeJsonColumn
			// と同じ考え方で json_valid() ガードを掛けてから渡す
			return $"EXISTS (SELECT 1 FROM json_each(CASE WHEN M.Jsub IS NOT NULL AND json_valid(M.Jsub) THEN M.Jsub ELSE '[]' END) J WHERE {sub})";
		}

		List<string> subs = [];
		if (from != null) {
			subs.Add(field.IsNumeric
				? $"{field.Column} >= CAST({AddParameter(parameters, from)} AS INTEGER)"
				: $"{field.Column} >= {AddParameter(parameters, from)}");
		}
		if (to != null) {
			subs.Add(field.IsNumeric
				? $"{field.Column} <= CAST({AddParameter(parameters, to)} AS INTEGER)"
				: $"{field.Column} <= {AddParameter(parameters, to)}");
		}
		return $"({string.Join(" AND ", subs)})";
	}

	const string MeisaiJoins = @"
     LEFT JOIN MasterMeisho Brd  ON Brd.Id  = M.Id_Brand
     LEFT JOIN MasterMeisho Item ON Item.Id = M.Id_Item
     LEFT JOIN MasterMeisho Mkr  ON Mkr.Id  = M.Id_Maker
     LEFT JOIN MasterMeisho Sea  ON Sea.Id  = M.Id_Season
     LEFT JOIN MasterMeisho Szi  ON Szi.Id  = M.Id_Material
     LEFT JOIN MasterMeisho Gen  ON Gen.Id  = M.Id_Country";

	async Task<List<JodaiMeisaiRow>> LoadMeisaiRowsAsync(CancellationToken ct) {
		var (where, parameters) = BuildCondWhere();
		TryGetMaxCount(out var maxCount);
		var sql = $@"
SELECT M.Id, M.Vdc, M.Vdu, M.Code, M.Name, M.Ryaku, M.Kana, M.MakerHin,
       M.TankaJodai, M.TankaJodaiOrg, M.TankaGenka, M.DayTento
FROM MasterShohin M
{MeisaiJoins}
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
			TankaGenka = m.TankaGenka,
		})];
	}

	/// <summary>
	/// 抽出された商品のSKU数（<see cref="DerivedShohinColSiz"/>＝色×サイズ展開行の件数）を数える。
	/// <para>
	/// 【20000件対策】<c>rows</c> の Id を直接 <c>IN (@0,@1,...)</c> に並べる方式は、件数が万単位になると
	/// パラメータ数がそれに比例して増え、SQLiteの変数上限（既定 SQLITE_MAX_VARIABLE_NUMBER=32766）に
	/// 接近するうえ、SQL文字列・パラメータ配列がそのまま通信されるため肥大化する。
	/// そこで <see cref="LoadMeisaiRowsAsync"/> と同じ抽出条件(WHERE句)をサブクエリとして再利用し、
	/// パラメータ数を条件行数程度（抽出件数に依存しない）に抑える。抽出結果と完全に対応させるため
	/// ORDER BY / LIMIT も同じ条件で揃える。
	/// </para>
	/// </summary>
	async Task<int> CountSkuAsync(List<JodaiMeisaiRow> rows, CancellationToken ct) {
		if (rows.Count == 0) return 0;
		var (where, parameters) = BuildCondWhere();
		TryGetMaxCount(out var maxCount);
		var sql = $@"
SELECT COUNT(*) AS Cnt
FROM {nameof(DerivedShohinColSiz)} D
WHERE D.Id_Shohin IN (
    SELECT M.Id
    FROM MasterShohin M
{MeisaiJoins}
{where}
    ORDER BY M.Code
    LIMIT {maxCount}
)";
		// QueryListSqlParam.ItemTypeはサーバ側で型解決するため、クライアント内の入れ子クラスではなく
		// 共有アセンブリ(CvBase)のScalarCountRowを使う(CvServerはCvWpfclientを参照しないため)。
		var list = await QuerySqlListAsync<ScalarCountRow>(sql, parameters, ct);
		return list.FirstOrDefault()?.Cnt ?? 0;
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

	// ===== ③ 価格（Price Matrix）==================================================

	readonly Dictionary<long, IReadOnlyList<int>> pricePointCache = [];

	/// <summary>
	/// 価格ポイント表（<see cref="EnumJodaiPriceMethod.PricePoint"/>用）のCSVを配列へ解釈してキャッシュする。
	/// <see cref="PricePointOptions"/>は<see cref="Init"/>で読み込んだあと変わらないため、Id単位でキャッシュしてよい。
	/// </summary>
	IReadOnlyList<int> PricePointsFor(long idPricePoint) {
		if (idPricePoint <= 0) return [];
		if (pricePointCache.TryGetValue(idPricePoint, out var cached)) return cached;
		var csv = PricePointOptions.FirstOrDefault(o => o.Id == idPricePoint)?.Name;
		var parsed = JodaiPriceRule.ParsePricePoints(csv);
		pricePointCache[idPricePoint] = parsed;
		return parsed;
	}

	/// <summary>
	/// 全<see cref="MeisaiRows"/>の<see cref="JodaiMeisaiRow.Cells"/>を<see cref="ScopeRows"/>へ同期する
	/// （設計書5.4「Scope 列は Jscope の変更でリビルド」）。既存セルの値は<c>No_Scope</c>が一致すれば温存し、
	/// 新しいScopeのぶんだけ簡易既定値（<see cref="DefaultCellValue"/>）で追加する。値の正式な再計算は
	/// 呼び出し側が別途<see cref="RecalcCellsAsync"/>を呼ぶこと（対象取得直後・一括計算ボタン）。
	/// </summary>
	void SyncAllRowsCells() {
		foreach (var row in MeisaiRows) {
			row.SyncCells(ScopeRows, cachedJodaiMinPrice, scope => DefaultCellValue(row, scope));
		}
	}

	/// <summary>
	/// セル追加時の簡易既定値。方式4（実効上代からの値下率）は発効日時点の実効上代解決に非同期DBアクセスが
	/// 要るため、ここでは通常上代（<see cref="JodaiMeisaiRow.JodaiOld"/>）を基準に近似する。
	/// 正式な値は<see cref="RecalcCellsAsync"/>（「一括計算」）で確定する。
	/// </summary>
	int DefaultCellValue(JodaiMeisaiRow row, JodaiScopeRow scope) => JodaiPriceRule.Calculate(
		(EnumJodaiPriceMethod)scope.PriceMethod, row.JodaiOld, scope.FixedPrice, scope.RateOff, scope.Amount,
		scope.RateOn, scope.RoundUnit, scope.RoundType, PricePointsFor(scope.Id_PricePoint));

	/// <summary>
	/// 「一括計算」（設計書5.6）。選択Scope（または全Scope）の価格ルールで、全商品×全Scopeのセルを
	/// 再計算する。方式4のセルがあれば、発効日時点の実効上代を一括解決してから使う
	/// （<see cref="BuildDenpyoAsync"/>が以前行っていたのと同じ解決方法）。
	/// </summary>
	[RelayCommand]
	async Task RecalcMatrix(CancellationToken ct) {
		if (MeisaiRows.Count == 0) {
			MessageEx.ShowWarningDialog("先に [明細取得] で対象商品を表示してください。", owner: ActiveWindow);
			return;
		}
		if (ScopeRows.Count == 0) return;
		try {
			StartBusy("Price Matrix 再計算中...");
			// 「一括計算」は明示操作なので、手動編集済みセルも含めて全セルを上書きする
			await RecalcCellsAsync(onlyIfNotManuallyEdited: false, ct);
			Message = $"Price Matrix を商品 {MeisaiRows.Count:N0} 件 × Scope {ScopeRows.Count:N0} 件で再計算しました";
		}
		catch (OperationCanceledException) {
			Message = "再計算を中断しました";
		}
		catch (Exception ex) {
			Message = $"再計算失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>
	/// Scope価格ルールでセルを再計算する。<paramref name="onlyIfNotManuallyEdited"/>=trueのときは
	/// <see cref="JodaiPriceCell.IsManuallyEdited"/>が立っているセル（手動編集・セル一括操作の結果）を
	/// 温存する。<see cref="BuildDenpyoAsync"/>（保存直前）はtrueで呼び、手動編集していないセルは
	/// 現行どおり保存直前に最新のScope設定で計算し直す（Scope編集を後から変えても反映される後方互換）。
	/// <see cref="RecalcMatrix"/>（「一括計算」ボタン）はfalseで呼び、全セルを明示的に上書きする。
	/// </summary>
	async Task RecalcCellsAsync(bool onlyIfNotManuallyEdited, CancellationToken ct) {
		// 先にセルの構成（数・順序）をScopeへ揃えておく（Scope追加直後にまだCellsが無い行があるため）
		SyncAllRowsCells();

		var effectiveScopes = ScopeRows.Where(s => s.PriceMethod == (int)EnumJodaiPriceMethod.RateOffFromEffective).ToList();
		Dictionary<(long Shohin, string Day), int> effectiveMap = [];
		if (effectiveScopes.Count > 0) {
			var keys = MeisaiRows.SelectMany(m => effectiveScopes.Select(s => (m.Id_Shohin, s.DayFrom)));
			effectiveMap = await ResolveEffectiveJodaiAsync(keys, ct);
		}

		foreach (var row in MeisaiRows) {
			foreach (var cell in row.Cells) {
				if (onlyIfNotManuallyEdited && cell.IsManuallyEdited) continue;
				var scope = ScopeRows.FirstOrDefault(s => s.No == cell.No_Scope);
				if (scope == null) continue; // SyncAllRowsCells直後なので通常は起きない
				var method = (EnumJodaiPriceMethod)scope.PriceMethod;
				var baseJodai = method == EnumJodaiPriceMethod.RateOffFromEffective
					? effectiveMap.GetValueOrDefault((row.Id_Shohin, scope.DayFrom), row.JodaiOld)
					: row.JodaiOld;
				cell.ApplyComputedValue(
					JodaiPriceRule.Calculate(method, baseJodai, scope.FixedPrice, scope.RateOff, scope.Amount, scope.RateOn,
						scope.RoundUnit, scope.RoundType, PricePointsFor(scope.Id_PricePoint)),
					baseJodai);
			}
		}
	}

	bool CanApplyBulkOperation() => selectedMatrixCells.Count > 0;

	/// <summary>
	/// セル一括操作（設計書5.4「30% OFF / 40% OFF / −1,000円 / 固定 7,900円 / 価格ポイント適用」）。
	/// 値をボタンへ焼き込まず、方式と値を選んで選択セルへ適用する形にしている。丸めは各セルが属する
	/// Scope自身の設定（<see cref="JodaiScopeRow.RoundUnit"/>/<see cref="JodaiScopeRow.RoundType"/>）に従う。
	/// <para>方式4（実効上代からの値下率）は対象外（<see cref="BulkPriceMethodOptions"/>参照）。</para>
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanApplyBulkOperation))]
	void ApplyBulkOperation() {
		var method = (EnumJodaiPriceMethod)BulkMethod;
		var rateOff = method == EnumJodaiPriceMethod.RateOff ? ParseDecimal(BulkValueText) : 0m;
		var amount = method == EnumJodaiPriceMethod.Amount ? ParseInt(BulkValueText) : 0;
		var fixedPrice = method == EnumJodaiPriceMethod.FixedPrice ? ParseInt(BulkValueText) : 0;
		var rateOn = method == EnumJodaiPriceMethod.RateOn ? ParseDecimal(BulkValueText) : 0m;
		var idPricePoint = BulkPricePoint?.Id ?? 0;

		var applied = 0;
		foreach (var (row, noScope) in selectedMatrixCells) {
			var scope = ScopeRows.FirstOrDefault(s => s.No == noScope);
			var cell = row.Cells.FirstOrDefault(c => c.No_Scope == noScope);
			if (scope == null || cell == null) continue;
			cell.JodaiBase = row.JodaiOld;
			cell.JodaiNew = JodaiPriceRule.Calculate(
				method, row.JodaiOld, fixedPrice, rateOff, amount, rateOn,
				scope.RoundUnit, scope.RoundType, PricePointsFor(idPricePoint));
			applied++;
		}
		Message = $"選択した {applied:N0} セルへ適用しました";
	}

	// ===== ④ 確認（プレビュー・競合・Timeline。設計書2.8・2.9・5.5）===============

	[ObservableProperty] public partial int PreviewStyleCount { get; set; }
	[ObservableProperty] public partial int PreviewSkuCount { get; set; }
	[ObservableProperty] public partial int PreviewShopCount { get; set; }
	[ObservableProperty] public partial string PreviewPeriodText { get; set; } = string.Empty;
	[ObservableProperty] public partial int PreviewAvgJodaiOld { get; set; }
	[ObservableProperty] public partial int PreviewAvgJodaiNew { get; set; }
	[ObservableProperty] public partial decimal PreviewAvgRateOffPercent { get; set; }
	[ObservableProperty] public partial long PreviewExpandRows { get; set; }
	[ObservableProperty] public partial bool PreviewExpandRowsWarning { get; set; }
	[ObservableProperty] public partial int PreviewConflictCount { get; set; }
	[ObservableProperty] public partial int PreviewBelowCostCount { get; set; }
	[ObservableProperty] public partial int PreviewBelowMinPriceCount { get; set; }

	/// <summary>C1/C2（エラー）が1件でもあれば true。<see cref="CanFix"/>が確定ボタンを無効化する（設計書5.5）。</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoFixCommand))]
	public partial bool HasBlockingConflicts { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<JodaiConflictRow> ConflictRows { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> TimelineShohinOptions { get; set; } = [];

	[ObservableProperty]
	public partial MasterOption? SelectedTimelineShohin { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> TimelineTenpoOptions { get; set; } = [];

	[ObservableProperty]
	public partial MasterOption? SelectedTimelineTenpo { get; set; }

	/// <summary>
	/// Timelineの区間データ（設計書5.5）。描画（横棒）はこのコレクションを見るだけにし、UatVmはこの
	/// プロパティを直接検証する（「見た目そのものはUatVmでは観測できない」ためのタスク指示）。
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<JodaiTimelineSegment> TimelineSegments { get; set; } = [];

	/// <summary>
	/// 競合チェック（設計書5.6）。プレビュー集計（2.9）と競合一覧（2.8）をまとめて算出する。
	/// <para>
	/// C1/C2/C5は<see cref="JodaiScopeResolver"/>（伝票内のみで判定できる純粋関数）、C7/C8は
	/// <see cref="JodaiPriceRule"/>（同じく純粋関数）をそのまま使う。C4/C6はDB参照が要るため、
	/// <see cref="CvBase.JodaiConflictSql"/>が組み立てるSQLをgRPCの<c>QueryListSqlParam</c>で実行する
	/// （設計書6.3。<c>CvDomainLogic.JodaiConflictChecker</c>と同じSQLを共有し、二重に書かない）。
	/// </para>
	/// </summary>
	/// <summary>プレビュー集計・競合一覧をクリアする（<see cref="ClearEdit"/>・競合チェック開始時）。</summary>
	void ClearPreviewAndConflicts() {
		PreviewStyleCount = 0;
		PreviewSkuCount = 0;
		PreviewShopCount = 0;
		PreviewPeriodText = string.Empty;
		PreviewAvgJodaiOld = 0;
		PreviewAvgJodaiNew = 0;
		PreviewAvgRateOffPercent = 0m;
		PreviewExpandRows = 0;
		PreviewExpandRowsWarning = false;
		PreviewConflictCount = 0;
		PreviewBelowCostCount = 0;
		PreviewBelowMinPriceCount = 0;
		ConflictRows = [];
		HasBlockingConflicts = false;
	}

	[RelayCommand]
	async Task CheckConflicts(CancellationToken ct) {
		ClearPreviewAndConflicts();
		if (EditDayFrom == null || EditDayTo == null) {
			Message = "適用期間を入力してから競合チェックしてください。";
			return;
		}
		if (MeisaiRows.Count == 0 || ScopeRows.Count == 0) {
			Message = "対象商品と適用範囲（Scope）を設定してから競合チェックしてください。";
			return;
		}
		var shops = ShopRows.Where(x => x.IsTarget).ToList();
		if (shops.Count == 0) {
			Message = "対象店舗をチェックしてから競合チェックしてください。";
			return;
		}

		try {
			StartBusy("競合チェック中...");

			var scopes = ScopeRows.Select(ToTranJodaiScope).ToList();
			var candidateStores = ResolveCandidateStores(shops.Select(s => s.Id_Tenpo).ToHashSet());
			var resolution = JodaiScopeResolver.Resolve(candidateStores, scopes);

			await RecalcCellsAsync(onlyIfNotManuallyEdited: true, ct);
			var jmeisai = BuildJmeisaiCells(scopes);
			var jshop = resolution.Jshop;

			// ---- プレビュー集計（設計書2.9）----
			PreviewStyleCount = jmeisai.Select(m => m.Id_Shohin).Distinct().Count();
			PreviewSkuCount = TargetSkuCount; // Step5で作ったSKU数の仕組みをそのまま再利用（タスク指示）
			PreviewShopCount = jshop.Select(s => s.Id_Tenpo).Distinct().Count();
			PreviewPeriodText = $"{scopes.Min(s => s.DayFrom)} ～ {scopes.Max(s => s.DayTo)}";
			PreviewAvgJodaiOld = jmeisai.Count > 0 ? (int)Math.Round(jmeisai.Average(m => (double)m.JodaiOld), MidpointRounding.AwayFromZero) : 0;
			PreviewAvgJodaiNew = jmeisai.Count > 0 ? (int)Math.Round(jmeisai.Average(m => (double)m.JodaiNew), MidpointRounding.AwayFromZero) : 0;
			var sumOld = jmeisai.Sum(m => (long)m.JodaiOld);
			var sumNew = jmeisai.Sum(m => (long)m.JodaiNew);
			// 分母(sumOld)が0（明細なし・原価データ未整備など）でも例外・ゼロ除算にしない（タスク指示）。
			PreviewAvgRateOffPercent = sumOld > 0 ? Math.Round((1m - (decimal)sumNew / sumOld) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
			// Scope毎に「該当Jshop件数 × 該当Jmeisai件数」の積の総和（設計書2.9「Jshop件数×Scope内商品件数の合計」）
			PreviewExpandRows = scopes.Sum(s => (long)jshop.Count(j => j.No_Scope == s.No) * jmeisai.Count(m => m.No_Scope == s.No));
			var warnRows = await GetConfigIntAsync(MasterConfig.NameJodaiExpandWarnRows, 200000, ct);
			PreviewExpandRowsWarning = warnRows > 0 && PreviewExpandRows > warnRows;

			// ---- 競合一覧（設計書2.8・5.5）----
			var rows = new List<JodaiConflictRow>();
			AddGroupedConflictRows(rows, resolution.Conflicts, EnumJodaiConflictKind.ScopeOverlapSameRange);
			AddGroupedConflictRows(rows, resolution.Conflicts, EnumJodaiConflictKind.ScopeDefinitionOverlap);
			AddGroupedConflictRows(rows, resolution.Conflicts, EnumJodaiConflictKind.PriorityResolvedAcrossRangeType);

			// C3: 伝票内・商品重複。Normalize()が自動解消するので件数だけ通知する（設計書2.8）。
			var duplicateCount = new TranJodai { Jmeisai = [.. jmeisai] }.FindDuplicates().Count;
			if (duplicateCount > 0) {
				rows.Add(new JodaiConflictRow(EnumJodaiConflictKind.DuplicateItem, EnumJodaiConflictSeverity.Info,
					duplicateCount, $"商品×Scopeの重複が{duplicateCount:N0}件あります。確定時に自動解消されます。"));
			}

			// C4/C6: DB参照が要るためgRPC QueryListSqlParamで取得する（設計書6.3。SQLはCvBase.JodaiConflictSqlを共有）。
			var shohinIds = jmeisai.Select(m => m.Id_Shohin).Distinct().ToList();
			var tenpoIds = jshop.Select(s => s.Id_Tenpo).Distinct().ToList();
			var (dayFrom, dayTo) = JodaiConflictSql.OverallPeriod(jshop, ToDay(EditDayFrom.Value), ToDay(EditDayTo.Value));

			var otherSlipRows = await FetchOtherSlipConflictsAsync(shohinIds, tenpoIds, properOnly: false, dayFrom, dayTo, ct);
			if (otherSlipRows.Count > 0) {
				var examples = otherSlipRows.Take(5).Select(c => $"{c.Code_Shohin} {c.Mei_Shohin}（店舗Id={c.Id_Tenpo}）{c.Jodai}円 [伝票Id={c.Id_Tran}]");
				rows.Add(new JodaiConflictRow(EnumJodaiConflictKind.OtherSlipConflict, EnumJodaiConflictSeverity.Warning,
					otherSlipRows.Count,
					$"他の伝票の確定済み適用上代（DerivedJodai）と、同一商品×同一店舗×期間で重複しています（{otherSlipRows.Count}件）。"
						+ $" 例: {string.Join("、", examples)}"));
			}

			var properRows = await FetchOtherSlipConflictsAsync(shohinIds, tenpoIds, properOnly: true, dayFrom, dayTo, ct);
			if (properRows.Count > 0) {
				var examples = properRows.Take(5).Select(c => $"{c.Code_Shohin} {c.Mei_Shohin}（店舗Id={c.Id_Tenpo}）恒久上代={c.Jodai}円 [伝票Id={c.Id_Tran}]");
				rows.Add(new JodaiConflictRow(EnumJodaiConflictKind.ProperBaselineMismatch, EnumJodaiConflictSeverity.Warning,
					properRows.Count,
					$"期間内に恒久上代変更（Kubun=Proper）の伝票が別途有効です（{properRows.Count}件）。"
						+ $" 例: {string.Join("、", examples)}"));
			}

			// C7/C8: 判定の中核はCvBase.JodaiPriceRule（Price Matrixのセル警告と同じ基準。CvDomainLogicのJodaiConflictCheckerも同じ関数を使う）。
			var minPrice = await GetConfigIntAsync(MasterConfig.NameJodaiMinPrice, 0, ct);
			var belowCost = jmeisai.Where(m => JodaiPriceRule.IsBelowCost(m.JodaiNew, m.TankaGenka)).ToList();
			PreviewBelowCostCount = belowCost.Count;
			if (belowCost.Count > 0) {
				var examples = belowCost.Take(5).Select(m => $"{m.Code_Shohin} {m.Mei_Shohin} 新{m.JodaiNew}円<原価{m.TankaGenka}円");
				rows.Add(new JodaiConflictRow(EnumJodaiConflictKind.BelowCost, EnumJodaiConflictSeverity.Warning,
					belowCost.Count, $"原価割れの明細が{belowCost.Count}件あります。 例: {string.Join("、", examples)}"));
			}

			var belowMin = minPrice > 0 ? jmeisai.Where(m => JodaiPriceRule.IsBelowMinPrice(m.JodaiNew, minPrice)).ToList() : [];
			PreviewBelowMinPriceCount = belowMin.Count;
			if (belowMin.Count > 0) {
				var examples = belowMin.Take(5).Select(m => $"{m.Code_Shohin} {m.Mei_Shohin} 新{m.JodaiNew}円<最低{minPrice}円");
				rows.Add(new JodaiConflictRow(EnumJodaiConflictKind.BelowMinPrice, EnumJodaiConflictSeverity.Warning,
					belowMin.Count, $"最低販売価格（{minPrice}円）を下回る明細が{belowMin.Count}件あります。 例: {string.Join("、", examples)}"));
			}

			ConflictRows = [.. rows.OrderBy(r => r.Severity).ThenBy(r => r.Kind)];
			PreviewConflictCount = rows.Where(r => r.Kind != EnumJodaiConflictKind.BelowCost && r.Kind != EnumJodaiConflictKind.BelowMinPrice)
				.Sum(r => r.Count);
			HasBlockingConflicts = rows.Any(r => r.Severity == EnumJodaiConflictSeverity.Error);

			Message = $"{DateTime.Now:MM/dd HH:mm:ss} 競合チェック: 展開見込 {PreviewExpandRows:N0} 行 / 競合(C1〜C6) {PreviewConflictCount:N0} 件"
				+ $" / 原価割れ {PreviewBelowCostCount:N0} 件 / 最低価格違反 {PreviewBelowMinPriceCount:N0} 件";
		}
		catch (OperationCanceledException) {
			Message = "競合チェックを中断しました";
		}
		catch (Exception ex) {
			Message = $"競合チェック失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary><paramref name="conflicts"/>のうち<paramref name="kind"/>の種別だけを1行へ集約する。</summary>
	static void AddGroupedConflictRows(List<JodaiConflictRow> rows, IReadOnlyList<JodaiConflict> conflicts, EnumJodaiConflictKind kind) {
		var items = conflicts.Where(c => c.Kind == kind).ToList();
		if (items.Count == 0) return;
		rows.Add(new JodaiConflictRow(kind, items[0].Severity, items.Count, string.Join("\n", items.Take(5).Select(c => c.Message))));
	}

	/// <summary>
	/// C4（<paramref name="properOnly"/>=false）／C6（true）の判定行をgRPC <c>QueryListSqlParam</c>で取得する。
	/// SQLは<see cref="JodaiConflictSql.BuildOtherSlipConflictSql"/>（<c>CvBase</c>）を<c>CvDomainLogic.JodaiConflictChecker</c>と
	/// 共有する（設計書6.3。二重に書かない）。
	/// </summary>
	async Task<List<JodaiConflictSql.OtherSlipRow>> FetchOtherSlipConflictsAsync(
		List<long> shohinIds, List<long> tenpoIds, bool properOnly, string dayFrom, string dayTo, CancellationToken ct) {
		if (shohinIds.Count == 0) return [];
		var sql = JodaiConflictSql.BuildOtherSlipConflictSql(shohinIds, tenpoIds, properOnly);
		// JodaiConflictSql.BuildOtherSlipConflictSqlの契約どおり @0=自伝票Id @1=TaishoType @2=DayTo @3=DayFrom の順。
		List<string> parameters = [
			EditId.ToString(CultureInfo.InvariantCulture),
			EditTaishoType.ToString(CultureInfo.InvariantCulture),
			dayTo,
			dayFrom,
		];
		return await QuerySqlListAsync<JodaiConflictSql.OtherSlipRow>(sql, parameters, ct);
	}

	/// <summary>
	/// Timeline（設計書5.5）を作る。選択商品×選択店舗の実効価格推移を、Scope由来の区間 + 通常上代へ戻る区間 +
	/// 他伝票の確定済み<see cref="DerivedJodai"/>の重ね合わせで表現する。表示範囲はScopeの最小開始日−30日〜
	/// 最大終了日+30日（設計書5.5）。
	/// </summary>
	[RelayCommand]
	async Task BuildTimeline(CancellationToken ct) {
		TimelineSegments = [];
		if (SelectedTimelineShohin == null || SelectedTimelineTenpo == null) {
			Message = "Timeline表示には商品と店舗を選んでください。";
			return;
		}
		if (ScopeRows.Count == 0) {
			Message = "先に適用範囲（Scope）を設定してください。";
			return;
		}
		var meisaiRow = MeisaiRows.FirstOrDefault(m => m.Id_Shohin == SelectedTimelineShohin.Id);
		if (meisaiRow == null) {
			Message = "選択した商品が対象明細に見つかりません。";
			return;
		}

		try {
			StartBusy("Timeline作成中...");

			var scopes = ScopeRows.Select(ToTranJodaiScope).ToList();
			var store = ResolveCandidateStores([SelectedTimelineTenpo.Id]).FirstOrDefault(s => s.Id == SelectedTimelineTenpo.Id);
			var resolution = JodaiScopeResolver.Resolve(store == null ? [] : [store], scopes);
			var ownScopeNos = resolution.Jshop.Where(j => j.Id_Tenpo == SelectedTimelineTenpo.Id).Select(j => j.No_Scope).ToHashSet();

			// 選択店舗で実際に採用されたScopeの区間だけを、開始日順に並べる（設計書2.5の優先順位解決を経た結果）。
			var ownSegments = scopes.Where(s => ownScopeNos.Contains(s.No))
				.Select(s => (s.DayFrom, s.DayTo, Jodai: meisaiRow.Cells.FirstOrDefault(c => c.No_Scope == s.No)?.JodaiNew ?? meisaiRow.JodaiOld, s.Name))
				.OrderBy(s => s.DayFrom, StringComparer.Ordinal)
				.ToList();

			var minFrom = ownSegments.Count > 0 ? ownSegments.Min(s => s.DayFrom) : ToDay(EditDayFrom ?? DateTime.Today);
			var maxTo = ownSegments.Count > 0 ? ownSegments.Max(s => s.DayTo) : ToDay(EditDayTo ?? DateTime.Today);
			var rangeFrom = ToDay((ParseDay(minFrom) ?? DateTime.Today).AddDays(-30));
			var rangeTo = ToDay((ParseDay(maxTo) ?? DateTime.Today).AddDays(30));

			var segments = new List<JodaiTimelineSegment>();
			var cursor = rangeFrom;
			foreach (var seg in ownSegments) {
				if (string.CompareOrdinal(cursor, seg.DayFrom) < 0) {
					// ギャップ=Scope期間外。通常上代へ戻る区間として明示する（設計書5.5）。
					var gapTo = ToDay((ParseDay(seg.DayFrom) ?? DateTime.Today).AddDays(-1));
					segments.Add(new JodaiTimelineSegment(cursor, gapTo, meisaiRow.JodaiOld, "通常上代", true, false, 0));
				}
				segments.Add(new JodaiTimelineSegment(seg.DayFrom, seg.DayTo, seg.Jodai, seg.Name, false, false, 0));
				cursor = ToDay((ParseDay(seg.DayTo) ?? DateTime.Today).AddDays(1));
			}
			if (string.CompareOrdinal(cursor, rangeTo) <= 0) {
				segments.Add(new JodaiTimelineSegment(cursor, rangeTo, meisaiRow.JodaiOld, "通常上代", true, false, 0));
			}

			// 他伝票の確定済みDerivedJodaiを重ねて表示する（設計書5.5）。全店(Id_Tenpo=0)指定も拾う。
			List<string> parameters = [];
			var shohinP = AddParameter(parameters, SelectedTimelineShohin.Id);
			var tenpoP = AddParameter(parameters, SelectedTimelineTenpo.Id);
			var taishoP = AddParameter(parameters, EditTaishoType);
			var toP = AddParameter(parameters, rangeTo);
			var fromP = AddParameter(parameters, rangeFrom);
			var sql = $@"
SELECT Id_Tenpo, DayFrom, DayTo, Jodai, Id_Tran
FROM {nameof(DerivedJodai)}
WHERE Id_Shohin = {shohinP} AND TaishoType = {taishoP}
  AND (Id_Tenpo = {tenpoP} OR Id_Tenpo = 0)
  AND DayFrom <= {toP} AND DayTo >= {fromP}
ORDER BY DayFrom";
			var others = await QuerySqlListAsync<JodaiTimelineOtherSlipRow>(sql, parameters, ct);
			foreach (var o in others) {
				segments.Add(new JodaiTimelineSegment(o.DayFrom, o.DayTo, o.Jodai, $"他伝票(Id={o.Id_Tran})", false, true, o.Id_Tran));
			}

			TimelineSegments = [.. segments.OrderBy(s => s.DayFrom, StringComparer.Ordinal)];
			Message = $"Timeline: {SelectedTimelineShohin.Code} / {SelectedTimelineTenpo.Code} の区間 {TimelineSegments.Count:N0} 件"
				+ $"（{rangeFrom}～{rangeTo}、他伝票 {others.Count:N0} 件）";
		}
		catch (OperationCanceledException) {
			Message = "Timeline作成を中断しました";
		}
		catch (Exception ex) {
			Message = $"Timeline作成失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>
	/// Timeline用の選択肢（商品・店舗）を最新の<see cref="MeisaiRows"/>/<see cref="ShopRows"/>から作り直す。
	/// 抽出・対象取得のたびに呼ぶ（設計書5.5「商品1件と店舗1件を選ぶ」の候補を最新に保つ）。
	/// </summary>
	void RefreshTimelineOptions() {
		var prevShohin = SelectedTimelineShohin?.Id;
		var prevTenpo = SelectedTimelineTenpo?.Id;
		TimelineShohinOptions = new ObservableCollection<MasterOption>(
			MeisaiRows.Select(m => new MasterOption(m.Id_Shohin, m.Code_Shohin, m.Mei_Shohin)));
		TimelineTenpoOptions = new ObservableCollection<MasterOption>(
			ShopRows.Where(s => s.IsTarget).Select(s => new MasterOption(s.Id_Tenpo, s.Code_Tenpo, s.Mei_Tenpo)));
		SelectedTimelineShohin = TimelineShohinOptions.FirstOrDefault(o => o.Id == prevShohin) ?? TimelineShohinOptions.FirstOrDefault();
		SelectedTimelineTenpo = TimelineTenpoOptions.FirstOrDefault(o => o.Id == prevTenpo) ?? TimelineTenpoOptions.FirstOrDefault();
	}

	// ===== 登録 ===================================================================

	[RelayCommand]
	async Task DoRegister(CancellationToken ct) {
		var den = await BuildDenpyoAsync(ct);
		if (den == null) {
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

	// C1/C2（エラー）が競合チェックで検出されていれば確定を禁止する（設計書5.5）。競合チェックを一度も
	// 実行していない場合はHasBlockingConflicts=falseのままなので、現行どおり確定できる（後方互換）。
	bool CanFix() => EditId > 0 && EditStatus == 0 && !HasBlockingConflicts;

	/// <summary>
	/// 確定する。Status=1 にして保存すると、サーバ側の DerivedDb が
	/// <see cref="DerivedJodai"/> へ展開する（この画面から展開処理は呼ばない）。
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanFix))]
	async Task DoFix(CancellationToken ct) {
		var den = await BuildDenpyoAsync(ct);
		if (den == null) {
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
		var den = await BuildDenpyoAsync(ct);
		if (den == null) {
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
		var den = await BuildDenpyoAsync(ct);
		if (den == null) {
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

	/// <summary>
	/// 画面の入力から伝票を組み立てる。検証に失敗したら null と理由を返す。
	/// <para>
	/// Scope対応（設計3.4・3.11・2.5・2.8）: <see cref="Jmeisai"/>は「商品×Scope」のセルへ複製し、
	/// 各セルの<c>JodaiNew</c>は<see cref="JodaiPriceRule.Calculate"/>で算出する。<see cref="Jshop"/>は
	/// <see cref="JodaiScopeResolver.Resolve"/>で店舗ごとの採用Scopeを決めてから作る。
	/// C1/C2（エラー競合）があれば保存を中止する。商品数×Scope数が<see cref="MasterConfig.NameJodaiMaxCells"/>を
	/// 超える場合も保存前に警告して中止する。
	/// </para>
	/// </summary>
	/// <summary>
	/// <see cref="MeisaiRows"/>（商品×セル）を<paramref name="scopes"/>を使って<see cref="TranJodaiMeisai"/>の
	/// 明細一覧へ複製する（設計3.4・5.6）。<see cref="BuildDenpyoAsync"/>（保存直前）と
	/// ④確認タブのプレビュー・競合チェック（設計2.8・2.9）の両方が同じ組み立てを使うための共通ヘルパ
	/// （呼び出し前に<see cref="RecalcCellsAsync"/>で未編集セルを最新のScope設定へ揃えておくこと）。
	/// </summary>
	List<TranJodaiMeisai> BuildJmeisaiCells(List<TranJodaiScope> scopes) {
		var jmeisai = new List<TranJodaiMeisai>();
		foreach (var row in MeisaiRows) {
			foreach (var cell in row.Cells) {
				var scope = scopes.FirstOrDefault(s => s.No == cell.No_Scope);
				if (scope == null) continue; // 削除されたScopeのセル残骸（通常は起きない）
				jmeisai.Add(new TranJodaiMeisai {
					No = 0, // Normalize()がScope内連番へ振り直す
					Id_Shohin = row.Id_Shohin,
					Code_Shohin = row.Code_Shohin,
					Mei_Shohin = row.Mei_Shohin,
					JodaiOld = row.JodaiOld,
					JodaiNew = cell.JodaiNew,
					RateOff = row.JodaiOld > 0
						? Math.Round((1m - (decimal)cell.JodaiNew / row.JodaiOld) * 100m, 2, MidpointRounding.AwayFromZero)
						: 0m,
					PriceInTax = CalcPriceInTax(cell.JodaiNew),
					DayTento = row.DayTento,
					DayChange = ToDay(DateTime.Today),
					Status = row.Status,
					No_Scope = scope.No,
					JodaiBase = cell.JodaiBase,
					TankaGenka = row.TankaGenka,
				});
			}
		}
		return jmeisai;
	}

	async Task<TranJodai?> BuildDenpyoAsync(CancellationToken ct) {
		if (EditDayFrom == null || EditDayTo == null) {
			ShowBuildError("適用期間を入力してください。");
			return null;
		}
		if (ToDay(EditDayFrom.Value).CompareTo(ToDay(EditDayTo.Value)) > 0) {
			ShowBuildError("適用期間の開始日が終了日より後になっています。");
			return null;
		}
		var shops = ShopRows.Where(x => x.IsTarget).ToList();
		if (shops.Count == 0) {
			ShowBuildError("対象を1件以上チェックしてください。");
			return null;
		}
		if (MeisaiRows.Count == 0) {
			ShowBuildError("[明細取得] で対象商品を表示してください。");
			return null;
		}
		if (ScopeRows.Count == 0) {
			ShowBuildError("適用範囲（Scope）を1件以上登録してください。");
			return null;
		}
		var badScopePeriod = ScopeRows.FirstOrDefault(s => string.Compare(s.DayFrom, s.DayTo, StringComparison.Ordinal) > 0);
		if (badScopePeriod != null) {
			ShowBuildError($"Scope#{badScopePeriod.No}「{badScopePeriod.Name}」の期間が逆転しています（{badScopePeriod.DayFrom}～{badScopePeriod.DayTo}）。");
			return null;
		}
		var badPeriod = shops.FirstOrDefault(s => string.Compare(s.DayFrom, s.DayTo, StringComparison.Ordinal) > 0);
		if (badPeriod != null) {
			ShowBuildError($"対象 {badPeriod.Code_Tenpo} {badPeriod.Mei_Tenpo} の期間が逆転しています（{badPeriod.DayFrom}～{badPeriod.DayTo}）。");
			return null;
		}

		var scopes = ScopeRows.Select(ToTranJodaiScope).ToList();

		// 商品数×Scope数の上限（設計3.11）。保存直前はDBの最新値で最終判定する
		if (!await CheckMaxCellsAsync(MeisaiRows.Count, scopes.Count, ct)) {
			ShowBuildError("商品数×Scope数の上限を超えるため保存を中止しました。");
			return null;
		}

		// 店舗をScopeへ解決する（設計2.5・2.6）。マスタから消えた店舗は価格グループ3軸未設定とみなす
		var candidateStores = ResolveCandidateStores(shops.Select(s => s.Id_Tenpo).ToHashSet());
		var resolution = JodaiScopeResolver.Resolve(candidateStores, scopes);
		var errorConflicts = resolution.Conflicts.Where(c => c.Severity == EnumJodaiConflictSeverity.Error).ToList();
		if (errorConflicts.Count > 0) {
			var head = string.Join("\n", errorConflicts.Take(5).Select(c => c.Message));
			var more = errorConflicts.Count > 5 ? $"\n… 他 {errorConflicts.Count - 5} 件" : string.Empty;
			MessageEx.ShowErrorDialog($"Scopeの競合があるため確定できません。\n{head}{more}", owner: ActiveWindow);
			ShowBuildError("Scopeの競合（C1/C2）があるため保存を中止しました。「解決結果を確認」で内容を見直してください。", showDialog: false);
			return null;
		}

		// 店舗ごとの期間微調整（設計3.3・U5）: 直前に読み込んだJshopに同じ(店舗,Scope)があれば、その期間を残す
		var previousByKey = loadedJshop.ToDictionary(x => (x.Id_Tenpo, x.No_Scope));
		var jshop = resolution.Jshop.Select(r => previousByKey.TryGetValue((r.Id_Tenpo, r.No_Scope), out var prev)
			? new TranJodaiShop { Id_Tenpo = r.Id_Tenpo, Code_Tenpo = r.Code_Tenpo, Mei_Tenpo = r.Mei_Tenpo, DayFrom = prev.DayFrom, DayTo = prev.DayTo, No_Scope = r.No_Scope }
			: r).ToList();

		// Jmeisaiを「商品×Scope」のセルへ複製する（設計3.4・5.6）。
		// Price Matrix（③価格タブ）で手動編集・セル一括操作したセル（IsManuallyEdited）はその値をそのまま使う
		// （設計書「主入力手段ではなく確認・例外編集用」。保存の瞬間に例外価格を上書きしないため）。
		// 手動編集していないセルは、保存直前にScopeの現在値で計算し直す（Scope編集を後から変えても保存時に
		// 反映される、Step6以前と同じ後方互換の挙動を保つため。RecalcCellsAsyncのonlyIfNotManuallyEdited=true）。
		await RecalcCellsAsync(onlyIfNotManuallyEdited: true, ct);
		var jmeisai = BuildJmeisaiCells(scopes);

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
			Jcond = [.. CondRows.Where(c => c.Field != null && c.Field.IsSelectable).Select((c, i) => new TranJodaiCond {
				No = i + 1,
				Field = c.Field!.Name,
				CdFrom = c.CdFrom,
				CdTo = c.CdTo,
				ZaikoJoken = ZaikoJoken,
				TenkaiTani = 0,
				Ope = c.Ope,
			})],
			Jshop = jshop,
			Jmeisai = jmeisai,
			Jscope = scopes,
		};

		// 重複したまま確定すると DerivedJodai のユニークキー違反で保存自体が失敗するので、必ず取り除く
		var duplicates = den.FindDuplicates();
		if (duplicates.Count > 0) {
			var head = string.Join("\n", duplicates.Take(5));
			var more = duplicates.Count > 5 ? $"\n… 他 {duplicates.Count - 5} 件" : string.Empty;
			if (MessageEx.ShowQuestionDialog(
					$"重複があります。後に指定した内容を残して取り除きます。続行しますか？\n{head}{more}",
					owner: ActiveWindow) != MessageBoxResult.Yes) {
				ShowBuildError("重複があるため登録を中止しました。", showDialog: false);
				return null;
			}
		}
		den.Normalize();
		return den;
	}

	/// <summary>
	/// <see cref="BuildDenpyoAsync"/>の検証失敗を利用者へ知らせる。<paramref name="showDialog"/>=falseは
	/// 呼び出し元が既に別のダイアログ（重複確認・競合エラー）を出している場合に、二重表示を避けるために使う。
	/// </summary>
	void ShowBuildError(string message, bool showDialog = true) {
		Message = message;
		if (showDialog) MessageEx.ShowWarningDialog(message, owner: ActiveWindow);
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

	// ===== ② 適用範囲（Scope）====================================================

	static JodaiScopeRow ToScopeRow(TranJodaiScope s) => new() {
		No = s.No,
		Name = s.Name,
		RangeType = s.RangeType,
		IncExc = s.IncExc,
		GroupAxis = s.GroupAxis,
		Id_Group = s.Id_Group,
		Code_Group = s.Code_Group,
		Mei_Group = s.Mei_Group,
		Id_Tenpo = s.Id_Tenpo,
		Code_Tenpo = s.Code_Tenpo,
		Mei_Tenpo = s.Mei_Tenpo,
		DayFrom = s.DayFrom,
		DayTo = s.DayTo,
		PriceMethod = s.PriceMethod,
		FixedPrice = s.FixedPrice,
		RateOff = s.RateOff,
		Amount = s.Amount,
		RateOn = s.RateOn,
		RoundUnit = s.RoundUnit,
		RoundType = s.RoundType,
		Id_PricePoint = s.Id_PricePoint,
		Odr = s.Odr,
	};

	static TranJodaiScope ToTranJodaiScope(JodaiScopeRow r) => new() {
		No = r.No,
		Name = r.Name,
		RangeType = r.RangeType,
		IncExc = r.IncExc,
		GroupAxis = r.GroupAxis,
		Id_Group = r.Id_Group,
		Code_Group = r.Code_Group,
		Mei_Group = r.Mei_Group,
		Id_Tenpo = r.Id_Tenpo,
		Code_Tenpo = r.Code_Tenpo,
		Mei_Tenpo = r.Mei_Tenpo,
		DayFrom = r.DayFrom,
		DayTo = r.DayTo,
		PriceMethod = r.PriceMethod,
		FixedPrice = r.FixedPrice,
		RateOff = r.RateOff,
		Amount = r.Amount,
		RateOn = r.RateOn,
		RoundUnit = r.RoundUnit,
		RoundType = r.RoundType,
		Id_PricePoint = r.Id_PricePoint,
		Odr = r.Odr,
	};

	int NextScopeNo() => ScopeRows.Count == 0 ? 1 : ScopeRows.Max(r => r.No) + 1;

	void RenumberScopeRows() {
		var no = 0;
		foreach (var row in ScopeRows) row.No = ++no;
	}

	/// <summary>Scope行を1件追加する（既定値は全店/対象/ヘッダ既定期間・価格ルール）。</summary>
	[RelayCommand]
	void AddScopeRow() {
		if (!CheckMaxCellsCached(MeisaiRows.Count, ScopeRows.Count + 1)) return;
		var scope = DefaultHeaderScope();
		scope.No = NextScopeNo();
		scope.Name = $"Scope{scope.No}";
		ScopeRows.Add(ToScopeRow(scope));
	}

	/// <summary>Scope行を削除する。最後の1件は残す（Price Matrixの列が0にならないように）。</summary>
	[RelayCommand]
	void RemoveScopeRow(JodaiScopeRow? row) {
		if (row == null || ScopeRows.Count <= 1) return;
		ScopeRows.Remove(row);
		RenumberScopeRows();
		if (SelectedScopeRow == row) SelectedScopeRow = null;
	}

	/// <summary>
	/// 「段を追加」：選択中のScopeの範囲・価格ルールを引き継ぎ、期間だけ後ろにずらした行を複製する
	/// （段階値下げの入力を1操作にする。設計書5.3）。ずらし方は「元のDayToの翌日から、元と同じ日数」。
	/// </summary>
	[RelayCommand]
	void AddScopeStage() {
		var src = SelectedScopeRow ?? ScopeRows.LastOrDefault();
		if (src == null) {
			MessageEx.ShowWarningDialog("複製元のScopeがありません。先にScopeを1件追加してください。", owner: ActiveWindow);
			return;
		}
		if (ParseDay(src.DayFrom) is not { } from || ParseDay(src.DayTo) is not { } to || from > to) {
			MessageEx.ShowWarningDialog("複製元Scopeの開始日・終了日が不正です。", owner: ActiveWindow);
			return;
		}
		if (!CheckMaxCellsCached(MeisaiRows.Count, ScopeRows.Count + 1)) return;

		var days = (to - from).Days + 1;
		var newFrom = to.AddDays(1);
		var newTo = newFrom.AddDays(days - 1);
		var clone = ToTranJodaiScope(src);
		clone.No = NextScopeNo();
		clone.DayFrom = ToDay(newFrom);
		clone.DayTo = ToDay(newTo);
		var row = ToScopeRow(clone);
		ScopeRows.Add(row);
		SelectedScopeRow = row;
		Message = $"Scope#{src.No}を複製し、期間を{row.DayFrom}～{row.DayTo}にずらしました";
	}

	/// <summary>
	/// 「解決結果を確認」：現在のScope一覧を実店舗へ解決し（<see cref="JodaiScopeResolver.Resolve"/>）、
	/// 店舗→採用Scopeの対応と検出した競合（C1・C2・C5）を提示する（設計書2.5・5.3）。
	/// </summary>
	[RelayCommand]
	async Task ResolveScope(CancellationToken ct) {
		try {
			StartBusy("解決結果を確認中...");
			if (shopMasters.Count == 0) {
				await LoadShopRowsAsync(loadedJshop, ct);
			}
			var checkedIds = ShopRows.Where(x => x.IsTarget).Select(x => x.Id_Tenpo).ToHashSet();
			var stores = ResolveCandidateStores(checkedIds);
			var scopes = ScopeRows.Select(ToTranJodaiScope).ToList();
			var resolution = JodaiScopeResolver.Resolve(stores, scopes);
			ShowResolutionDialog(resolution);
		}
		catch (OperationCanceledException) {
			Message = "解決確認を中断しました";
		}
		catch (Exception ex) {
			Message = $"解決確認失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	/// <summary>
	/// <paramref name="checkedIds"/>の対象店舗を<see cref="shopMasters"/>から解決用の<see cref="MasterTokui"/>へ変換する。
	/// マスタから消えた店舗（<see cref="shopMasters"/>に無い）は価格グループ3軸を未設定(0)とみなす合成行を作る
	/// （グループScopeには該当しないが、個別店舗Scope・全店Scopeの判定には支障が無い）。
	/// </summary>
	List<MasterTokui> ResolveCandidateStores(IReadOnlyCollection<long> checkedIds) {
		var byId = shopMasters.ToDictionary(m => m.Id);
		var result = new List<MasterTokui>();
		foreach (var shop in ShopRows.Where(x => checkedIds.Contains(x.Id_Tenpo))) {
			result.Add(byId.TryGetValue(shop.Id_Tenpo, out var master)
				? master
				: new MasterTokui { Id = shop.Id_Tenpo, Code = shop.Code_Tenpo, Name = shop.Mei_Tenpo });
		}
		return result;
	}

	void ShowResolutionDialog(JodaiScopeResolution resolution) {
		var lines = new List<string>();
		var byScope = resolution.Jshop.GroupBy(x => x.No_Scope).OrderBy(g => g.Key);
		lines.Add("【店舗 → 採用Scope】");
		foreach (var group in byScope) {
			var scopeName = ScopeRows.FirstOrDefault(s => s.No == group.Key)?.Name ?? $"Scope{group.Key}";
			lines.Add($"Scope#{group.Key}「{scopeName}」: {group.Count():N0} 店舗");
		}
		var storesWithMultiple = resolution.Jshop.GroupBy(x => x.Id_Tenpo).Where(g => g.Count() > 1).ToList();
		if (storesWithMultiple.Count > 0) {
			lines.Add(string.Empty);
			lines.Add("【複数Scopeに該当した店舗（段階値下げ等で期間が重ならないため正常）】");
			foreach (var g in storesWithMultiple.Take(20)) {
				lines.Add($"{g.First().Code_Tenpo} {g.First().Mei_Tenpo}: Scope#{string.Join(",", g.Select(x => x.No_Scope))}");
			}
		}
		foreach (var severity in new[] { EnumJodaiConflictSeverity.Error, EnumJodaiConflictSeverity.Warning, EnumJodaiConflictSeverity.Info }) {
			var items = resolution.Conflicts.Where(c => c.Severity == severity).ToList();
			if (items.Count == 0) continue;
			lines.Add(string.Empty);
			lines.Add($"【{SeverityLabel(severity)}　{items.Count:N0}件】");
			foreach (var c in items.Take(20)) lines.Add(c.Message);
		}
		var text = string.Join("\n", lines);
		if (resolution.Conflicts.Any(c => c.Severity == EnumJodaiConflictSeverity.Error)) {
			MessageEx.ShowErrorDialog(text, owner: ActiveWindow);
		}
		else if (resolution.Conflicts.Count > 0) {
			MessageEx.ShowWarningDialog(text, owner: ActiveWindow);
		}
		else {
			MessageEx.ShowInformationDialog(text, owner: ActiveWindow);
		}
		Message = $"解決結果: 店舗×Scope {resolution.Jshop.Count:N0} 行、競合 {resolution.Conflicts.Count:N0} 件";
	}

	static string SeverityLabel(EnumJodaiConflictSeverity severity) => severity switch {
		EnumJodaiConflictSeverity.Error => "エラー",
		EnumJodaiConflictSeverity.Warning => "警告",
		_ => "情報",
	};

	/// <summary>
	/// 商品数×Scope数がキャッシュ済みの<see cref="cachedJodaiMaxCells"/>を超えるかを判定する（設計3.11）。
	/// 画面操作（抽出・Scope追加）時の即時チェック用。保存直前の最終判定は<see cref="CheckMaxCellsAsync"/>を使う。
	/// </summary>
	bool CheckMaxCellsCached(int styleCount, int scopeCount) {
		var cells = (long)styleCount * Math.Max(scopeCount, 1);
		if (cells <= cachedJodaiMaxCells) return true;
		MessageEx.ShowWarningDialog(
			$"商品数×Scope数（{styleCount:N0}×{scopeCount:N0}={cells:N0}）が上限（{cachedJodaiMaxCells:N0}）を超えるため中止しました。伝票を分けてください。",
			owner: ActiveWindow);
		return false;
	}

	/// <summary>保存直前の最終判定。DBの<see cref="MasterConfig.NameJodaiMaxCells"/>を読み直す。</summary>
	async Task<bool> CheckMaxCellsAsync(int styleCount, int scopeCount, CancellationToken ct) {
		cachedJodaiMaxCells = await GetConfigIntAsync(MasterConfig.NameJodaiMaxCells, 30000, ct);
		return CheckMaxCellsCached(styleCount, scopeCount);
	}

	/// <summary>
	/// <see cref="MasterConfig"/>の設定値(整数)を読む。未設定・不正値・行が無い場合は<paramref name="fallback"/>を返す
	/// （<see cref="JodaiDb.GetKeepDays"/>と同じ方針。設計3.11・3.8）。
	/// </summary>
	async Task<int> GetConfigIntAsync(string name, int fallback, CancellationToken ct) {
		List<string> parameters = [];
		var sql = $"SELECT Id, Vdc, Vdu, Name, Val FROM {nameof(MasterConfig)} WHERE Name = {AddParameter(parameters, name)}";
		var list = await QuerySqlListAsync<MasterConfig>(sql, parameters, ct);
		var val = list.FirstOrDefault()?.Val;
		return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;
	}

	/// <summary>
	/// 価格方式4（実効上代からの値下率）で必要になる、発効日時点の実効上代を解決する。
	/// <see cref="DerivedJodai.FinalJodaiSql"/>を商品×日付ぶんUNION ALLした1本のSQLで一括取得する
	/// （行ごとに個別クエリを投げるとラウンドトリップが商品数×該当Scope数だけ発生するため）。
	/// <para>
	/// 対象(Id_Tenpo)は<see cref="EditTaishoType"/>の全件(0)で解決する。価格グループ・個別店舗Scopeでは
	/// 店舗ごとに実効上代が異なり得るが、Scopeの価格ルールは「商品×Scope」の1セルにつき1つの値しか
	/// 持てないため、全件(0)基準への近似とする（設計2.7の「伝票作成時点で解決した実効上代」を、
	/// 店舗非依存の近似値で満たす）。
	/// </para>
	/// </summary>
	async Task<Dictionary<(long Shohin, string Day), int>> ResolveEffectiveJodaiAsync(
		IEnumerable<(long Shohin, string Day)> keys, CancellationToken ct) {
		var keyList = keys.Distinct().ToList();
		if (keyList.Count == 0) return [];

		List<string> parameters = [];
		var unions = keyList.Select(k => {
			var shohinParam = AddParameter(parameters, k.Shohin);
			var tenpoParam = AddParameter(parameters, 0);
			var dayParam = AddParameter(parameters, k.Day);
			var taishoParam = AddParameter(parameters, EditTaishoType);
			var eff = DerivedJodai.FinalJodaiSql(shohinParam, taishoParam, tenpoParam, dayParam, "sh");
			return $"SELECT {shohinParam} AS Id_Shohin, {tenpoParam} AS Id_Tenpo, {dayParam} AS Day, {eff} AS Eff FROM MasterShohin sh WHERE sh.Id = {shohinParam}";
		});
		var sql = string.Join("\nUNION ALL\n", unions);
		var list = await QuerySqlListAsync<JodaiEffectiveRow>(sql, parameters, ct);
		return list.ToDictionary(x => (x.Id_Shohin, x.Day), x => x.Eff);
	}

	// ===== 選択ダイアログ =========================================================

	[RelayCommand]
	void SelectSaleDialog() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterMeisho>(this, typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunSale}'", "Code",
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

	/// <summary>
	/// 商品分類(<see cref="MasterShohin.Jsub"/>)の枠を抽出条件の検索項目として追加する。
	/// <para>
	/// <c>Jsub</c> の区分キー(<c>Kb</c>)は <c>B01</c>〜<c>B10</c> の利用者自由枠であり
	/// （<see cref="MasterMeisho.KubunTopShohin"/>='B'）、「大分類=B01」のような固定割当は無い。
	/// そのため <see cref="MasterMeisho"/> の <c>Kubun='IDX'</c> かつ <c>Code IN ('B01'..'B10')</c> の行、
	/// すなわち実際に登録されている枠だけを、その名称で検索項目に並べる。
	/// <see cref="MasterTokuiMenteViewModel.DoGetKubun"/>（得意先の C01〜C10 で同じことをしている）に倣う。
	/// </para>
	/// </summary>
	async Task LoadJsubFieldOptionsAsync(CancellationToken ct) {
		List<string> parameters = [];
		var codes = Enumerable.Range(1, 10).Select(i => $"{MasterMeisho.KubunTopShohin}{i:D2}");
		var placeholders = codes.Select(c => AddParameter(parameters, c)).ToList();
		var kubunParam = AddParameter(parameters, MasterMeisho.KubunIndex);
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM MasterMeisho
WHERE Kubun = {kubunParam} AND Code IN ({string.Join(",", placeholders)})
ORDER BY Code";
		var list = await QuerySqlListAsync<MasterMeisho>(sql, parameters, ct);
		var jsubOptions = list.Select(x => new FieldOption(
			string.IsNullOrEmpty(x.Name) ? (x.Code ?? string.Empty) : x.Name,
			string.Empty,
			IsNumeric: false,
			JsubKb: x.Code));
		FieldOptions = new ObservableCollection<FieldOption>([.. BaseFieldOptions, .. jsubOptions]);
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

	/// <summary>この行と直前までの式との結合。0:AND 1:OR。1行目は繋ぐ相手が無いため無視される。</summary>
	[ObservableProperty]
	public partial int Ope { get; set; }
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

	/// <summary>原価割れ判定用の時点値（<see cref="MasterShohin.TankaGenka"/>のSnapshot。設計3.4）。</summary>
	[ObservableProperty]
	public partial int TankaGenka { get; set; }

	/// <summary>
	/// 最低販売価格（<see cref="MasterConfig.NameJodaiMinPrice"/>）。<see cref="SyncCells"/>のたびに
	/// ViewModelのキャッシュ値で更新する（設計2.8のC8。0なら判定しない）。
	/// </summary>
	[ObservableProperty]
	public partial int MinSellingPrice { get; set; }

	/// <summary>
	/// Price Matrix（③価格タブ、設計書5.4）の1行ぶんのセル。<see cref="JodaiScopeRow"/>と同じ並び順を保つ
	/// （画面側は列インデックスで<c>Cells[i]</c>を束縛するため。<see cref="DailyShopBudgetQueryViewModel"/>や
	/// <see cref="HachuHaibunInputView.xaml.cs"/>の動的列と同じ流儀）。
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<JodaiPriceCell> Cells { get; set; } = [];

	/// <summary>
	/// <paramref name="scopes"/>と同じ並び順・同じ個数へ<see cref="Cells"/>を揃える。既存セルは
	/// <see cref="JodaiPriceCell.No_Scope"/>が一致すれば値をそのまま温存し（手動編集・一括操作の結果を失わないため）、
	/// 新しいScopeのぶんだけ<paramref name="defaultValue"/>で作る。無くなったScopeのセルは捨てる。
	/// </summary>
	public void SyncCells(IEnumerable<JodaiScopeRow> scopes, int minSellingPrice, Func<JodaiScopeRow, int> defaultValue) {
		MinSellingPrice = minSellingPrice;
		var existing = Cells.ToDictionary(c => c.No_Scope);
		var rebuilt = new ObservableCollection<JodaiPriceCell>();
		foreach (var scope in scopes) {
			if (existing.TryGetValue(scope.No, out var cell)) {
				rebuilt.Add(cell);
			}
			else {
				var newCell = new JodaiPriceCell(this) { No_Scope = scope.No };
				newCell.ApplyComputedValue(defaultValue(scope), JodaiOld);
				rebuilt.Add(newCell);
			}
		}
		Cells = rebuilt;
		foreach (var cell in Cells) RefreshCellViolation(cell);
	}

	/// <summary>
	/// セルの原価割れ・最低販売価格違反フラグ（設計2.8のC7/C8）を、判定の中核である
	/// <see cref="JodaiPriceRule.IsBelowCost"/>/<see cref="JodaiPriceRule.IsBelowMinPrice"/>で更新する。
	/// <c>JodaiConflictChecker.CheckBelowCost</c>/<c>CheckBelowMinPrice</c>（CvDomainLogic）と同じ基準
	/// （<c>CvWpfclient</c>は<c>CvDomainLogic</c>を参照できないため、判定の中核だけを<c>CvBase</c>で共有する）。
	/// </summary>
	public void RefreshCellViolation(JodaiPriceCell cell) {
		cell.IsCostViolation = JodaiPriceRule.IsBelowCost(cell.JodaiNew, TankaGenka);
		cell.IsMinPriceViolation = JodaiPriceRule.IsBelowMinPrice(cell.JodaiNew, MinSellingPrice);
	}
}

/// <summary>
/// Price Matrix（設計書5.4）の1セル（商品×Scope）。<see cref="JodaiMeisaiRow.Cells"/>の要素。
/// </summary>
public partial class JodaiPriceCell : ObservableObject {
	readonly JodaiMeisaiRow owner;

	/// <summary>
	/// <see cref="ApplyComputedValue"/>実行中だけtrueにする再入防止フラグ。この間は
	/// <see cref="OnJodaiNewChanged"/>が<see cref="IsManuallyEdited"/>を立てないようにする。
	/// </summary>
	bool suppressManualFlag;

	public JodaiPriceCell(JodaiMeisaiRow owner) => this.owner = owner;

	/// <summary><see cref="TranJodaiScope.No"/>（伝票内で一意なScope番号）。</summary>
	[ObservableProperty]
	public partial int No_Scope { get; set; }

	/// <summary>このセルの新上代。DataGridから直接編集できる（設計書5.4「確認・例外編集用」）。</summary>
	[ObservableProperty]
	public partial int JodaiNew { get; set; }

	/// <summary>方式4（実効上代からの値下率）の基準額。参考値であり、保存時に<see cref="TranJodaiMeisai.JodaiBase"/>へそのまま渡す。</summary>
	[ObservableProperty]
	public partial int JodaiBase { get; set; }

	/// <summary>原価割れ（設計2.8 C7）。DataGridセルの背景警告に使う。</summary>
	[ObservableProperty]
	public partial bool IsCostViolation { get; set; }

	/// <summary>最低販売価格違反（設計2.8 C8）。DataGridセルの背景警告に使う。</summary>
	[ObservableProperty]
	public partial bool IsMinPriceViolation { get; set; }

	/// <summary>
	/// 手動編集フラグ。DataGridでの直接編集・セル一括操作（設計5.4）で立つ。
	/// <see cref="MasterJouDaiBulkChangeViewModel.BuildDenpyoAsync"/>は、保存直前にこのフラグが立っていない
	/// セルだけをScopeの現在値で再計算し直す（Scope設定を後から変えた場合に、現行どおり最新ルールで
	/// 確定される後方互換を保つため）。立っているセルは「例外編集」として温存し、上書きしない。
	/// </summary>
	[ObservableProperty]
	public partial bool IsManuallyEdited { get; set; }

	partial void OnJodaiNewChanged(int value) {
		owner.RefreshCellViolation(this);
		if (!suppressManualFlag) IsManuallyEdited = true;
	}

	/// <summary>
	/// Scope価格ルールによる自動計算（対象取得直後の既定値・一括計算・保存直前の未編集セル再計算）で使う。
	/// 手動編集フラグは立てない（むしろfalseへ戻す。再計算した以上は「例外」ではなくなるため）。
	/// </summary>
	public void ApplyComputedValue(int jodaiNew, int jodaiBase) {
		suppressManualFlag = true;
		try {
			JodaiBase = jodaiBase;
			JodaiNew = jodaiNew;
		}
		finally {
			suppressManualFlag = false;
		}
		IsManuallyEdited = false;
	}
}

/// <summary>適用範囲（Scope）の1行。<see cref="TranJodai.Jscope"/>の編集用（設計書5.3）。</summary>
public partial class JodaiScopeRow : ObservableObject {
	[ObservableProperty]
	public partial int No { get; set; }

	[ObservableProperty]
	public partial string Name { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial int RangeType { get; set; }

	[ObservableProperty]
	public partial int IncExc { get; set; }

	[ObservableProperty]
	public partial int GroupAxis { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial long Id_Group { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial string Code_Group { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial string Mei_Group { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial long Id_Tenpo { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial string Code_Tenpo { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GroupOrStoreDisplay))]
	public partial string Mei_Tenpo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DayFrom { get; set; } = "19010101";

	[ObservableProperty]
	public partial string DayTo { get; set; } = "99991231";

	[ObservableProperty]
	public partial int PriceMethod { get; set; }

	[ObservableProperty]
	public partial int FixedPrice { get; set; }

	[ObservableProperty]
	public partial decimal RateOff { get; set; }

	[ObservableProperty]
	public partial int Amount { get; set; }

	[ObservableProperty]
	public partial decimal RateOn { get; set; }

	[ObservableProperty]
	public partial int RoundUnit { get; set; }

	[ObservableProperty]
	public partial int RoundType { get; set; }

	[ObservableProperty]
	public partial long Id_PricePoint { get; set; }

	[ObservableProperty]
	public partial int Odr { get; set; }

	MasterJouDaiBulkChangeViewModel.MasterOption? selectedGroupOrStoreOption;

	/// <summary>
	/// XAML側の「グループ/店舗」ComboBoxの選択（<see cref="RangeType"/>によりItemsSourceを切り替える）。
	/// 選択に応じて<see cref="Id_Group"/>/<see cref="Id_Tenpo"/>とそのコード・名称(時点値)を更新する。
	/// </summary>
	public MasterJouDaiBulkChangeViewModel.MasterOption? SelectedGroupOrStoreOption {
		get => selectedGroupOrStoreOption;
		set {
			selectedGroupOrStoreOption = value;
			if (RangeType == (int)EnumJodaiRangeType.Store) {
				Id_Tenpo = value?.Id ?? 0;
				Code_Tenpo = value?.Code ?? string.Empty;
				Mei_Tenpo = value?.Name ?? string.Empty;
			}
			else {
				Id_Group = value?.Id ?? 0;
				Code_Group = value?.Code ?? string.Empty;
				Mei_Group = value?.Name ?? string.Empty;
			}
			OnPropertyChanged();
		}
	}

	/// <summary>読み取り専用セル表示用（範囲種別に応じて全店/グループ名/店舗名を出す）。</summary>
	public string GroupOrStoreDisplay => RangeType switch {
		(int)EnumJodaiRangeType.PriceGroup => string.IsNullOrEmpty(Code_Group) ? "(未選択)" : $"{Code_Group} {Mei_Group}",
		(int)EnumJodaiRangeType.Store => string.IsNullOrEmpty(Code_Tenpo) ? "(未選択)" : $"{Code_Tenpo} {Mei_Tenpo}",
		_ => "(全店)",
	};
}

/// <summary>
/// ④確認タブの競合一覧1行（設計書2.8・5.5）。<see cref="JodaiConflict"/>（<c>CvBase</c>）を種別・深刻度ごとに
/// 集約し、実際の件数（<see cref="Count"/>）を持たせたもの。C1/C2/C5は<see cref="JodaiScopeResolver"/>の
/// 検出インスタンス数、C3は<c>TranJodai.FindDuplicates()</c>の件数、C4/C6/C7/C8は該当明細・該当行の実数。
/// </summary>
/// <param name="Kind">競合種別（C1〜C8）。</param>
/// <param name="Severity">深刻度。</param>
/// <param name="Count">該当件数。</param>
/// <param name="Message">代表例を含む利用者向けメッセージ。</param>
public sealed record JodaiConflictRow(EnumJodaiConflictKind Kind, EnumJodaiConflictSeverity Severity, int Count, string Message) {
	public string KindLabel => $"C{(int)Kind}";
	public string SeverityLabel => Severity switch {
		EnumJodaiConflictSeverity.Error => "エラー",
		EnumJodaiConflictSeverity.Warning => "警告",
		_ => "情報",
	};
}

/// <summary>
/// ④確認タブのTimeline（設計書5.5）の1区間。「いつ・どの商品が・どの店舗で・いくらになるか」を
/// 直感的に確認するのが目的で、描画（横棒）はこのデータを見るだけにする。見た目そのものは自動検証できない
/// ため、UatVmはこのプロパティ（<see cref="MasterJouDaiBulkChangeViewModel.TimelineSegments"/>）を直接検証する。
/// </summary>
/// <param name="DayFrom">区間の開始日（yyyyMMdd）。</param>
/// <param name="DayTo">区間の終了日（yyyyMMdd）。</param>
/// <param name="Jodai">この区間の適用上代。</param>
/// <param name="Source">区間の由来（Scope名／"通常上代"／"他伝票(Id=...)"）。</param>
/// <param name="IsFallback">true なら、どのScopeにも該当せず通常上代へ戻っている区間。</param>
/// <param name="IsOtherSlip">true なら、他伝票の確定済み<see cref="DerivedJodai"/>由来の重ね合わせ区間。</param>
/// <param name="Id_Tran"><paramref name="IsOtherSlip"/>=true のときの伝票Id。それ以外は0。</param>
public sealed record JodaiTimelineSegment(string DayFrom, string DayTo, int Jodai, string Source, bool IsFallback, bool IsOtherSlip, long Id_Tran);
