using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.ViewModels._03Hatchu;

public partial class HachuInputViewModel : Helpers.BaseTranInputViewModel<Tran13Hachu>, ITranInputTab {
	public sealed record KubunOption(EnumHachu Value, string Name);
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
		? $"発注 No. {CurrentEdit.Id:N0}"
		: "新規発注";

	SelectInputParameter? selectParam;

	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumHachu.Hachu, "発注"),
		new(EnumHachu.Tsuika, "追加発注"),
		new(EnumHachu.Jido, "自動発注"),
		new(EnumHachu.Henpin, "発注返品"),
	];

	public IReadOnlyList<MeisaiKubunOption> MeisaiKubunOptions { get; } = [
		new(ProperMeisaiKubun, "Pプロパー"),
		new(SaleMeisaiKubun, "Sセール"),
	];

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(Tran13Hachu);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,NouhinDay,Id_Shiire,VShiire,Id_Soko,VSoko,Id_Shain,VShain,Kubun,RelateNo1,Rate,SuTotal,KingakuTotal,Tax1,Tax2,Tax3,Total";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "発注",
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

	// 消費税は明細ごとに MasterShohin.Id_Tax の税区分で計算し、ヘッダはその合計を持つ。
	protected override bool IsMeisaiTaxEnabled => true;
	// 直近に選択・読込した仕入先のリードタイム日数。発注日変更時の納品予定日再計算に使う。
	int shiireLeadTimeDays;

	protected override void OnCurrentEditChangedCore(Tran13Hachu? oldValue, Tran13Hachu newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		ApplyMeisaiFromCurrentEdit();
		// ここは同期メソッドのため税率キャッシュの充填を await できない。キャッシュが空だと
		// 税額 0 の暫定値になるが、直後の RecalcAllMeisaiTaxAsync が正しい値へ書き直す。
		UpdateHeaderTotals();
		OnPropertyChanged(nameof(DetailStatusText));

		shiireLeadTimeDays = 0;
		_ = RecalcAllMeisaiTaxAsync();
		if (newValue.Id <= 0) {
			_ = ApplyDefaultSokoAsync();
		}
		else if (newValue.Id_Shiire > 0) {
			_ = CacheShiireLeadTimeAsync(newValue.Id_Shiire);
		}
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		// Tax1/2/3 は UpdateHeaderTotals の出力であって入力ではない。監視すると自己再入になるため含めない。
		if (e.PropertyName is nameof(Tran13Hachu.Kubun) or nameof(Tran13Hachu.TaxRounding)) {
			UpdateHeaderTotals();
		}
		else if (e.PropertyName == nameof(Tran13Hachu.DenDay)) {
			RecalcNouhinDay();
			// 発注日が変われば適用税率が変わるため明細全行を引き直す
			_ = RecalcAllMeisaiTaxAsync();
		}
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
		// 発注はTaxCalcUnitを持たない(常に伝票単位)。消費税は税区分ごとに1回だけ丸める(TaxCalculator.Apply)。
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
		OnPropertyChanged(nameof(TaxTotal));
	}

	/// <summary>Tax1+Tax2+Tax3。Tax は分割済みで存在しないため、XAMLの消費税欄表示はこちらを使う。</summary>
	public long TaxTotal => CurrentEdit.Tax1 + CurrentEdit.Tax2 + CurrentEdit.Tax3;

	async Task ApplyDefaultSokoAsync() {
		if (CurrentEdit.Id_Soko > 0) return;
		var sysman = await AppGlobal.LogicGetSysman();
		if (sysman.Id_Soko <= 0) return;
		CurrentEdit.Id_Soko = sysman.Id_Soko;
		CurrentEdit.VSoko = new CodeNameView { Sid = sysman.VSoko.Sid, Cd = sysman.VSoko.Cd, Mei = sysman.VSoko.Mei };
	}

	async Task CacheShiireLeadTimeAsync(long idShiire) {
		var shiire = await LoadFullShiireAsync(idShiire);
		if (shiire != null) shiireLeadTimeDays = shiire.LeadTimeDays;
	}

	// 選択ダイアログ(QueryListSimpleParam)は Id/Code/Name しか返さないため、掛率・リードタイムはIdで1件取得し直す。
	async Task<MasterShiire?> LoadFullShiireAsync(long idShiire) =>
		await AppGlobal.LogicGetMasterById<MasterShiire>(idShiire);

	// 発注日 + 仕入先のリードタイム日数で納品予定日を再計算する（常に上書き）。
	void RecalcNouhinDay() {
		if (!DateTime.TryParseExact(CurrentEdit.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var denDay)) return;
		CurrentEdit.NouhinDay = denDay.AddDays(shiireLeadTimeDays).ToString("yyyyMMdd");
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
	void GoToDetail(Tran13Hachu? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id <= 0) {
			Current = CreateNewDenpyo();
		}
		SelectedTabIndex = 1;
	}

	/// <summary>新規伝票の既定値。</summary>
	protected virtual Tran13Hachu CreateNewDenpyo() => new() {
		DenDay = DateTime.Now.ToString("yyyyMMdd"),
		Kubun = (int)EnumHachu.Hachu,
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
		await RunPrintPdfAsync("HachuInput_header.qfm", null, new QueryListSqlParam(typeof(Tran13Hachu), BuildListPrintSql(query), query.Parameters), ct);
	}

	[RelayCommand(CanExecute = nameof(IsListTabSelected), IncludeCancelCommand = true)]
	async Task DoPrintDetail(CancellationToken ct) {
		var query = CreateListQueryParam();
		await RunPrintPdfAsync("HachuInput_detail.qfm", null, new QueryListSqlParam(typeof(Tran13Hachu), BuildDetailPrintSql(query), query.Parameters), ct);
	}

	// 帳票CSVは旧cvnet(SubDIgInp13.crs)のSELECT列順をそのまま踏襲する。
	// 列順は printform/HachuInput_header.qfm(39列) / HachuInput_detail.qfm(68列) の item1..itemN と 1:1 で対応する。
	// PrintPdfService は SQL の結果列名をそのまま Dictionary のキーにするため、同名列があるとCSV化で壊れる。
	// そのため全SELECT列に `as itemN` を付け、列順・一意性を固定している。
	// 旧cvnetにあってCV10に存在しない項目（手入力伝票NO・関連No2・SYSFLG・送信FLG・連携など）は空欄で出す。

	/// <summary>伝票処理区分。旧cvnet では発注は 13 固定。</summary>
	const int DenpyoShoriKubun = 13;

	/// <summary>掛計上日の未設定値。qfm 側スクリプトはこの値のとき掛計上日列を非表示にする。</summary>
	const string KakeDayNone = "19010101";

	/// <summary>区分名（発注/返品 など）。<paramref name="p"/> はテーブル別名（"" または "h."）。</summary>
	static string KubunNameSql(string p) =>
		$"case {p}Kubun when 10 then '発注' when 11 then '追加発注' when 15 then '自動発注' when 20 then '返品' when 30 then '値引' when 99 then 'その他' else cast({p}Kubun as text) end";

	/// <summary>旧帳票の「取引区分名」= 「区分コード 区分名」形式。</summary>
	static string KubunLabelSql(string p) => $"(cast({p}Kubun as text) || ' ' || {KubunNameSql(p)})";

	/// <summary>V*列(CodeNameView JSON)のコード。</summary>
	static string VCd(string column) => $"ifnull(json_extract({column},'$.Cd'),'')";

	/// <summary>V*列(CodeNameView JSON)の名称。</summary>
	static string VMei(string column) => $"ifnull(json_extract({column},'$.Mei'),'')";

	/// <summary>仕入区分（MasterShohin.PurchaseType）を「コード 名称」で出す。</summary>
	const string ShiireKubunLabelSql =
		"case s.PurchaseType when 0 then '0 通常仕入' when 3 then '3 消化仕入' else ifnull(cast(s.PurchaseType as text),'') end";

	static string BuildListPrintSql(QueryListParam query) {
		return $@"
select
Id as item1,
'' as item2,
'' as item3,
'発注伝票一覧' as item4,
'' as item5,
DenDay as item6,
ifnull(NouhinDay,'') as item7,
Kubun as item8,
{VCd("VShain")} as item9,
{VCd("VSoko")} as item10,
{VCd("VShiire")} as item11,
Rate as item12,
'' as item13,
SuTotal as item14,
KingakuTotal as item15,
'' as item16,
(Tax1+Tax2+Tax3) as item17,
JodaiTotal as item18,
GedaiTotal as item19,
ifnull(Memo,'') as item20,
'' as item21,
{DenpyoShoriKubun} as item22,
'' as item23,
{VCd("VSoko")} as item24,
RelateNo1 as item25,
'{KakeDayNone}' as item26,
'' as item27,
'' as item28,
{VMei("VShain")} as item29,
{VMei("VShiire")} as item30,
{VMei("VSoko")} as item31,
'' as item32,
'' as item33,
TaxRounding as item34,
'' as item35,
'' as item36,
{KubunLabelSql("")} as item37,
'発注' as item38,
'' as item39
from Tran13Hachu {query.AddWhereOrder()}
";
	}

	static string BuildDetailPrintSql(QueryListParam query) {
		var denpyoSub = $"select * from Tran13Hachu {query.AddWhereOrder()}";
		const string M = "json_extract(m.value,";
		return $@"
select
h.Id as item1,
'' as item2,
'' as item3,
'発注伝票明細' as item4,
'' as item5,
h.DenDay as item6,
ifnull(h.NouhinDay,'') as item7,
h.Kubun as item8,
{VCd("h.VShain")} as item9,
{VCd("h.VSoko")} as item10,
{VCd("h.VShiire")} as item11,
h.Rate as item12,
'' as item13,
h.SuTotal as item14,
h.KingakuTotal as item15,
'' as item16,
(h.Tax1+h.Tax2+h.Tax3) as item17,
h.JodaiTotal as item18,
h.GedaiTotal as item19,
ifnull(h.Memo,'') as item20,
'' as item21,
{DenpyoShoriKubun} as item22,
'' as item23,
{VCd("h.VSoko")} as item24,
h.RelateNo1 as item25,
'{KakeDayNone}' as item26,
'' as item27,
'' as item28,
{VMei("h.VShain")} as item29,
{VMei("h.VShiire")} as item30,
{VMei("h.VSoko")} as item31,
'' as item32,
'' as item33,
h.TaxRounding as item34,
'' as item35,
'' as item36,
ifnull({M}'$.Kubun'),0) as item37,
ifnull({M}'$.Code_Shohin'),'') as item38,
ifnull({M}'$.Code_Col'),'') as item39,
ifnull({M}'$.Code_Siz'),'') as item40,
ifnull({M}'$.Mei_Shohin'),'') as item41,
ifnull({M}'$.Su'),0) as item42,
ifnull({M}'$.Tanka'),0) as item43,
ifnull({M}'$.Kingaku'),0) as item44,
'' as item45,
ifnull({M}'$.Tax'),0) as item46,
ifnull({M}'$.Jodai'),0) as item47,
(cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Jodai'),0) as int)) as item48,
ifnull({M}'$.Gedai'),0) as item49,
(cast(ifnull({M}'$.Su'),0) as int) * cast(ifnull({M}'$.Gedai'),0) as int)) as item50,
ifnull({M}'$.Memo'),'') as item51,
'' as item52,
'' as item53,
'' as item54,
'' as item55,
ifnull({M}'$.JanCode'),'') as item56,
'' as item57,
h.EndFlag as item58,
'' as item59,
ifnull({M}'$.Mei_Col'),'') as item60,
ifnull({M}'$.Mei_Siz'),'') as item61,
{KubunLabelSql("h.")} as item62,
cast(ifnull({M}'$.No'),0) as int) as item63,
case h.EndFlag when 1 then '完了' else '未完' end as item64,
ifnull(s.MakerHin,'') as item65,
{ShiireKubunLabelSql} as item66,
'発注' as item67,
'' as item68
from ({denpyoSub}) h
cross join json_each(h.Jmeisai) m
left join MasterShohin s on s.Id = cast(ifnull({M}'$.Id_Shohin'),0) as int)
order by h.DenDay desc, h.Id desc, cast({M}'$.No') as int)
";
	}

	// 基底フック: 発注明細の新規行は P プロパー区分で作る。
	protected override Tran99Meisai CreateNewMeisai(int no) => new() { No = no, Kubun = ProperMeisaiKubun };

	[RelayCommand]
	async Task DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		// 上代一括変更の適用価格を引くための対象軸（発注は得意先が特定できないので本部売上用の全件行・伝票日付）
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

		// 選択ダイアログはCode/Nameしか返さないため、掛率・リードタイム・端数処理はIdで1件取得し直す。
		var fullShiire = await LoadFullShiireAsync(shiire.Id);
		if (fullShiire == null) {
			// 仕入先が引けない場合は自社既定の端数処理を使う(3.7の解決順3)
			CurrentEdit.TaxRounding = (await AppGlobal.LogicGetSysman()).TaxRounding;
			UpdateHeaderTotals();
			return;
		}
		shiireLeadTimeDays = fullShiire.LeadTimeDays;
		CurrentEdit.Rate = fullShiire.RateProper;
		// 発注はTaxCalcUnitを持たず常に伝票単位。端数処理は伝票作成時点のマスタ値をスナップショットする
		// (Doc/spec/2026-09-01 2.2)。既存伝票の読込時は上書きしない(このコマンドは仕入先を選び直したときにしか呼ばれない)。
		CurrentEdit.TaxRounding = fullShiire.TaxRounding;
		RecalcNouhinDay();
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
		// 上代一括変更の適用価格を引くための対象軸（発注は得意先が特定できないので本部売上用の全件行・伝票日付）
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

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (発注No={CurrentEdit.Id})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (発注No={CurrentEdit.Id})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (発注No={CurrentEdit.Id})";
}
