using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 生地・付属入力 — <see cref="MasterMaterial"/> を明細に持つ <see cref="Tran02Material"/> の入力画面。
/// <para>
/// 商品仕入入力(<see cref="ShiireInputViewModel"/>)と同じ「一覧/詳細」2タブ構成だが、
/// <see cref="Tran02Material"/> は <see cref="TranAllHeader"/> を継承しない(倉庫実在庫連動なし)ため
/// <see cref="Helpers.BaseTranInputViewModel{TDen}"/>（<c>Tran99Meisai</c> 固定）を使えない。
/// 明細管理・合計集計・明細別消費税計算は本クラスで <see cref="Tran99MaterialMeisai"/> 向けに個別実装する。
/// </para>
/// </summary>
public partial class MaterialInputViewModel : Helpers.BasePlainLightMenteViewModel<Tran02Material>, ITranInputTab {
	public sealed record KubunOption(EnumShiire Value, string Name);
	public sealed record IsPayOption(EnumYesNo Value, string Name);

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoListOnListTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoUpdateOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoInsertOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoPrintListCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoPrintDetailCommand))]
	public partial int SelectedTabIndex { get; set; }

	public string DetailStatusText => CurrentEdit.Id > 0
		? $"生地・付属仕入 No. {CurrentEdit.Id:N0}"
		: "新規生地・付属仕入";

	SelectInputParameter? selectParam;

	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumShiire.Shiire, "仕入"),
		new(EnumShiire.Henpin, "仕入返品"),
		new(EnumShiire.Nebiki, "値引"),
		new(EnumShiire.Other, "その他(消費税へ計上)"),
	];

	public IReadOnlyList<IsPayOption> IsPayOptions { get; } = [
		new(EnumYesNo.No, "しない"),
		new(EnumYesNo.Yes, "する"),
	];

	/// <summary>編集中の明細行。</summary>
	[ObservableProperty]
	public partial ObservableCollection<Tran99MaterialMeisai> EditMeisai { get; set; } = [];

	/// <summary>選択中の明細行。</summary>
	[ObservableProperty]
	public partial Tran99MaterialMeisai? SelectedMeisai { get; set; }

	/// <summary>明細行数（ヘッダのバッジ表示用）。</summary>
	public int DetailMeisaiCount => EditMeisai.Count;

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(Tran02Material);
	protected override string? ListOrder => "KakeDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,KakeDay,Id_Shiire,VShiire,Id_Shain,VShain,Kubun,IsPay,ManualNo,SuTotal,KingakuTotal,Tax1,Tax2,Tax3,Total";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "生地・付属仕入",
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
			AddSelectedIdInClause(clauses, "Id_Shiire", selectParam.ToriIds);
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	// 消費税は明細ごとに MasterMaterial.Id_Tax の税区分で計算し、ヘッダはその合計を持つ。
	protected override void OnCurrentEditChangedCore(Tran02Material? oldValue, Tran02Material newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		ApplyMeisaiFromCurrentEdit();
		UpdateHeaderTotals();
		OnPropertyChanged(nameof(DetailStatusText));
		_ = RecalcAllMeisaiTaxAsync();
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is nameof(Tran02Material.Tax1) or nameof(Tran02Material.Tax2)
			or nameof(Tran02Material.Tax3) or nameof(Tran02Material.Kubun)) {
			UpdateHeaderTotals();
			OnPropertyChanged(nameof(TaxTotal));
		}
		// 伝票日付が変われば適用税率が変わるため明細全行を引き直す
		else if (e.PropertyName is nameof(Tran02Material.DenDay)) {
			_ = RecalcAllMeisaiTaxAsync();
		}
	}

	void UpdateHeaderTotals() {
		// 消費税は税区分ごとに1回だけ丸める(TaxCalculator.Apply)。返品等の符号はヘッダ Kubun の CalcFlag が集計側で決める
		var calcUnit = (EnumTaxCalcUnit)CurrentEdit.TaxCalcUnit;
		var rounding = (EnumRounding)CurrentEdit.TaxRounding;
		var totals = TaxCalculator.Apply(EditMeisai, TaxRateOf, calcUnit, rounding);
		CurrentEdit.TaxableAmount1 = totals.TaxableAmount1;
		CurrentEdit.TaxableAmount2 = totals.TaxableAmount2;
		CurrentEdit.TaxableAmount3 = totals.TaxableAmount3;
		CurrentEdit.Tax1 = totals.Tax1;
		CurrentEdit.Tax2 = totals.Tax2;
		CurrentEdit.Tax3 = totals.Tax3;
		CurrentEdit.Total = Math.Abs(CurrentEdit.KingakuTotal) + totals.TaxTotal;
	}

	/// <summary>Tax1+Tax2+Tax3。Tax は分割済みで存在しないため、XAMLの消費税欄表示はこちらを使う。</summary>
	public long TaxTotal => CurrentEdit.Tax1 + CurrentEdit.Tax2 + CurrentEdit.Tax3;

	void UpdateTotals() {
		CurrentEdit.SuTotal = EditMeisai.Sum(m => m.Su);
		CurrentEdit.KingakuTotal = EditMeisai.Sum(m => m.Kingaku);
		OnPropertyChanged(nameof(DetailMeisaiCount));
		UpdateHeaderTotals();
	}

	/// <summary>CurrentEdit.Jmeisai から編集用明細を再構築し、購読・集計を行う。</summary>
	void ApplyMeisaiFromCurrentEdit() {
		foreach (var m in EditMeisai) m.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai = new ObservableCollection<Tran99MaterialMeisai>(
			CurrentEdit.Jmeisai?.Select(Common.CloneObject) ?? []);
		foreach (var m in EditMeisai) m.PropertyChanged += OnMeisaiPropertyChanged;
		UpdateTotals();
	}

	/// <summary>編集用明細を CurrentEdit.Jmeisai へ書き戻し、集計を行う（保存前）。</summary>
	void SyncMeisaiToCurrentEdit() {
		CurrentEdit.Jmeisai = [.. EditMeisai];
		UpdateTotals();
	}

	void OnMeisaiPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (sender is Tran99MaterialMeisai m && e.PropertyName is nameof(Tran99MaterialMeisai.Su) or nameof(Tran99MaterialMeisai.Tanka)) {
			// Kingaku への代入で PropertyChanged が発生し、下の分岐へ再入して税額まで引き直される
			m.Kingaku = m.Su * m.Tanka;
			UpdateTotals();
		}
		else if (e.PropertyName is nameof(Tran99MaterialMeisai.Kingaku)) {
			UpdateTotals();
		}
		// 金額が変われば税額が、生地・付属が変われば税区分が変わるため明細税額を引き直す
		if (sender is Tran99MaterialMeisai target
			&& e.PropertyName is nameof(Tran99MaterialMeisai.Kingaku) or nameof(Tran99MaterialMeisai.Id_Material)) {
			_ = RecalcMeisaiTaxAsync(target, updateTotals: true);
		}
	}

	/// <summary>Id_Material → MasterMaterial.Id_Tax のキャッシュ。明細を触るたびにマスタを引き直さないため。</summary>
	readonly Dictionary<long, long> materialTaxIdCache = [];

	/// <summary>
	/// 伝票日付ごとの消費税区分(1-3)→税率(%)キャッシュ。<see cref="TaxCalculator.Apply"/> の rateOf に渡す。
	/// 伝票日付が変わるたびに区分1-3をまとめて先読みし直す（明細ごとに個別で引かない）。
	/// </summary>
	readonly Dictionary<long, int> taxRateCache = [];
	string? taxRateCacheDenDay;

	/// <summary>伝票日付時点の消費税区分1-3の税率をまとめて先読みし、キャッシュを更新する。</summary>
	async Task EnsureTaxRateCacheAsync(string denDay) {
		if (taxRateCacheDenDay == denDay) return;
		taxRateCache.Clear();
		for (long taxId = 1; taxId <= 3; taxId++) {
			taxRateCache[taxId] = await AppGlobal.LogicGetTax((int)taxId, denDay);
		}
		taxRateCacheDenDay = denDay;
	}

	/// <summary>
	/// キャッシュ済みの税率を返す。<see cref="TaxCalculator.Apply"/> の rateOf にそのまま渡せる。
	/// Id_Tax&lt;=0(非課税)は0を返す(<see cref="AppGlobal.LogicGetTax"/> は0を渡すと例外になるため呼ばない)。
	/// </summary>
	int TaxRateOf(long taxId) => taxId <= 0 ? 0 : taxRateCache.GetValueOrDefault(taxId);

	/// <summary>
	/// 明細1行の消費税区分を、生地・付属マスタから解決し直す。適用税率・税額の確定は
	/// <see cref="TaxCalculator.Apply"/>（<see cref="UpdateHeaderTotals"/>）が行う。
	/// </summary>
	async Task RecalcMeisaiTaxAsync(Tran99MaterialMeisai m, bool updateTotals) {
		m.Id_Tax = await ResolveMeisaiTaxIdAsync(m.Id_Material);
		if (updateTotals) {
			await EnsureTaxRateCacheAsync(CurrentEdit.DenDay);
			UpdateTotals();
		}
	}

	/// <summary>明細全行の消費税区分を再解決してヘッダ合計へ反映する（伝票を開いた時・伝票日付変更時）。</summary>
	async Task RecalcAllMeisaiTaxAsync() {
		await EnsureTaxRateCacheAsync(CurrentEdit.DenDay);
		foreach (var m in EditMeisai) {
			m.Id_Tax = await ResolveMeisaiTaxIdAsync(m.Id_Material);
		}
		UpdateTotals();
	}

	/// <summary>明細の生地・付属から消費税区分を引く。マスタが引けない明細は標準税率(<see cref="TaxCalculator.StandardTaxId"/>)を既定とする。</summary>
	async Task<long> ResolveMeisaiTaxIdAsync(long idMaterial) {
		if (idMaterial <= 0) return TaxCalculator.StandardTaxId;
		if (materialTaxIdCache.TryGetValue(idMaterial, out var cached)) return cached;
		var material = await AppGlobal.LogicGetMasterById<MasterMaterial>(idMaterial);
		var taxId = material?.Id_Tax ?? TaxCalculator.StandardTaxId;
		materialTaxIdCache[idMaterial] = taxId;
		return taxId;
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
	void GoToDetail(Tran02Material? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id <= 0) {
			Current = new Tran02Material {
				DenDay = DateTime.Now.ToString("yyyyMMdd"),
				KakeDay = DateTime.Now.ToString("yyyyMMdd"),
				Kubun = (int)EnumShiire.Shiire,
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
		await RunPrintPdfAsync("MaterialInput_header.qfm", null, new QueryListSqlParam(typeof(Tran02Material), BuildListPrintSql(query), query.Parameters), ct);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintDetail(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync("MaterialInput_detail.qfm", null, new QueryListSqlParam(typeof(Tran02Material), BuildDetailPrintSql(query), query.Parameters), ct);
	}

	const string KubunLabel = "case Kubun when 10 then '仕入' when 20 then '仕入返品' when 30 then '値引' when 99 then 'その他' else cast(Kubun as text) end";
	const string IsPayLabel = "case IsPay when 1 then '支払済' else '未払' end";

	// 画面の V*列共通表示と同じ「(Id) コード 名称」で帳票CSVへ出す（書式定義は CodeNameDisplay 側の1箇所）
	static string CodeNameViewSql(string column) => Helpers.CodeNameDisplay.SqlFromVColumn(column);

	static string DetailCodeNameSql(string value, string code, string name) => Helpers.CodeNameDisplay.Sql(value, code, name);

	static string BuildListPrintSql(QueryListParam query) {
		return $@"
select Id,
DenDay,
KakeDay,
{KubunLabel} KubunText,
{IsPayLabel} IsPayText,
{CodeNameViewSql("VShiire")} Shiire,
{CodeNameViewSql("VShain")} Shain,
ManualNo,
SuTotal,
KingakuTotal,
(Tax1+Tax2+Tax3) Tax,
Total,
ifnull(Memo,'') Memo
from Tran02Material {query.AddWhereOrder()}
";
	}

	static string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from Tran02Material {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		return $@"
select h.Id,
h.DenDay,
h.KakeDay,
{KubunLabel} KubunText,
{IsPayLabel} IsPayText,
{CodeNameViewSql("h.VShiire")} Shiire,
{CodeNameViewSql("h.VShain")} Shain,
h.ManualNo,
h.SuTotal,
h.KingakuTotal,
(h.Tax1+h.Tax2+h.Tax3) Tax,
h.Total,
{M}'$.No') No,
{DetailCodeNameSql($"{M}'$.Id_Material')", $"{M}'$.Code_Material')", $"{M}'$.Mei_Material')")} Material,
ifnull({M}'$.Su'),0) Su,
ifnull({M}'$.Tanka'),0) Tanka,
ifnull({M}'$.Kingaku'),0) Kingaku,
ifnull({M}'$.Tax'),0) Tax,
ifnull({M}'$.Memo'),'') Memo
from ({denpyoSub}) h, json_each(h.Jmeisai) m
order by h.DenDay desc, h.Id desc, cast({M}'$.No') as int)
";
	}

	static Tran99MaterialMeisai CreateNewMeisai(int no) => new() { No = no };

	[RelayCommand]
	void AddMeisai() {
		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		var newMeisai = CreateNewMeisai(nextNo);
		newMeisai.PropertyChanged += OnMeisaiPropertyChanged;
		EditMeisai.Add(newMeisai);
		SelectedMeisai = newMeisai;
	}

	[RelayCommand]
	void DeleteMeisai() {
		if (SelectedMeisai == null) return;
		SelectedMeisai.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai.Remove(SelectedMeisai);
		for (int i = 0; i < EditMeisai.Count; i++) EditMeisai[i].No = i + 1;
		SelectedMeisai = EditMeisai.LastOrDefault();
		UpdateTotals();
	}

	[RelayCommand]
	async Task DoSelectShiire() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Shiire);
		if (shiire == null) return;
		CurrentEdit.Id_Shiire = shiire.Id;
		CurrentEdit.VShiire = new CodeNameView { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };

		// 選択ダイアログはCode/Nameしか返さないため、税設定はIdで1件取得し直す。
		var fullShiire = await AppGlobal.LogicGetMasterById<MasterShiire>(shiire.Id);
		if (fullShiire != null) {
			// 税計算単位・消費税端数処理は伝票作成時点のマスタ値をスナップショットする(Doc/spec/2026-09-01 2.2)。
			// 既存伝票の読込時は上書きしない(このコマンドは仕入先を選び直したときにしか呼ばれない)。
			CurrentEdit.TaxCalcUnit = fullShiire.TaxCalcUnit;
			CurrentEdit.TaxRounding = fullShiire.TaxRounding;
		}
		else {
			// 仕入先が引けない場合は自社既定の端数処理を使う(3.7の解決順3)
			CurrentEdit.TaxRounding = (await AppGlobal.LogicGetSysman()).TaxRounding;
		}
	}

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new CodeNameView { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectMaterial(Tran99MaterialMeisai? meisai) {
		if (meisai != null) SelectedMeisai = meisai;
		if (SelectedMeisai == null) return;
		var material = ShowSelectDialog<MasterMaterial>(typeof(MasterMaterial), "", "Code", startPos: SelectedMeisai.Id_Material);
		if (material == null) return;
		SelectedMeisai.Id_Material = material.Id;
		SelectedMeisai.Code_Material = material.Code ?? "";
		SelectedMeisai.Mei_Material = material.Name ?? "";
		SelectedMeisai.Tanka = material.TankaShiire;
		SelectedMeisai.Kingaku = SelectedMeisai.Su * SelectedMeisai.Tanka;
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (生地・付属仕入No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (生地・付属仕入No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (生地・付属仕入No={CurrentEdit.Id})";
}
