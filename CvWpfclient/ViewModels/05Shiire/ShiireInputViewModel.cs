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

namespace CvWpfclient.ViewModels._05Shiire;

public partial class ShiireInputViewModel : Helpers.BasePlainLightMenteViewModel<Tran03Shiire> {
	public sealed record KubunOption(EnumShiire Value, string Name);
	public sealed record MeisaiKubunOption(int Value, string Name);
	public sealed record IsPayOption(EnumYesNo Value, string Name);

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

	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumShiire.Shiire, "仕入"),
		new(EnumShiire.Henpin, "仕入返品"),
		new(EnumShiire.Nebiki, "値引"),
		new(EnumShiire.Other, "その他"),
	];

	public IReadOnlyList<MeisaiKubunOption> MeisaiKubunOptions { get; } = [
		new(ProperMeisaiKubun, "Pプロパー"),
		new(SaleMeisaiKubun, "Sセール"),
	];

	public IReadOnlyList<IsPayOption> IsPayOptions { get; } = [
		new(EnumYesNo.No, "未払"),
		new(EnumYesNo.Yes, "支払済"),
	];

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(Tran03Shiire);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,KakeDay,Id_Shiire,VShiire,Id_Soko,VSoko,Id_Shain,VShain,Kubun,IsPay,ManualNo,RelateNo1,Rate,SuTotal,KingakuTotal,Tax,Total";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "仕入",
			ToriLabel = "仕入先Id",
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
			AddIdInClause(clauses, "Id_Shiire", selectParam.ToriIds);
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

	protected override void OnCurrentEditChangedCore(Tran03Shiire? oldValue, Tran03Shiire newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		ApplyMeisaiFromCurrentEdit();
		UpdateHeaderTotals();
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is nameof(Tran03Shiire.Tax) or nameof(Tran03Shiire.Kubun)) {
			UpdateHeaderTotals();
		}
	}

	void ApplyMeisaiFromCurrentEdit() {
		foreach (var m in EditMeisai) m.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai = new ObservableCollection<Tran99Meisai>(
			CurrentEdit.Jmeisai?.Select(Common.CloneObject) ?? []);
		foreach (var m in EditMeisai) {
			m.Kubun = NormalizeMeisaiKubun(m.Kubun);
			m.PropertyChanged += OnMeisaiPropertyChanged;
		}
		UpdateTotals();
	}

	void SyncMeisaiToCurrentEdit() {
		foreach (var m in EditMeisai) m.Kubun = NormalizeMeisaiKubun(m.Kubun);
		CurrentEdit.Jmeisai = [.. EditMeisai];
		UpdateTotals();
	}

	static int NormalizeMeisaiKubun(int kubun) =>
		kubun switch {
			SaleMeisaiKubun => SaleMeisaiKubun,
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
		UpdateHeaderTotals();
	}

	void UpdateHeaderTotals() {
		CurrentEdit.CalcFlag = CurrentEdit.EnKubun == EnumShiire.Shiire ? 1 : -1;
		CurrentEdit.Total = CurrentEdit.KingakuTotal + CurrentEdit.Tax;
	}

	protected override object CreateInsertParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateUpdateParam();
	}

	[RelayCommand]
	void GoToDetail(Tran03Shiire? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id <= 0) {
			Current = new Tran03Shiire {
				DenDay = DateTime.Now.ToString("yyyyMMdd"),
				KakeDay = DateTime.Now.ToString("yyyyMMdd"),
				Kubun = (int)EnumShiire.Shiire,
				CalcFlag = 1,
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

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintList(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync("ShiireInput_header.qfm", null, new QueryListSqlParam(typeof(Tran03Shiire), BuildListPrintSql(query), query.Parameters), ct);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintDetail(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync("ShiireInput_detail.qfm", null, new QueryListSqlParam(typeof(Tran03Shiire), BuildDetailPrintSql(query), query.Parameters), ct);
	}

	const string KubunLabel = "case Kubun when 10 then '仕入' when 20 then '仕入返品' when 30 then '値引' when 99 then 'その他' else cast(Kubun as text) end";
	const string IsPayLabel = "case IsPay when 1 then '支払済' else '未払' end";

	static string CodeNameViewSql(string column) =>
		$"trim(ifnull(json_extract({column},'$.Sid'),'') || ' ' || ifnull(json_extract({column},'$.Cd'),'') || ' ' || ifnull(json_extract({column},'$.Mei'),''))";

	static string DetailCodeNameSql(string value, string code, string name) =>
		$"trim(ifnull({value},'') || ' ' || ifnull({code},'') || ' ' || ifnull({name},''))";

	static string MeisaiKubunLabelSql(string jsonExtractPrefix) =>
		$"case cast(ifnull({jsonExtractPrefix}'$.Kubun'),0) as int) when 1 then 'S セール' else 'P プロパー' end";

	static string BuildListPrintSql(QueryListParam query) {
		return $@"
select Id,
DenDay,
KakeDay,
{KubunLabel} KubunText,
{IsPayLabel} IsPayText,
{CodeNameViewSql("VShiire")} Shiire,
{CodeNameViewSql("VSoko")} Soko,
{CodeNameViewSql("VShain")} Shain,
ManualNo,
RelateNo1,
Rate,
SuTotal,
KingakuTotal,
Tax,
Total,
JodaiTotal,
GedaiTotal,
ifnull(Memo,'') Memo
from Tran03Shiire {query.AddWhereOrder()}
";
	}

	static string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from Tran03Shiire {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		return $@"
select h.Id,
h.DenDay,
h.KakeDay,
{KubunLabel} KubunText,
{IsPayLabel} IsPayText,
{CodeNameViewSql("h.VShiire")} Shiire,
{CodeNameViewSql("h.VSoko")} Soko,
{CodeNameViewSql("h.VShain")} Shain,
h.ManualNo,
h.RelateNo1,
h.Rate,
h.SuTotal,
h.KingakuTotal,
h.Tax,
h.Total,
{M}'$.No') No,
{MeisaiKubunLabelSql(M)} MeisaiKubunText,
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

		ApplyBarcodeMeisai(vm.CreateMeisaiRows(ProperMeisaiKubun));
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
	void DoSelectShiire() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Shiire);
		if (shiire == null) return;
		CurrentEdit.Id_Shiire = shiire.Id;
		CurrentEdit.VShiire = new CodeNameView { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };
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
		SelectedMeisai.Tanka = shohin.TankaGenka;
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

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (仕入No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (仕入No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (仕入No={CurrentEdit.Id})";
}
