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

public partial class ShiireInputViewModel : Helpers.BaseTranInputViewModel<Tran03Shiire>, ITranInputTab {
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
	public partial int SelectedTabIndex { get; set; }

	// ---- 派生画面(仕入返品入力)向けのフック -------------------------------------
	// 仕入返品は「区分が返品系に固定された仕入伝票」でしかなく、入力項目・明細操作・印刷SQLは
	// 完全に同じ。SQLを2箇所に持つと片方だけ直して食い違うので、ここを virtual にして派生させる。
	// （Phase 8 の原価無バリアントと同じ方針）

	/// <summary>画面名・確認メッセージに使う短い名称</summary>
	protected virtual string DenLabel => "仕入";

	/// <summary>新規伝票の既定区分</summary>
	protected virtual EnumShiire DefaultKubun => EnumShiire.Shiire;

	/// <summary>一覧に出す区分の絞り込み条件（null なら絞らない）</summary>
	protected virtual string? KubunListWhere => null;

	/// <summary>帳票ファイル名の接頭辞。`{接頭辞}_header.qfm` / `{接頭辞}_detail.qfm` を使う</summary>
	protected virtual string FormFilePrefix => "ShiireInput";

	public override string DetailStatusText => CurrentEdit.Id > 0
		? $"{DenLabel} No. {CurrentEdit.Id:N0}"
		: $"新規{DenLabel}";

	SelectInputParameter? selectParam;

	// 消化仕入(15/25)は通常は消化仕入更新(CostUpdateDbConsumption)が自動生成する(GeneratedKind=1)。
	// イレギュラーで後付けの起票が必要になる場合があるため、選択肢としては残す。
	// 手入力分は GeneratedKind=0 になるため、消化仕入更新の再生成・削除対象(GeneratedKind=1限定)には含まれない。
	public virtual IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumShiire.Shiire, "仕入"),
		new(EnumShiire.SoldOnShiire, "消化仕入"),
		new(EnumShiire.Henpin, "仕入返品"),
		new(EnumShiire.SoldOnHenpin, "消化仕入返品"),
		new(EnumShiire.Nebiki, "値引"),
		new(EnumShiire.Tax, "消費税"),
	];

	public IReadOnlyList<MeisaiKubunOption> MeisaiKubunOptions { get; } = [
		new(ProperMeisaiKubun, "Pプロパー"),
		new(SaleMeisaiKubun, "Sセール"),
	];

	public IReadOnlyList<IsPayOption> IsPayOptions { get; } = [
		new(EnumYesNo.No, "しない"),
		new(EnumYesNo.Yes, "する"),
	];

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(Tran03Shiire);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,KakeDay,Id_Shiire,VShiire,Id_Soko,VSoko,Id_Shain,VShain,Kubun,IsPay,ManualNo,RelateNo1,Rate,SuTotal,KingakuTotal,Tax1,Tax2,Tax3,Total";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = DenLabel,
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
			if (!string.IsNullOrWhiteSpace(KubunListWhere)) clauses.Add(KubunListWhere);
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

	// 消費税は明細ごとに MasterShohin.Id_Tax の税区分で計算し、ヘッダはその合計を持つ。
	protected override bool IsMeisaiTaxEnabled => true;

	protected override void OnCurrentEditChangedCore(Tran03Shiire? oldValue, Tran03Shiire newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		ApplyMeisaiFromCurrentEdit();
		// ここは同期メソッドのため税率キャッシュの充填を await できない。キャッシュが空だと
		// 税額 0 の暫定値になるが、直後の RecalcAllMeisaiTaxAsync が正しい値へ書き直す。
		UpdateHeaderTotals();
		OnPropertyChanged(nameof(DetailStatusText));
		_ = RecalcAllMeisaiTaxAsync();
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		// Tax1/2/3 は UpdateHeaderTotals の出力であって入力ではない。監視すると自己再入になるため含めない。
		if (e.PropertyName is nameof(Tran03Shiire.Kubun)
			or nameof(Tran03Shiire.TaxCalcUnit) or nameof(Tran03Shiire.TaxRounding)) {
			if (e.PropertyName == nameof(Tran03Shiire.Kubun)) NormalizeIsStockForKubun();
			UpdateHeaderTotals();
		}
		// 伝票日付が変われば適用税率が変わるため明細全行を引き直す
		else if (e.PropertyName is nameof(Tran03Shiire.DenDay)) {
			_ = RecalcAllMeisaiTaxAsync();
		}
	}

	// 消化仕入(15/25)は在庫を動かさない(消化仕入更新が自動生成する分と同じ扱い)。
	// 自動生成分は IsStock=0 で作られるが、この画面から手入力で区分を選んだ場合は既定値 1 のまま
	// 保存されてしまうため、区分変更のたびにここで揃える。それ以外の区分では 1(在庫加算する)に戻す。
	void NormalizeIsStockForKubun() {
		var kubun = (EnumShiire)CurrentEdit.Kubun;
		CurrentEdit.IsStock = kubun is EnumShiire.SoldOnShiire or EnumShiire.SoldOnHenpin ? 0 : 1;
	}

	// 基底フック: 明細集計後に消費税・総合計を再計算する。
	protected override void OnTotalsUpdated() => UpdateHeaderTotals();

	// 基底フック: 明細区分を P/S に正規化する。
	protected override int ResolveMeisaiKubun(Tran99Meisai m) => NormalizeMeisaiKubun(m.Kubun);

	static int NormalizeMeisaiKubun(int kubun) =>
		kubun switch {
			SaleMeisaiKubun => SaleMeisaiKubun,
			_ => ProperMeisaiKubun,
		};

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
		OnPropertyChanged(nameof(TaxTotal));
	}

	/// <summary>Tax1+Tax2+Tax3。Tax は分割済みで存在しないため、XAMLの消費税欄表示はこちらを使う。</summary>
	public long TaxTotal => CurrentEdit.Tax1 + CurrentEdit.Tax2 + CurrentEdit.Tax3;

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
			Current = CreateNewDenpyo();
		}
		SelectedTabIndex = 1;
	}

	/// <summary>新規伝票の既定値。</summary>
	protected virtual Tran03Shiire CreateNewDenpyo() => new() {
		DenDay = DateTime.Now.ToString("yyyyMMdd"),
		KakeDay = DateTime.Now.ToString("yyyyMMdd"),
		Kubun = (int)DefaultKubun,
		Rate = 100,
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
		await RunPrintPdfAsync($"{FormFilePrefix}_header.qfm", null, new QueryListSqlParam(typeof(Tran03Shiire), BuildListPrintSql(query), query.Parameters), ct);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintDetail(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync($"{FormFilePrefix}_detail.qfm", null, new QueryListSqlParam(typeof(Tran03Shiire), BuildDetailPrintSql(query), query.Parameters), ct);
	}

	// 帳票CSVは旧cvnet(SubDIgInp03.crs)のSELECT列順を踏襲する。
	// 旧cvnetにのみ存在する項目は空欄とし、全列に一意な itemN 別名を付ける。
	const int DenpyoShoriKubun = 3;
	const string ShimeDayNone = "19010101";

	static string KubunNameSql(string prefix) =>
		$"case {prefix}Kubun when 10 then '仕入' when 15 then '消化仕入' when 20 then '仕入返品' when 25 then '消化仕入返品' when 30 then '値引' when 99 then '消費税' else cast({prefix}Kubun as text) end";

	static string KubunLabelSql(string prefix) => $"(cast({prefix}Kubun as text) || ' ' || {KubunNameSql(prefix)})";
	static string IsPayLabelSql(string prefix) => $"case {prefix}IsPay when 1 then '1 する' else '0 しない' end";
	static string VCd(string column) => $"ifnull(json_extract({column},'$.Cd'),'')";
	static string VMei(string column) => $"ifnull(json_extract({column},'$.Mei'),'')";
	// PrintStream は全角1文字だけの値を描画しないため（色名「黒」など）、1文字の名称に半角空白を足して回避する。
	static string Pad1(string expr) => $"case when length({expr})=1 then {expr}||' ' else {expr} end";
	const string ShiireKubunLabelSql =
		"case s.PurchaseType when 0 then '0 通常仕入' when 3 then '3 消化仕入' else ifnull(cast(s.PurchaseType as text),'') end";

	static string BuildListPrintSql(QueryListParam query) {
		return $@"
select
Id as item1,
'' as item2,
'' as item3,
'商品仕入伝票一覧' as item4,
ifnull(ManualNo,'') as item5,
DenDay as item6,
KakeDay as item7,
Kubun as item8,
{VCd("VShain")} as item9,
{VCd("VSoko")} as item10,
{VCd("VShiire")} as item11,
Rate as item12,
(TaxableAmount1+TaxableAmount2+TaxableAmount3) as item13,
SuTotal as item14,
KingakuTotal as item15,
'' as item16,
(Tax1+Tax2+Tax3) as item17,
JodaiTotal as item18,
GedaiTotal as item19,
ifnull(Memo,'') as item20,
IsPay as item21,
{DenpyoShoriKubun} as item22,
'' as item23,
RelateNo1 as item24,
'' as item25,
'' as item26,
'' as item27,
'' as item28,
{VMei("VShain")} as item29,
{VMei("VShiire")} as item30,
{VMei("VSoko")} as item31,
'' as item32,
TaxCalcUnit as item33,
TaxRounding as item34,
case IsPay when 1 then '○' else '' end as item35,
'{ShimeDayNone}' as item36,
'' as item37,
'' as item38,
{KubunLabelSql("")} as item39,
{IsPayLabelSql("")} as item40,
'' as item41,
'' as item42
from Tran03Shiire {query.AddWhereOrder()}
";
	}

	static string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from Tran03Shiire {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		return $@"
select
h.Id as item1,
'' as item2,
'' as item3,
'商品仕入伝票明細' as item4,
ifnull(h.ManualNo,'') as item5,
h.DenDay as item6,
h.KakeDay as item7,
h.Kubun as item8,
{VCd("h.VShain")} as item9,
{VCd("h.VSoko")} as item10,
{VCd("h.VShiire")} as item11,
h.Rate as item12,
(h.TaxableAmount1+h.TaxableAmount2+h.TaxableAmount3) as item13,
h.SuTotal as item14,
h.KingakuTotal as item15,
'' as item16,
(h.Tax1+h.Tax2+h.Tax3) as item17,
h.JodaiTotal as item18,
h.GedaiTotal as item19,
{Pad1("ifnull(h.Memo,'')")} as item20,
h.IsPay as item21,
{DenpyoShoriKubun} as item22,
'' as item23,
h.RelateNo1 as item24,
'' as item25,
'' as item26,
'' as item27,
'' as item28,
{Pad1(VMei("h.VShain"))} as item29,
{Pad1(VMei("h.VShiire"))} as item30,
{Pad1(VMei("h.VSoko"))} as item31,
'' as item32,
h.TaxCalcUnit as item33,
h.TaxRounding as item34,
case h.IsPay when 1 then '○' else '' end as item35,
'{ShimeDayNone}' as item36,
'' as item37,
'' as item38,
ifnull({M}'$.Kubun'),0) as item39,
ifnull({M}'$.Code_Shohin'),'') as item40,
'' as item41,
ifnull({M}'$.Code_Col'),'') as item42,
ifnull({M}'$.Code_Siz'),'') as item43,
{Pad1($"ifnull({M}'$.Mei_Shohin'),'')")} as item44,
ifnull({M}'$.Su'),0) as item45,
ifnull({M}'$.Tanka'),0) as item46,
ifnull({M}'$.Kingaku'),0) as item47,
'' as item48,
ifnull({M}'$.Tax'),0) as item49,
ifnull({M}'$.Jodai'),0) as item50,
(cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Jodai'),0) as int)) as item51,
ifnull({M}'$.Gedai'),0) as item52,
(cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Gedai'),0) as int)) as item53,
{Pad1($"ifnull({M}'$.Memo'),'')")} as item54,
'' as item55,
'' as item56,
'' as item57,
'' as item58,
ifnull({M}'$.JanCode'),'') as item59,
'' as item60,
'' as item61,
{Pad1($"ifnull({M}'$.Mei_Col'),'')")} as item62,
{Pad1($"ifnull({M}'$.Mei_Siz'),'')")} as item63,
{KubunLabelSql("h.")} as item64,
{IsPayLabelSql("h.")} as item65,
'' as item66,
cast(ifnull({M}'$.No'),0) as int) as item67,
ifnull(s.MakerHin,'') as item68,
'' as item69,
{ShiireKubunLabelSql} as item70,
ifnull({M}'$.TaxRate'),0) as item71
from ({denpyoSub}) h
cross join json_each(h.Jmeisai) m
left join MasterShohin s on s.Id = cast(ifnull({M}'$.Id_Shohin'),0) as int)
order by h.DenDay desc, h.Id desc, cast({M}'$.No') as int)
";
	}

	// 基底フック: 仕入明細の新規行は P プロパー区分で作る。
	protected override Tran99Meisai CreateNewMeisai(int no) => new() { No = no, Kubun = ProperMeisaiKubun };

	[RelayCommand]
	async Task DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		// 上代一括変更の適用価格を引くための対象軸（仕入は得意先が特定できないので本部売上用の全件行・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Honbu;
		vm.JodaiTenpoId = 0;
		vm.JodaiDay = CurrentEdit.DenDay;
		if (ClientLib.ShowDialogView(win, this) != true) return;

		await ApplyBarcodeMeisai(vm.CreateMeisaiRows(ProperMeisaiKubun));
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
	async Task DoSelectShiire() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Shiire);
		if (shiire == null) return;
		CurrentEdit.Id_Shiire = shiire.Id;
		CurrentEdit.VShiire = new CodeNameView { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };

		// 選択ダイアログはCode/Nameしか返さないため、掛率・税設定はIdで1件取得し直す。
		var fullShiire = await AppGlobal.LogicGetMasterById<MasterShiire>(shiire.Id);
		if (fullShiire != null) {
			CurrentEdit.Rate = fullShiire.RateProper;
			// 税計算単位・消費税端数処理は伝票作成時点のマスタ値をスナップショットする(Doc/spec/2026-09-01 2.2)。
			// 既存伝票の読込時は上書きしない(このコマンドは仕入先を選び直したときにしか呼ばれない)。
			CurrentEdit.TaxCalcUnit = fullShiire.TaxCalcUnit;
			CurrentEdit.TaxRounding = fullShiire.TaxRounding;
		}
		else {
			// 仕入先が引けない場合は自社既定の端数処理を使う(3.7の解決順3)
			CurrentEdit.TaxRounding = (await AppGlobal.LogicGetSysman()).TaxRounding;
		}
		// 税計算単位・端数処理が変われば税額が変わる。差し替え後の値がたまたま同値だと
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
		SelectedMeisai.Tanka = shohin.TankaGenka;
		SelectedMeisai.Jodai = shohin.TankaJodai;
		SelectedMeisai.Gedai = shohin.TankaGenka;
		// 同じ商品を選び直すと Id_Shohin が同値で PropertyChanged が出ないため、明示的に引き直す
		await RecalcMeisaiTaxAsync(SelectedMeisai, updateTotals: true);
	}

	MasterShohin? ShowShohinSelectDialog() {
		var selWin = new Views.Sub.SelectShohinView();
		if (selWin.DataContext is not SelectShohinViewModel vm) return null;
		vm.ShohinCodeFrom = SelectedMeisai?.Code_Shohin ?? string.Empty;
		// 上代一括変更の適用価格を引くための対象軸（仕入は得意先が特定できないので本部売上用の全件行・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Honbu;
		vm.JodaiTenpoId = 0;
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

	protected override string GetInsertConfirmMessage() => $"追加しますか？ ({DenLabel}No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ ({DenLabel}No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ ({DenLabel}No={CurrentEdit.Id})";

	// G0-4.3.1: 完了済み発注に紐付く仕入を編集したら気付き用の警告を出す（RelateNo1 = 発注Id）。
	// 仕入返品(HenpinInput)も本クラスを継承するので同じ挙動になる。
	protected override void AfterInsert(Tran03Shiire item) {
		base.AfterInsert(item);
		_ = WarnIfLinkedZanCompletedAsync(typeof(Tran13Hachu), item.RelateNo1, DenLabel, "発注", "発注残完了設定");
	}

	protected override void AfterUpdate(Tran03Shiire item) {
		base.AfterUpdate(item);
		_ = WarnIfLinkedZanCompletedAsync(typeof(Tran13Hachu), item.RelateNo1, DenLabel, "発注", "発注残完了設定");
	}

	protected override void AfterDelete(Tran03Shiire removedItem) {
		base.AfterDelete(removedItem);
		_ = WarnIfLinkedZanCompletedAsync(typeof(Tran13Hachu), removedItem.RelateNo1, DenLabel, "発注", "発注残完了設定");
	}
}
