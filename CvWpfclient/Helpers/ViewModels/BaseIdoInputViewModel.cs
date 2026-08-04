/*
# description
BaseIdoInputViewModel は倉庫間移動の伝票入力画面（即時移動 / 積送出庫 / 積送入庫）の共通基底クラスです。

移動3伝票は「倉庫(出庫元) → 移動先」という同じ構造(ITranIdo)を持ち、在庫計算のフラグだけが
`TranCalcBase.GetCalcSoko/GetCalcIdosaki` でテーブル名によって切り替わります。
つまり画面側は伝票の種類が変わっても入力項目・明細操作・印刷SQLがすべて同じなので、
ここに集約して派生クラスは「どのテーブルか」「画面名」「帳票ファイル名」だけを与えます。

# example
public partial class IdoInputSokuViewModel : Helpers.BaseIdoInputViewModel<Tran05Ido> {
	protected override string IdoDisplayName => "移動(即時)";
	protected override string FormFilePrefix => "IdoInputSoku";
}
 */
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.ViewModels.Sub;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.Helpers;

public abstract partial class BaseIdoInputViewModel<TDen> : BaseTranInputViewModel<TDen>, ITranInputTab
	where TDen : TranAllHeader, ITranIdo, new() {

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoListOnListTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoUpdateOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoInsertOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoPrintListCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoPrintDetailCommand))]
	public partial int SelectedTabIndex { get; set; }

	/// <summary>一覧条件ダイアログや確認メッセージに出す画面名（例: "移動(積送)"）</summary>
	protected abstract string IdoDisplayName { get; }

	/// <summary>帳票ファイル名の接頭辞。`{接頭辞}_header.qfm` / `{接頭辞}_detail.qfm` を使う</summary>
	protected abstract string FormFilePrefix { get; }

	/// <summary>伝票Noの前に付ける短い名称（既定は "移動"）</summary>
	protected virtual string DenLabel => "移動";

	public override string DetailStatusText => CurrentEdit.Id > 0
		? $"{DenLabel} No. {CurrentEdit.Id:N0}"
		: $"新規{DenLabel}";

	SelectInputParameter? selectParam;

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(TDen);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,Id_Soko,VSoko,Id_Ido,VIdo,Id_Shain,VShain,RelateNo1,ManualNo,SuTotal,KingakuTotal";

	/// <summary>印刷SQLで参照するテーブル名。Tabletype と一致する。</summary>
	protected string TableName => typeof(TDen).Name;

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = IdoDisplayName,
			ToriLabel = "移動先Id",
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
			AddIdInClause(clauses, "Id_Ido", selectParam.ToriIds);
			AddIdInClause(clauses, "Id_Soko", selectParam.SokoIds);
			if (selectParam.ShohinIds.Any(id => id > 0)) clauses.Add(BuildShohinIdInWhere(selectParam.ShohinIds));
			if (!string.IsNullOrWhiteSpace(selectParam.InputBarcode)) clauses.Add(BuildInputBarcodeWhere(selectParam.InputBarcode));
			if (!string.IsNullOrWhiteSpace(selectParam.ShohinNameLike)) clauses.Add(BuildShohinMeisaiWhere(selectParam.ShohinNameLike));
			AppendAdditionalListWhere(clauses);
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	/// <summary>派生画面固有の一覧条件を足すフック（既定は何もしない）。</summary>
	protected virtual void AppendAdditionalListWhere(List<string> clauses) { }

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

	protected override void OnCurrentEditChangedCore(TDen? oldValue, TDen newValue) {
		if (newValue == null) return;
		ApplyMeisaiFromCurrentEdit();
		OnPropertyChanged(nameof(DetailStatusText));
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
	void GoToDetail(TDen? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id <= 0) {
			Current = CreateNewDenpyo();
		}
		SelectedTabIndex = 1;
	}

	/// <summary>新規伝票の既定値。派生で移動先の初期値などを足す。</summary>
	protected virtual TDen CreateNewDenpyo() => new() {
		DenDay = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
		Jmeisai = [],
	};

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
		await RunPrintPdfAsync($"{FormFilePrefix}_header.qfm", null,
			new QueryListSqlParam(typeof(TDen), BuildListPrintSql(query), query.Parameters), ct);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintDetail(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync($"{FormFilePrefix}_detail.qfm", null,
			new QueryListSqlParam(typeof(TDen), BuildDetailPrintSql(query), query.Parameters), ct);
	}

	// 画面の V*列共通表示と同じ「(Id) コード 名称」で帳票CSVへ出す（書式定義は CodeNameDisplay 側の1箇所）
	static string CodeNameViewSql(string column) => Helpers.CodeNameDisplay.SqlFromVColumn(column);

	static string DetailCodeNameSql(string value, string code, string name) => Helpers.CodeNameDisplay.Sql(value, code, name);

	string BuildListPrintSql(QueryListParam query) {
		return $@"
select Id,
DenDay,
{CodeNameViewSql("VSoko")} Soko,
{CodeNameViewSql("VIdo")} Ido,
{CodeNameViewSql("VShain")} Shain,
RelateNo1,
ManualNo,
SuTotal,
KingakuTotal,
JodaiTotal,
GedaiTotal,
ifnull(Memo,'') Memo,
'' as Dummy1,
0 as Dummy2,
0 as Dummy3
from {TableName} {query.AddWhereOrder()}
";
	}

	string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from {TableName} {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		return $@"
select h.Id,
h.DenDay,
{CodeNameViewSql("h.VSoko")} Soko,
{CodeNameViewSql("h.VIdo")} Ido,
{CodeNameViewSql("h.VShain")} Shain,
h.RelateNo1,
h.ManualNo,
h.SuTotal,
h.KingakuTotal,
{M}'$.No') No,
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
ifnull({M}'$.Memo'),'') Memo,
'' as Dummy1,
0 as Dummy2,
0 as Dummy3,
0 as Dummy4
from ({denpyoSub}) h, json_each(h.Jmeisai) m
order by h.DenDay desc, h.Id desc, cast({M}'$.No') as int)
";
	}

	[RelayCommand]
	void DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		if (ClientLib.ShowDialogView(win, this) != true) return;

		ApplyBarcodeMeisai(vm.CreateMeisaiRows(0));
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
	void DoSelectIdo() {
		var ido = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Ido);
		if (ido == null) return;
		CurrentEdit.Id_Ido = ido.Id;
		CurrentEdit.VIdo = new CodeNameView { Sid = ido.Id, Cd = ido.Code ?? "", Mei = ido.Name ?? "" };
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

	protected override string GetInsertConfirmMessage() => $"追加しますか？ ({DenLabel}No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ ({DenLabel}No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ ({DenLabel}No={CurrentEdit.Id})";
}
