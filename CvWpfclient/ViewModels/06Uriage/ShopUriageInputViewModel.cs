using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.ViewModels._06Uriage;

public partial class ShopUriageInputViewModel : Helpers.BasePlainLightMenteViewModel<Tran01Tenuri> {
	public sealed record MeisaiKubunOption(int Value, string Name);
	const int ProperMeisaiKubun = 0;
	const int SaleMeisaiKubun = 1;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoListOnListTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoUpdateOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoInsertOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoPrintListCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoPrintDetailCommand))]
	int selectedTabIndex;

	[ObservableProperty]
	ObservableCollection<Tran99Meisai> editMeisai = [];

	[ObservableProperty]
	Tran99Meisai? selectedMeisai;

	SelectInputParameter? selectParam;

	public List<EnumUri01> KubunOptions { get; } = [
		EnumUri01.Uriage,
		EnumUri01.Henpin,
	];

	public IReadOnlyList<MeisaiKubunOption> MeisaiKubunOptions { get; } = [
		new(ProperMeisaiKubun, "Pプロパー"),
		new(SaleMeisaiKubun, "Sセール"),
	];

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(Tran01Tenuri);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,Id_Tenpo,VTenpo,Id_Soko,VSoko,Id_Shain,VShain,Id_Customer,VCustomer,SuTotal,KingakuTotal";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "店舗売上",
			ToriLabel = "店舗Id",
			IsToriVisible = true,
			MaxCount = AppGlobal.Limit,
		};
		vm.Initialize(selectParam);
		if (ClientLib.ShowDialogView(win, this, true) != true) return new ValueTask<bool>(false);
		selectParam = vm.Parameter;
		return new ValueTask<bool>(true);
	}

	protected override string? ListWhere {
		get {
			if (selectParam == null) return null;
			List<string> clauses = [];
			if (selectParam.FromId.HasValue) clauses.Add($"Id >= {selectParam.FromId.Value}");
			if (selectParam.ToId.HasValue) clauses.Add($"Id <= {selectParam.ToId.Value}");
			if (!string.IsNullOrWhiteSpace(selectParam.FromDate)) clauses.Add($"DenDay >= '{EscapeSqlLiteral(selectParam.FromDate)}'");
			if (!string.IsNullOrWhiteSpace(selectParam.ToDate)) clauses.Add($"DenDay <= '{EscapeSqlLiteral(selectParam.ToDate)}'");
			AddIdInClause(clauses, "Id_Tenpo", selectParam.ToriIds);
			AddIdInClause(clauses, "Id_Soko", selectParam.SokoIds);
			if (selectParam.ShohinIds.Any(id => id > 0)) clauses.Add(BuildShohinIdInWhere(selectParam.ShohinIds));
			if (!string.IsNullOrWhiteSpace(selectParam.InputBarcode)) clauses.Add(BuildInputBarcodeWhere(selectParam.InputBarcode));
			if (!string.IsNullOrWhiteSpace(selectParam.ShohinNameLike)) clauses.Add(BuildShohinMeisaiWhere(selectParam.ShohinNameLike));
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	static string BuildShohinMeisaiWhere(string shohinText) {
		string like = EscapeSqlLiteral(shohinText);
		return $"""
			EXISTS (
				SELECT 1
				FROM json_each(Jmeisai) AS meisai
				WHERE json_extract(meisai.value, '$.Mei_Shohin') LIKE '%{like}%'
			)
			""";
		/*
					OR json_extract(meisai.value, '$.Id_Shohin') IN (
						SELECT Id
						FROM MasterShohin
						WHERE Name LIKE '%{like}%'
					)
		 */
	}

	static string BuildShohinIdInWhere(IEnumerable<long> ids) {
		string[] values = ids
			.Where(id => id > 0)
			.Distinct()
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray();
		if (values.Length == 0) return string.Empty;
		return $"""
			EXISTS (
				SELECT 1
				FROM json_each(Jmeisai) AS b
				WHERE json_extract(b.value, '$.Id_Shohin') IN ({string.Join(",", values)})
			)
			""";
	}

	static string BuildInputBarcodeWhere(string barcode) {
		string value = EscapeSqlLiteral(barcode.Trim());
		return $"""
			EXISTS (
				SELECT 1
				FROM json_each(Jmeisai) AS b
				WHERE json_extract(b.value, '$.JanCode') = '{value}'
			)
			""";
	}

	static void AddIdInClause(List<string> clauses, string column, IEnumerable<long>? ids) {
		string[] values = ids?
			.Where(id => id > 0)
			.Distinct()
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray() ?? [];
		if (values.Length == 0) return;
		clauses.Add($"{column} IN ({string.Join(",", values)})");
	}

	protected override void OnCurrentEditChangedCore(Tran01Tenuri? oldValue, Tran01Tenuri newValue) {
		if (newValue == null) return;
		bool headerIsSale = IsHeaderSaleKubun(newValue.Kubun);
		newValue.Kubun = NormalizeHeaderKubun(newValue.Kubun);
		ApplyMeisaiFromCurrentEdit(headerIsSale);
	}

	static bool IsHeaderSaleKubun(int kubun) =>
		kubun is (int)EnumUri01.UriSale or (int)EnumUri01.HenSale;

	static int NormalizeHeaderKubun(int kubun) =>
		kubun switch {
			(int)EnumUri01.Henpin or (int)EnumUri01.HenSale => (int)EnumUri01.Henpin,
			_ => (int)EnumUri01.Uriage,
		};

	void ApplyMeisaiFromCurrentEdit(bool forceSaleMeisai) {
		foreach (var m in EditMeisai) m.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai = new ObservableCollection<Tran99Meisai>(
			CurrentEdit.Jmeisai?.Select(Common.CloneObject) ?? []);
		foreach (var m in EditMeisai) {
			m.Kubun = forceSaleMeisai ? SaleMeisaiKubun : NormalizeMeisaiKubun(m.Kubun);
			m.PropertyChanged += OnMeisaiPropertyChanged;
		}
		UpdateTotals();
	}

	void SyncMeisaiToCurrentEdit(bool forceSaleMeisai = false) {
		foreach (var m in EditMeisai) m.Kubun = forceSaleMeisai ? SaleMeisaiKubun : NormalizeMeisaiKubun(m.Kubun);
		CurrentEdit.Jmeisai = [.. EditMeisai];
		UpdateTotals();
	}

	static int NormalizeMeisaiKubun(int kubun) =>
		kubun switch {
			SaleMeisaiKubun or (int)EnumUri01.UriSale or (int)EnumUri01.HenSale => SaleMeisaiKubun,
			_ => ProperMeisaiKubun,
		};

	void OnMeisaiPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (sender is Tran99Meisai m && e.PropertyName is nameof(Tran99Meisai.Su) or nameof(Tran99Meisai.Tanka)) {
			m.Kingaku = m.Su * m.Tanka;
			UpdateTotals();
		}
		else if (e.PropertyName is nameof(Tran99Meisai.Kingaku) or nameof(Tran99Meisai.Jodai) or nameof(Tran99Meisai.Gedai)) {
			UpdateTotals();
		}
	}

	void UpdateTotals() {
		CurrentEdit.SuTotal = EditMeisai.Sum(m => m.Su);
		CurrentEdit.KingakuTotal = EditMeisai.Sum(m => m.Kingaku);
		CurrentEdit.JodaiTotal = EditMeisai.Sum(m => m.Su * m.Jodai);
		CurrentEdit.GedaiTotal = EditMeisai.Sum(m => m.Su * m.Gedai);
	}

	protected override object CreateInsertParam() {
		bool headerIsSale = IsHeaderSaleKubun(CurrentEdit.Kubun);
		CurrentEdit.Kubun = NormalizeHeaderKubun(CurrentEdit.Kubun);
		SyncMeisaiToCurrentEdit(headerIsSale);
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		bool headerIsSale = IsHeaderSaleKubun(CurrentEdit.Kubun);
		CurrentEdit.Kubun = NormalizeHeaderKubun(CurrentEdit.Kubun);
		SyncMeisaiToCurrentEdit(headerIsSale);
		return base.CreateUpdateParam();
	}

	[RelayCommand]
	void GoToDetail(Tran01Tenuri? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id > 0) SelectedTabIndex = 1;
		/*
		var view = ClientLib.GetActiveView(this) as Views._06Uriage.ShopUriageInputView;
		if (view != null) {
			view.TabControlMain.SelectedIndex = 1;
			//			view.TabDetail
		}
		*/
	}

	[RelayCommand]
	void GoToList() {
		SelectedTabIndex = 0;
	}

	[RelayCommand]
	async Task Init() {
		await DoList(CancellationToken.None);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoListOnListTab(CancellationToken ct) {
		await DoList(ct);
	}

	// ---- 印刷 ------------------------------------------------------------
	// printform/ の qfm は PrintStream の「レコード区分」CSV 形式を使う。
	//   CSV 先頭カラム = レコード区分キー。 "H" → ヘッダレコード(HEADn)、それ以外 → 明細(itemn)。
	// 一覧印刷: 伝票ごとに 1 本の "H" 行（HEAD1..HEAD22）。
	// 明細印刷: 伝票ごとに "H" 行（HEAD1..HEAD37）＋ Jmeisai を json_each で展開した明細行（item1..item72）。
	// 列の並びは datarecord の item 定義順に一致させる。未使用スロットは '' で桁を保持する。
	// フィールドの厳密な対応は実機の印刷サーバ出力で最終調整が必要（Tran01Tenuri に存在しない
	// 手入力No/関連No2 等は空文字、消費税/SYSFLG/送信FLG 等はプレースホルダ '0'/'' としている）。

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintList(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync("ShopUriageInput_header.qfm", null, new QueryListSqlParam(typeof(Tran01Tenuri), BuildListPrintSql(query), query.Parameters), ct);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintDetail(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync("ShopUriageInput_detail.qfm", null, new QueryListSqlParam(typeof(Tran01Tenuri), BuildDetailPrintSql(query), query.Parameters), ct);
	}

	// yyyyMMdd(8桁文字列) を "yyyy/MM/dd" に整形
	const string DenDayFmt = "substr(DenDay,1,4)||'/'||substr(DenDay,5,2)||'/'||substr(DenDay,7,2)";
	const string KubunLabel = "case Kubun when 10 then '10 売上' when 20 then '20 売上返品' else cast(Kubun as text) end";
	const string TenpoView = "trim(ifnull(json_extract(VTenpo,'$.Cd'),'')||' '||ifnull(json_extract(VTenpo,'$.Mei'),''))";
	const string CustomerView = "trim(ifnull(json_extract(VCustomer,'$.Cd'),'')||' '||ifnull(json_extract(VCustomer,'$.Mei'),''))";
	const string ShainView = "trim(ifnull(json_extract(VShain,'$.Cd'),'')||' '||ifnull(json_extract(VShain,'$.Mei'),''))";

	/// <summary>式リストへ位置に応じた一意な列別名(c1, c2, ...)を付与する。</summary>
	static string AliasColumns(IReadOnlyList<string> exprs) =>
		string.Join(", ", exprs.Select((e, i) => $"{e} c{i + 1}"));

	/// <summary>外側 select 用に c1..cN のカンマ区切りを生成する。</summary>
	static string OuterColumns(int count) =>
		string.Join(",", Enumerable.Range(1, count).Select(i => $"c{i}"));

	/// <summary>店舗売上伝票一覧（ヘッダ）印刷 SQL。先頭に列見出し "H" 行、以降は伝票 1 件 = item 行 1 本。</summary>
	static string BuildListPrintSql(QueryListParam query) {
		var whereClause = string.IsNullOrWhiteSpace(query.Where) ? string.Empty : $"where {query.Where}";
		var orderBy = string.IsNullOrWhiteSpace(query.Order) ? "Id" : query.Order;
		var limitClause = query.MaxCount.HasValue && query.MaxCount.Value > 0 ? $"limit {query.MaxCount.Value}" : string.Empty;
		var denpyoSub = $"select * from Tran01Tenuri {whereClause} order by {orderBy} {limitClause}".Trim();

		// 列見出し "H" 行 (c1..c45)。ShopUriageInput_header.qfm の Rec01/Rec02 に合わせる。
		var head = new[] {
			"'H'", "'店舗売上伝票一覧'",
			"'伝票No'", "'売上日'", "'伝票区分'", "'取引区分'", "'取引詳細'",
			"'数量計'", "'掛率'", "'SYSFLG'", "'送信FLG'", "'金額計'", "'上代合計'",
			"'下代合計'", "'消費税計'", "'手入力No'", "'関連No2'", "'顧客'", "'入力者'",
			"'性別'", "'年代'", "'注文番号'", "'関連No1'",
		};
		var headCols = AliasColumns(head.Concat(Enumerable.Repeat("''", 45 - head.Length)).ToArray());

		// 明細 item 行 (c1..c45)。prefix "item" として印刷されるよう先頭は 'H' 以外。
		// c3..c23 は ShopUriageInput_header.qfm の Rec02 が参照する item1..item45 のうち使用されているスロット。
		var data = new[] {
			"''", "''",
			"Id", "DenDay", "'1'", KubunLabel, "'0 通常  ' || " + TenpoView,
			"SuTotal", "cast(Rate as text) || '%'", "'0'", "'0'",
			"KingakuTotal", "JodaiTotal", "GedaiTotal", "cast(round(KingakuTotal * 0.1) as int)",
			"''", "''", CustomerView, ShainView, "''", "''", "''", "RelateNo1",
		};
		var dataCols = AliasColumns(data.Concat(Enumerable.Repeat("''", 45 - data.Length)).ToArray());

		return $@"
select {OuterColumns(45)}
from (
  select '' __d, 0 __id, 0 __rt, {headCols}
  union all
  select DenDay __d, Id __id, 1 __rt, {dataCols}
  from ({denpyoSub})
)
order by __rt, __d desc, __id desc
";
	}

	/// <summary>
	/// 店舗売上伝票明細印刷 SQL。伝票 1 件 = "H" 行(HEAD1..HEAD37) ＋ 明細行(item1..item72)。
	/// UNION ALL の桁数を 72 に揃える(H 行は HEAD1..HEAD37 + 空35列)。
	/// 並びは 伝票(Id desc) → H 行 → 明細(No asc)。
	/// </summary>
	static string BuildDetailPrintSql(QueryListParam query) {
		var whereClause = string.IsNullOrWhiteSpace(query.Where) ? string.Empty : $"where {query.Where}";
		var orderBy = string.IsNullOrWhiteSpace(query.Order) ? "Id" : query.Order;
		var limitClause = query.MaxCount.HasValue && query.MaxCount.Value > 0 ? $"limit {query.MaxCount.Value}" : string.Empty;
		var denpyoSub = $"select * from Tran01Tenuri {whereClause} order by {orderBy} {limitClause}".Trim();
		// 明細(item)列: json_each(b) から取得。未対応スロットは '' で桁だけ確保。
		const string M = "json_extract(b.value,";
		var detailCols =
$@"  h.Id                                  c1,   -- item1  グループキー/手入力No 表示
  ''                                    c2,
  ''                                    c3,
  ''                                    c4,
  ''                                    c5,
  {DenDayFmt2()}                        c6,   -- item6  売上日
  ''                                    c7,
  ''                                    c8,
  ''                                    c9,
  ''                                    c10,
  ''                                    c11,
  ''                                    c12,
  ''                                    c13,
  ''                                    c14,
  ''                                    c15,
  ''                                    c16,
  ''                                    c17,
  ''                                    c18,
  ''                                    c19,
  ''                                    c20,
  ''                                    c21,
  ''                                    c22,
  ''                                    c23,
  ''                                    c24,
  ''                                    c25,
  ''                                    c26,
  ''                                    c27,
  ''                                    c28,
  ''                                    c29,
  ''                                    c30,
  ''                                    c31,
  ''                                    c32,
  ''                                    c33,
  ''                                    c34,
  ''                                    c35,
  ''                                    c36,
  ''                                    c37,
  ''                                    c38,
  ''                                    c39,
  ''                                    c40,
  ''                                    c41,
  ''                                    c42,
  ''                                    c43,
  ''                                    c44,
  {M}'$.Code_Col')                      c45,  -- item45 色CD
  {M}'$.Code_Shohin')                   c46,  -- item46 商品CD
  ''                                    c47,
  ''                                    c48,
  {M}'$.Mei_Shohin')                    c49,  -- item49 商品名
  cast({M}'$.Su') as int)               c50,  -- item50 数量
  {M}'$.Tanka')                         c51,  -- item51 単価
  {M}'$.Kingaku')                       c52,  -- item52 金額
  ''                                    c53,
  '0'                                   c54,  -- item54 消費税(プレースホルダ)
  {M}'$.Jodai')                         c55,  -- item55 上代単価
  cast({M}'$.Su') as int)*cast({M}'$.Jodai') as int)   c56,  -- item56 上代金額
  {M}'$.Gedai')                         c57,  -- item57 下代単価
  cast({M}'$.Su') as int)*cast({M}'$.Gedai') as int)   c58,  -- item58 下代金額
  {M}'$.Memo')                          c59,  -- item59 明細メモ/摘要
  ''                                    c60,
  ''                                    c61,
  ''                                    c62,
  ''                                    c63,
  ''                                    c64,
  ''                                    c65,
  ''                                    c66,
  ''                                    c67,
  {M}'$.Code_Siz')                      c68,  -- item68 サイズCD
  {M}'$.Mei_Col')                       c69,  -- item69 色名
  ''                                    c70,
  {M}'$.No')                            c71,  -- item71 行No
  {M}'$.Mei_Siz')                       c72   -- item72 サイズ名";

		// H 行: HEAD1..HEAD37 を c1..c37 へ、残り c38..c72 は ''。
		var headerCols =
$@"  'H'                       c1,   -- HEAD1  レコード区分キー
  '店舗売上伝票明細'         c2,   -- HEAD2  タイトル
  cast(Id as text)          c3,   -- HEAD3  伝票No
  {DenDayFmt}               c4,   -- HEAD4  売上日
  '1'                       c5,   -- HEAD5  伝票区分
  {KubunLabel}              c6,   -- HEAD6  取引区分
  '0 通常  '||{TenpoView}   c7,   -- HEAD7  取引詳細+店舗
  cast(Rate as text)||'%'   c8,   -- HEAD8  掛率
  '0'                       c9,   -- HEAD9  SYSFLG
  '0'                       c10,  -- HEAD10 送信FLG
  cast(SuTotal as text)     c11,  -- HEAD11 数量計
  cast(KingakuTotal as text) c12, -- HEAD12 金額計
  cast(JodaiTotal as text)  c13,  -- HEAD13 上代合計
  cast(GedaiTotal as text)  c14,  -- HEAD14 下代合計
  ''                        c15,  -- HEAD15 手入力No
  cast(RelateNo1 as text)   c16,  -- HEAD16 関連No1
  ''                        c17,  -- HEAD17 関連No2
  {CustomerView}            c18,  -- HEAD18 顧客
  {ShainView}               c19,  -- HEAD19 入力者
  ''                        c20,  -- HEAD20 性別
  ''                        c21,  -- HEAD21 年代
  '0'                       c22,  -- HEAD22 消費税計
  ''                        c23, '' c24, '' c25, '' c26, '' c27,
  cast(SuTotal as text)     c28,  -- HEAD28 合計行 数量計
  cast(KingakuTotal as text) c29, -- HEAD29 合計行 金額計
  cast(JodaiTotal as text)  c30,  -- HEAD30 合計行 上代合計
  cast(GedaiTotal as text)  c31,  -- HEAD31 合計行 下代合計
  '' c32, '' c33, '' c34, '' c35, '' c36, '' c37,
  '' c38, '' c39, '' c40, '' c41, '' c42, '' c43, '' c44,
  '' c45, '' c46, '' c47, '' c48, '' c49, '' c50, '' c51, '' c52,
  '' c53, '' c54, '' c55, '' c56, '' c57, '' c58, '' c59, '' c60,
  '' c61, '' c62, '' c63, '' c64, '' c65, '' c66, '' c67, '' c68,
  '' c69, '' c70, '' c71, '' c72";

		return $@"
select c1,c2,c3,c4,c5,c6,c7,c8,c9,c10,c11,c12,c13,c14,c15,c16,c17,c18,c19,c20,
       c21,c22,c23,c24,c25,c26,c27,c28,c29,c30,c31,c32,c33,c34,c35,c36,c37,c38,
       c39,c40,c41,c42,c43,c44,c45,c46,c47,c48,c49,c50,c51,c52,c53,c54,c55,c56,
       c57,c58,c59,c60,c61,c62,c63,c64,c65,c66,c67,c68,c69,c70,c71,c72
from (
  select DenDay sday, Id sid, 0 rt, 0 mno,
{headerCols}
  from ({denpyoSub})
  union all
  select h.DenDay sday, h.Id sid, 1 rt, cast(json_extract(b.value,'$.No') as int) mno,
{detailCols}
  from ({denpyoSub}) h, json_each(h.Jmeisai) b
)
order by sday desc, sid desc, rt, mno";
	}

	// 明細側は json_each(b) が 'id' 列を持ち非修飾 Id と衝突するため、
	// 画面 WHERE は json_each 結合前のサブクエリ内で適用してから展開する。

	// item6(売上日) は date decode(S0.4/S4.2/S6.2) 用に生の yyyyMMdd を渡す。
	static string DenDayFmt2() => "h.DenDay";

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoUpdateOnDetailTab(CancellationToken ct) {
		await DoUpdate(ct);
	}

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoDeleteOnDetailTab(CancellationToken ct) {
		await DoDelete(ct);
	}

	[RelayCommand(CanExecute = nameof(IsDetailTabSelected), IncludeCancelCommand = true)]
	async Task DoInsertOnDetailTab(CancellationToken ct) {
		await DoInsert(ct);
	}

	[RelayCommand]
	void AddMeisai() {
		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		var newMeisai = new Tran99Meisai { No = nextNo, Kubun = ProperMeisaiKubun };
		newMeisai.PropertyChanged += OnMeisaiPropertyChanged;
		EditMeisai.Add(newMeisai);
	}

	[RelayCommand]
	void DeleteMeisai() {
		if (SelectedMeisai == null) return;
		SelectedMeisai.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai.Remove(SelectedMeisai);
		RenumberMeisaiNo();
		SelectedMeisai = EditMeisai.LastOrDefault() ?? null;
		UpdateTotals();
	}

	void RenumberMeisaiNo() {
		for (int i = 0; i < EditMeisai.Count; i++) {
			EditMeisai[i].No = i + 1;
		}
	}

	[RelayCommand]
	void DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		if (ClientLib.ShowDialogView(win, this) != true) return;

		ApplyBarcodeMeisai(vm.CreateMeisaiRows(CurrentEdit.Kubun));
	}

	void ApplyBarcodeMeisai(IEnumerable<Tran99Meisai> rows) {
		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		foreach (var row in rows) {
			var existing = EditMeisai.FirstOrDefault(m =>
				!string.IsNullOrWhiteSpace(row.JanCode) &&
				string.Equals(m.JanCode, row.JanCode, StringComparison.OrdinalIgnoreCase));
			if (existing != null) {
				existing.Su += row.Su;
				SelectedMeisai = existing;
				continue;
			}

			row.No = nextNo++;
			row.Kubun = ProperMeisaiKubun;
			row.Kingaku = row.Su * row.Tanka;
			row.PropertyChanged += OnMeisaiPropertyChanged;
			EditMeisai.Add(row);
			SelectedMeisai = row;
		}
		UpdateTotals();
	}

	[RelayCommand]
	void DoSelectTenpo() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType>=0", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (tokui == null) return;
		CurrentEdit.Id_Tenpo = tokui.Id;
		CurrentEdit.VTenpo = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectSoko() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Soko);
		if (tokui == null) return;
		CurrentEdit.Id_Soko = tokui.Id;
		CurrentEdit.VSoko = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new CodeNameView { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectCustomer() {
		var customer = ShowSelectDialog<MasterEndCustomer>(typeof(MasterEndCustomer), "", "Code", startPos: CurrentEdit.Id_Customer);
		if (customer == null) return;
		CurrentEdit.Id_Customer = customer.Id;
		CurrentEdit.VCustomer = new CodeNameView { Sid = customer.Id, Cd = customer.Code ?? "", Mei = customer.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectShohin(Tran99Meisai? meisai) {
		if (meisai != null) SelectedMeisai = meisai;
		if (SelectedMeisai == null) return;
		var shohin = ShowShohinSelectDialog();
		if (shohin == null) return;
		SelectedMeisai.Id_Shohin = shohin.Id;
		SelectedMeisai.Code_Shohin = shohin.Code ?? "";
		SelectedMeisai.Mei_Shohin = shohin.Name ?? "";
		SelectedMeisai.Id_Col = 0;
		SelectedMeisai.Code_Col = "";
		SelectedMeisai.Mei_Col = "";
		SelectedMeisai.Id_Siz = 0;
		SelectedMeisai.Code_Siz = "";
		SelectedMeisai.Mei_Siz = "";
		SelectedMeisai.JanCode = "";
		SelectedMeisai.Tanka = shohin.TankaJodai;
		SelectedMeisai.Jodai = shohin.TankaJodai;
		SelectedMeisai.Gedai = shohin.TankaGenka;
	}

	MasterShohin? ShowShohinSelectDialog() {
		var selWin = new Views.Sub.SelectShohinView();
		if (selWin.DataContext is not SelectShohinViewModel vm) return null;
		vm.ShohinCodeFrom = SelectedMeisai?.Code_Shohin ?? string.Empty;
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.SelectedShohin;
	}

	[RelayCommand]
	void DoSelectCol(Tran99Meisai? meisai) {
		if (meisai != null) SelectedMeisai = meisai;
		if (SelectedMeisai == null) return;
		if (SelectedMeisai.Id_Shohin <= 0) {
			MessageEx.ShowWarningDialog("商品を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}
		var selected = ShowShohinColSizSelectDialog(filterByColor: false);
		if (selected == null) return;
		ApplyShohinColSiz(selected);
	}

	[RelayCommand]
	void DoSelectSiz(Tran99Meisai? meisai) {
		if (meisai != null) SelectedMeisai = meisai;
		if (SelectedMeisai == null) return;
		if (SelectedMeisai.Id_Shohin <= 0) {
			MessageEx.ShowWarningDialog("商品を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (SelectedMeisai.Id_Col <= 0) {
			MessageEx.ShowWarningDialog("カラーを選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}
		var selected = ShowShohinColSizSelectDialog(filterByColor: true);
		if (selected == null) return;
		ApplyShohinColSiz(selected);
	}

	DerivedShohinColSiz? ShowShohinColSizSelectDialog(bool filterByColor) {
		if (SelectedMeisai == null) return null;
		var selWin = new Views.Sub.SelectShohinColSizView();
		if (selWin.DataContext is not SelectShohinColSizViewModel vm) return null;
		vm.SetParam(
			idShohin: SelectedMeisai.Id_Shohin,
			idCol: SelectedMeisai.Id_Col,
			idSiz: SelectedMeisai.Id_Siz,
			filterByColor: filterByColor);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current;
	}

	void ApplyShohinColSiz(DerivedShohinColSiz selected) {
		if (SelectedMeisai == null) return;
		SelectedMeisai.Id_Col = selected.Id_Col;
		SelectedMeisai.Code_Col = selected.Code_Col;
		SelectedMeisai.Mei_Col = selected.Mei_Col;
		SelectedMeisai.Id_Siz = selected.Id_Siz;
		SelectedMeisai.Code_Siz = selected.Code_Siz;
		SelectedMeisai.Mei_Siz = selected.Mei_Siz;
		SelectedMeisai.JanCode = selected.Jan1;
	}

	[RelayCommand]
	void DoSelectMeisaiShain(Tran99Meisai? meisai) {
		if (meisai != null) SelectedMeisai = meisai;
		if (SelectedMeisai == null) return;
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: SelectedMeisai.Id_Shain);
		if (shain == null) return;
		SelectedMeisai.Id_Shain = shain.Id;
		SelectedMeisai.Code_Shain = shain.Code ?? "";
		SelectedMeisai.Mei_Shain = shain.Name ?? "";
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (伝票No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (伝票No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (伝票No={CurrentEdit.Id})";
}
