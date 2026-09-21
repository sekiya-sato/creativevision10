using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.ViewModels._06Uriage;

public partial class ShopUriageInputViewModel : Helpers.BaseTranInputViewModel<Tran01Tenuri>, ITranInputTab {
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
	public partial int SelectedTabIndex { get; set; }

	public override string DetailStatusText => CurrentEdit.Id > 0
		? $"売上 No. {CurrentEdit.Id:N0}"
		: "新規売上";

	SelectInputParameter? selectParam;

	public sealed record KubunOption(EnumUri01 Value, string Name);
	// セール(11/21)はここに含めない。セールは明細区分側のトグルで表現する既存設計のため。
	// 社販(14/24)はプロパー扱いだがセールとは別概念のため、ヘッダ区分として選択肢に加える。
	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumUri01.Uriage, "売上"),
		new(EnumUri01.Henpin, "返品"),
		new(EnumUri01.UriShahan, "社販売上"),
		new(EnumUri01.HenShahan, "社販返品"),
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

	// 消費税は明細ごとに MasterShohin.Id_Tax の税区分で計算し、ヘッダはその合計を持つ。
	protected override bool IsMeisaiTaxEnabled => true;

	protected override void OnCurrentEditChangedCore(Tran01Tenuri? oldValue, Tran01Tenuri newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		bool headerIsSale = IsHeaderSaleKubun(newValue.Kubun);
		newValue.Kubun = NormalizeHeaderKubun(newValue.Kubun);
		ApplyMeisaiFromCurrentEdit(headerIsSale);
		// ここは同期メソッドのため税率キャッシュの充填を await できない。キャッシュが空だと
		// 税額 0 の暫定値になるが、直後の RecalcAllMeisaiTaxAsync が正しい値へ書き直す。
		UpdateHeaderTotals();
		OnPropertyChanged(nameof(DetailStatusText));
		_ = RecalcAllMeisaiTaxAsync();
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		// Tax1/2/3 は UpdateHeaderTotals の出力であって入力ではない。監視すると自己再入になるため含めない。
		if (e.PropertyName is nameof(Tran01Tenuri.Kubun) or nameof(Tran01Tenuri.TaxRounding)) {
			UpdateHeaderTotals();
		}
		// 伝票日付が変われば適用税率が変わるため明細全行を引き直す
		else if (e.PropertyName is nameof(Tran01Tenuri.DenDay)) {
			_ = RecalcAllMeisaiTaxAsync();
		}
	}

	protected override void OnTotalsUpdated() => UpdateHeaderTotals();

	void UpdateHeaderTotals() {
		// 店舗売上はTaxCalcUnitを持たない(常に伝票単位。現金売のため)。消費税は税区分ごとに1回だけ丸める(TaxCalculator.Apply)。
		// 返品等の符号はヘッダ Kubun の CalcFlag が集計側で決める
		var rounding = (EnumRounding)CurrentEdit.TaxRounding;
		var totals = TaxCalculator.Apply(EditMeisai, TaxRateOf, EnumTaxCalcUnit.Slip, rounding);
		CurrentEdit.TaxableAmount1 = totals.TaxableAmount1;
		CurrentEdit.TaxableAmount2 = totals.TaxableAmount2;
		CurrentEdit.TaxableAmount3 = totals.TaxableAmount3;
		CurrentEdit.Tax1 = totals.Tax1;
		CurrentEdit.Tax2 = totals.Tax2;
		CurrentEdit.Tax3 = totals.Tax3;
		CurrentEdit.Total = Math.Abs(CurrentEdit.KingakuTotal) + totals.TaxTotal;
	}

	// 社販(14/24)はプロパー扱いであってセールではないため、ここには含めない。
	static bool IsHeaderSaleKubun(int kubun) =>
		kubun is (int)EnumUri01.UriSale or (int)EnumUri01.HenSale;

	// セール明細の有無に応じて売上系(10/11)・返品系(20/21)を正規化する。
	// 社販(14/24)はプロパー扱いのため、セール明細が含まれていても 11/21 に化けさせず、そのまま保持する。
	// 消費税(99)はこの画面(店舗売上入力)からは新規作成されず、POS確定(PointOfSaleService)も
	// 10/11/20/21 のみを発行するため実運用では発生しない。念のため既存の既定(売上10)を維持する。
	static int NormalizeHeaderKubun(int kubun) =>
		kubun switch {
			(int)EnumUri01.UriShahan => (int)EnumUri01.UriShahan,
			(int)EnumUri01.HenShahan => (int)EnumUri01.HenShahan,
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

	// 社販(14/24)は明細区分では第3の値を持たず、業務決定によりプロパー(0)に寄せる(_ に落ちる)。
	static int NormalizeMeisaiKubun(int kubun) =>
		kubun switch {
			SaleMeisaiKubun or (int)EnumUri01.UriSale or (int)EnumUri01.HenSale => SaleMeisaiKubun,
			_ => ProperMeisaiKubun,
		};

	// 明細行の金額計算・集計 (OnMeisaiPropertyChanged / UpdateTotals) は基底を使用。
	// Apply/Sync はセール区分の強制(forceSale)がヘッダ正規化前に確定する固有制御のため VM に温存する。

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
		if (Current.Id <= 0) {
			Current = CreateNewDenpyo();
		}
		SelectedTabIndex = 1;
	}

	/// <summary>新規伝票の既定値。</summary>
	protected virtual Tran01Tenuri CreateNewDenpyo() => new() {
		DenDay = DateTime.Now.ToString("yyyyMMdd"),
		Kubun = (int)EnumUri01.Uriage,
		Jmeisai = [],
	};

	/// <summary>一覧の選択状態に関わらず、新規伝票を作って詳細タブを開く。</summary>
	[RelayCommand]
	void GoToNew() {
		Current = CreateNewDenpyo();
		SelectedTabIndex = 1;
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
	// qfm (旧cvnet cvnet01prn_header/detail.qfm を移植) は列見出しをstatic textに持ち、
	// item1..itemN は旧cvnet SubDIgInp01.crs の OnQueryPrint/OnQueryDetailPrint が組み立てる
	// d_sql.txt の列順と完全一致させている。CV10に対応列が無い旧項目は '' で空欄にする
	// (旧内税/外税消費税はCV10がTax1/2/3の税率別集計のみのため合計を外税消費税列へ、
	//  掛計上日はCV10にこの伝票専用列が無いためDenDayを流用、消費税端数はTaxRoundingをそのまま出力)。

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

	/// <summary>店舗売上伝票一覧印刷 SQL（cvnet01prn_header.qfm item1..item45 に対応）。</summary>
	static string BuildListPrintSql(QueryListParam query) {
		return $@"
select
OldSeqNo item1,
'' item2,
'' item3,
'店舗売上伝票一覧' item4,
ifnull(Code_Customer,'') item5,
DenDay item6,
DenDay item7,
Kubun item8,
ifnull(json_extract(VShain,'$.Cd'),'') item9,
ifnull(json_extract(VSoko,'$.Cd'),'') item10,
ifnull(json_extract(VTenpo,'$.Cd'),'') item11,
Rate item12,
'' item13,
SuTotal item14,
KingakuTotal item15,
'' item16,
Tax1 + Tax2 + Tax3 item17,
JodaiTotal item18,
GedaiTotal item19,
ifnull(Memo,'') item20,
'' item21,
'' item22,
'' item23,
RelateNo1 item24,
'' item25,
'' item26,
'' item27,
'' item28,
'' item29,
'' item30,
'' item31,
'' item32,
'' item33,
'' item34,
'' item35,
ifnull(json_extract(VShain,'$.Mei'),'') item36,
ifnull(json_extract(VTenpo,'$.Mei'),'') item37,
ifnull(json_extract(VSoko,'$.Mei'),'') item38,
'' item39,
'' item40,
'' item41,
TaxRounding item42,
'' item43,
'' item44,
'' item45
from Tran01Tenuri {query.AddWhereOrder()}
";
	}

	/// <summary>
	/// 店舗売上伝票明細印刷 SQL（cvnet01prn_detail.qfm item1..item72 に対応）。
	/// 対象伝票を一覧条件で絞り、Jmeisai を json_each で明細行へ展開する。
	/// </summary>
	static string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from Tran01Tenuri {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		return $@"
select
h.OldSeqNo item1,
'' item2,
'' item3,
'店舗売上伝票明細' item4,
ifnull(h.Code_Customer,'') item5,
h.DenDay item6,
h.DenDay item7,
h.Kubun item8,
ifnull(json_extract(h.VShain,'$.Cd'),'') item9,
ifnull(json_extract(h.VSoko,'$.Cd'),'') item10,
ifnull(json_extract(h.VTenpo,'$.Cd'),'') item11,
h.Rate item12,
'' item13,
h.SuTotal item14,
h.KingakuTotal item15,
'' item16,
h.Tax1 + h.Tax2 + h.Tax3 item17,
h.JodaiTotal item18,
h.GedaiTotal item19,
ifnull(h.Memo,'') item20,
'' item21,
'' item22,
'' item23,
h.RelateNo1 item24,
'' item25,
'' item26,
'' item27,
'' item28,
'' item29,
'' item30,
'' item31,
'' item32,
'' item33,
'' item34,
'' item35,
ifnull(json_extract(h.VShain,'$.Mei'),'') item36,
ifnull(json_extract(h.VTenpo,'$.Mei'),'') item37,
ifnull(json_extract(h.VSoko,'$.Mei'),'') item38,
'' item39,
'' item40,
'' item41,
h.TaxRounding item42,
'' item43,
'' item44,
ifnull({M}'$.Code_Siz'),'') item45,
ifnull({M}'$.Code_Shohin'),'') item46,
ifnull({M}'$.Code_Col'),'') item47,
'' item48,
ifnull({M}'$.Mei_Shohin'),'') item49,
ifnull({M}'$.Su'),0) item50,
ifnull({M}'$.Tanka'),0) item51,
ifnull({M}'$.Kingaku'),0) item52,
'' item53,
ifnull({M}'$.Tax'),0) item54,
ifnull({M}'$.Jodai'),0) item55,
cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Jodai'),0) as int) item56,
ifnull({M}'$.Gedai'),0) item57,
cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Gedai'),0) as int) item58,
ifnull({M}'$.Memo'),'') item59,
'' item60,
'' item61,
'' item62,
'' item63,
'' item64,
'' item65,
'' item66,
'' item67,
ifnull({M}'$.Mei_Col'),'') item68,
ifnull({M}'$.Mei_Siz'),'') item69,
'' item70,
ifnull({M}'$.No'),0) item71,
'' item72
from ({denpyoSub}) h, json_each(h.Jmeisai) m
order by h.DenDay desc, h.Id desc, cast({M}'$.No') as int)
";
	}

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

	// 基底フック: 店舗売上の新規行は P プロパー区分で作る。
	protected override Tran99Meisai CreateNewMeisai(int no) => new() { No = no, Kubun = ProperMeisaiKubun };

	[RelayCommand]
	async Task DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		// 上代一括変更の適用価格を引くための対象軸（店舗売上なので店舗用・当該店舗・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Tenpo;
		vm.JodaiTenpoId = CurrentEdit.Id_Tenpo;
		vm.JodaiDay = CurrentEdit.DenDay;
		if (ClientLib.ShowDialogView(win, this) != true) return;

		await ApplyBarcodeMeisai(vm.CreateMeisaiRows(CurrentEdit.Kubun));
	}

	async Task ApplyBarcodeMeisai(IEnumerable<Tran99Meisai> rows) {
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
			row.Kingaku = (long)row.Su * row.Tanka;
			row.PropertyChanged += OnMeisaiPropertyChanged;
			EditMeisai.Add(row);
			SelectedMeisai = row;
		}
		// InputBarcodeRow.ToMeisai は Id_Tax を持たず、値を詰めてから購読を張るため
		// OnMeisaiPropertyChanged 経由の税区分解決にも乗らない。ここで全行を引き直す
		// （数量を加算しただけの既存行も同じ1回で正しくなる）。内部で UpdateTotals まで行う。
		await RecalcAllMeisaiTaxAsync();
	}

	[RelayCommand]
	async Task DoInputShohinColSiz() {
		if (SelectedMeisai == null) {
			MessageEx.ShowWarningDialog("明細行を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (SelectedMeisai.Id_Shohin <= 0) {
			MessageEx.ShowWarningDialog("商品を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}

		var win = new Views.Sub.InputShohinColSizView();
		if (win.DataContext is not InputShohinColSizViewModel vm) return;
		vm.SetParam(SelectedMeisai.Id_Shohin);
		if (ClientLib.ShowDialogView(win, this) != true) return;

		await ApplyShohinColSizMeisai(vm.GetResults());
	}

	async Task ApplyShohinColSizMeisai(IEnumerable<InputShohinColSizRow> rows) {
		var results = rows.ToList();
		if (results.Count == 0) return;

		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		var firstResult = results[0];
		var firstTarget = SelectedMeisai;

		if (firstTarget != null && firstTarget.Id_Col == 0 && firstTarget.Id_Siz == 0) {
			// firstTarget は EditMeisai の既存要素であり購読済み。ここで足すと二重購読になる
			FillMeisaiFromColSizRow(firstTarget, firstResult);
			SelectedMeisai = firstTarget;
			results = results.Skip(1).ToList();
		}

		foreach (var result in results) {
			var row = new Tran99Meisai {
				No = nextNo++,
				Kubun = ProperMeisaiKubun,
				Id_Shohin = SelectedMeisai?.Id_Shohin ?? 0,
				Code_Shohin = SelectedMeisai?.Code_Shohin ?? string.Empty,
				Mei_Shohin = SelectedMeisai?.Mei_Shohin ?? string.Empty,
				Tanka = SelectedMeisai?.Tanka ?? 0,
				Jodai = SelectedMeisai?.Jodai ?? 0,
				Gedai = SelectedMeisai?.Gedai ?? 0,
			};
			FillMeisaiFromColSizRow(row, result);
			row.PropertyChanged += OnMeisaiPropertyChanged;
			EditMeisai.Add(row);
			SelectedMeisai = row;
		}

		// 展開行は Id_Tax を持たず、Su/Kingaku も購読を張る前に確定するため税区分解決に乗らない。
		// ここで全行を引き直す。内部で UpdateTotals まで行う。
		await RecalcAllMeisaiTaxAsync();
	}

	static void FillMeisaiFromColSizRow(Tran99Meisai meisai, InputShohinColSizRow row) {
		meisai.Id_Col = row.Source.Id_Col;
		meisai.Code_Col = row.Source.Code_Col;
		meisai.Mei_Col = row.Source.Mei_Col;
		meisai.Id_Siz = row.Source.Id_Siz;
		meisai.Code_Siz = row.Source.Code_Siz;
		meisai.Mei_Siz = row.Source.Mei_Siz;
		meisai.Su = row.Su;
		meisai.Kingaku = (long)meisai.Su * meisai.Tanka;
		meisai.JanCode = row.Source.Jan1;
	}

	[RelayCommand]
	async Task DoSelectTenpo() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType>=0", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (tokui == null) return;
		CurrentEdit.Id_Tenpo = tokui.Id;
		CurrentEdit.VTenpo = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };

		// 選択ダイアログはCode/Nameしか返さないため、端数処理はIdで1件取得し直す。
		var fullTenpo = await AppGlobal.LogicGetMasterById<MasterTokui>(tokui.Id);
		if (fullTenpo != null) {
			// 店舗売上はTaxCalcUnitを持たず常に伝票単位。端数処理は伝票作成時点のマスタ値をスナップショットする
			// (Doc/spec/2026-09-01 2.2 / 3.7)。既存伝票の読込時は上書きしない(このコマンドは店舗を選び直したときにしか呼ばれない)。
			CurrentEdit.TaxRounding = fullTenpo.TaxRounding;
		}
		else {
			// 店舗が引けない場合は自社既定の端数処理を使う(3.7の解決順3)
			CurrentEdit.TaxRounding = (await AppGlobal.LogicGetSysman()).TaxRounding;
		}
		// 端数処理が変われば税額が変わる。差し替え後の値がたまたま同値だと
		// PropertyChanged が出ずヘッダが古いままになるため、ここで明示的に引き直す。
		UpdateHeaderTotals();
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
	async Task DoSelectShohin(Tran99Meisai? meisai) {
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
		// 同じ商品を選び直すと Id_Shohin が同値で PropertyChanged が出ないため、明示的に引き直す
		await RecalcMeisaiTaxAsync(SelectedMeisai, updateTotals: true);
	}

	MasterShohin? ShowShohinSelectDialog() {
		var selWin = new Views.Sub.SelectShohinView();
		if (selWin.DataContext is not SelectShohinViewModel vm) return null;
		vm.ShohinCodeFrom = SelectedMeisai?.Code_Shohin ?? string.Empty;
		// 上代一括変更の適用価格を引くための対象軸（店舗売上なので店舗用・当該店舗・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Tenpo;
		vm.JodaiTenpoId = CurrentEdit.Id_Tenpo;
		vm.JodaiDay = CurrentEdit.DenDay;
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
