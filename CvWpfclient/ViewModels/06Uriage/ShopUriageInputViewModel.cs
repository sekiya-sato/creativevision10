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
	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumUri01.Uriage, "売上"),
		new(EnumUri01.Henpin, "返品"),
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
		UpdateHeaderTotals();
		OnPropertyChanged(nameof(DetailStatusText));
		_ = RecalcAllMeisaiTaxAsync();
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is nameof(Tran01Tenuri.Tax) or nameof(Tran01Tenuri.Kubun)) {
			UpdateHeaderTotals();
		}
		// 伝票日付が変われば適用税率が変わるため明細全行を引き直す
		else if (e.PropertyName is nameof(Tran01Tenuri.DenDay)) {
			_ = RecalcAllMeisaiTaxAsync();
		}
	}

	protected override void OnTotalsUpdated() => UpdateHeaderTotals();

	void UpdateHeaderTotals() {
		var absKingakuTotal = Math.Abs(CurrentEdit.KingakuTotal);
		// 明細Taxは常に正値。返品等の符号はヘッダ Kubun の CalcFlag が集計側で決める
		var tax = EditMeisai.Sum(m => m.Tax);
		CurrentEdit.Tax = tax;
		CurrentEdit.Total = absKingakuTotal + tax;
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
			Current = new Tran01Tenuri {
				DenDay = DateTime.Now.ToString("yyyyMMdd"),
				Kubun = (int)EnumUri01.Uriage,
				Jmeisai = [],
			};
		}
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
	// qfm 側にタイトルと列見出しを持たせ、SQL は画面入力項目に対応するデータ列だけを返す。

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

	// DenDay は qfm 側で yyyy/MM/dd 表示にするため、SQL では yyyyMMdd のまま返す。
	const string KubunLabel = "case Kubun when 10 then '売上' when 11 then '売上セール' when 20 then '返品' when 21 then '返品セール' else cast(Kubun as text) end";

	// 画面の V*列共通表示と同じ「(Id) コード 名称」で帳票CSVへ出す（書式定義は CodeNameDisplay 側の1箇所）
	static string CodeNameViewSql(string column) => Helpers.CodeNameDisplay.SqlFromVColumn(column);

	static string DetailCodeNameSql(string value, string code, string name) => Helpers.CodeNameDisplay.Sql(value, code, name);

	/// <summary>店舗売上伝票印刷 SQL。見出しは qfm の static text に持たせ、ここではデータ列だけを返す。</summary>
	static string BuildListPrintSql(QueryListParam query) {
		return $@"
select Id,
DenDay,
{KubunLabel} KubunText,
{CodeNameViewSql("VTenpo")} Tenpo,
{CodeNameViewSql("VSoko")} Soko,
{CodeNameViewSql("VShain")} Shain,
{CodeNameViewSql("VCustomer")} Customer,
SuTotal,
KingakuTotal,
JodaiTotal,
GedaiTotal,
ifnull(Memo,'') Memo
from Tran01Tenuri {query.AddWhereOrder()}
";
	}

	/// <summary>
	/// 店舗売上伝票明細印刷 SQL。対象伝票を一覧条件で絞り、Jmeisai を json_each で明細行へ展開する。
	/// </summary>
	static string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from Tran01Tenuri {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		var detailKubunLabel = $"case cast(ifnull({M}'$.Kubun'),0) as int) when 1 then 'S セール' else 'P プロパー' end";
		return $@"
select h.Id,
h.DenDay,
{KubunLabel} KubunText,
{CodeNameViewSql("h.VTenpo")} Tenpo,
{CodeNameViewSql("h.VSoko")} Soko,
{CodeNameViewSql("h.VShain")} Shain,
{CodeNameViewSql("h.VCustomer")} Customer,
{M}'$.No') No,
{detailKubunLabel} MeisaiKubunText,
{DetailCodeNameSql($"{M}'$.Id_Shohin')", $"{M}'$.Code_Shohin')", $"{M}'$.Mei_Shohin')")} Shohin,
{DetailCodeNameSql($"{M}'$.Id_Col')", $"{M}'$.Code_Col')", $"{M}'$.Mei_Col')")} Col,
{DetailCodeNameSql($"{M}'$.Id_Siz')", $"{M}'$.Code_Siz')", $"{M}'$.Mei_Siz')")} Siz,
ifnull({M}'$.Su'),0) Su,
ifnull({M}'$.Tanka'),0) Tanka,
ifnull({M}'$.Kingaku'),0) Kingaku,
ifnull({M}'$.Jodai'),0) Jodai,
cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Jodai'),0) as int) JodaiKingaku,
ifnull({M}'$.Gedai'),0) Gedai,
cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Gedai'),0) as int) GedaiKingaku,
{DetailCodeNameSql($"{M}'$.Id_Shain')", $"{M}'$.Code_Shain')", $"{M}'$.Mei_Shain')")} MeisaiShain,
ifnull({M}'$.Memo'),'') Memo
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
	void DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		// 上代一括変更の適用価格を引くための対象軸（店舗売上なので店舗用・当該店舗・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Tenpo;
		vm.JodaiTenpoId = CurrentEdit.Id_Tenpo;
		vm.JodaiDay = CurrentEdit.DenDay;
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
	void DoInputShohinColSiz() {
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

		ApplyShohinColSizMeisai(vm.GetResults());
	}

	void ApplyShohinColSizMeisai(IEnumerable<InputShohinColSizRow> rows) {
		var results = rows.ToList();
		if (results.Count == 0) return;

		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		var firstResult = results[0];
		var firstTarget = SelectedMeisai;

		if (firstTarget != null && firstTarget.Id_Col == 0 && firstTarget.Id_Siz == 0) {
			FillMeisaiFromColSizRow(firstTarget, firstResult);
			firstTarget.PropertyChanged += OnMeisaiPropertyChanged;
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

		UpdateTotals();
	}

	static void FillMeisaiFromColSizRow(Tran99Meisai meisai, InputShohinColSizRow row) {
		meisai.Id_Col = row.Source.Id_Col;
		meisai.Code_Col = row.Source.Code_Col;
		meisai.Mei_Col = row.Source.Mei_Col;
		meisai.Id_Siz = row.Source.Id_Siz;
		meisai.Code_Siz = row.Source.Code_Siz;
		meisai.Mei_Siz = row.Source.Mei_Siz;
		meisai.Su = row.Su;
		meisai.Kingaku = meisai.Su * meisai.Tanka;
		meisai.JanCode = row.Source.Jan1;
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
