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

namespace CvWpfclient.ViewModels._06Uriage;

public partial class ShukkaUriageInputViewModel : Helpers.BaseTranInputViewModel<Tran00Uriage>, ITranInputTab {
	public sealed record KubunOption(EnumUri00 Value, string Name);
	public sealed record YesNoOption(EnumYesNo Value, string Name);

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoListOnListTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoUpdateOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoDeleteOnDetailTabCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoInsertOnDetailTabCommand))]
	public partial int SelectedTabIndex { get; set; }

	public override string DetailStatusText => CurrentEdit.Id > 0
		? $"売上 No. {CurrentEdit.Id:N0}"
		: "新規売上";

	SelectInputParameter? selectParam;

	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumUri00.Uriage, "売上"),
		new(EnumUri00.UriSale, "セール売上"),
		new(EnumUri00.Henpin, "返品"),
		new(EnumUri00.HenSale, "セール返品"),
		new(EnumUri00.Nebiki, "値引"),
		new(EnumUri00.Other, "その他"),
	];

	public IReadOnlyList<YesNoOption> IsPayOptions { get; } = [
		new(EnumYesNo.No, "しない"),
		new(EnumYesNo.Yes, "する"),
	];

	bool IsListTabSelected() => SelectedTabIndex == 0;
	bool IsDetailTabSelected() => SelectedTabIndex == 1;

	protected override Type Tabletype => typeof(Tran00Uriage);
	protected override string? ListOrder => "DenDay desc, Id desc";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	// 一覧の消費税・総合計列に使うほか、一覧から詳細タブへ移ったときの CurrentEdit の初期値にもなる。
	// 欠けていると詳細を開いた瞬間に 0 が表示されてから正しい値へ差し替わる。
	protected override string LightweightSelectColumns =>
		"Id,Vdc,Vdu,DenDay,KakeDay,Id_Tokui,VTokui,Id_Soko,VSoko,Id_Shain,VShain,IsPay,Kubun,ManualNo,RelateNo1,RelateNo2,Rate,SuTotal,KingakuTotal,JodaiTotal,GedaiTotal"
		+ ",Tax1,Tax2,Tax3,TaxableAmount1,TaxableAmount2,TaxableAmount3,TaxCalcUnit,TaxRounding,Total";

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.RangeInputParamView();
		if (win.DataContext is not RangeInputParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new SelectInputParameter {
			DisplayName = "出荷・売上",
			ToriLabel = "得意先Id",
			IsToriVisible = true,
			ToriSearchWhere = string.Empty,
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
			AddIdInClause(clauses, "Id_Tokui", selectParam.ToriIds);
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

	protected override void OnCurrentEditChangedCore(Tran00Uriage? oldValue, Tran00Uriage newValue) {
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
		if (e.PropertyName is nameof(Tran00Uriage.Kubun)
			or nameof(Tran00Uriage.TaxCalcUnit) or nameof(Tran00Uriage.TaxRounding)) {
			UpdateHeaderTotals();
		}
		// 伝票日付が変われば適用税率が変わるため明細全行を引き直す
		else if (e.PropertyName is nameof(Tran00Uriage.DenDay)) {
			_ = RecalcAllMeisaiTaxAsync();
		}
	}

	protected override void OnTotalsUpdated() => UpdateHeaderTotals();

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
		UpdateTaxDisplay(totals);
	}

	/// <summary>Tax1+Tax2+Tax3。Tax は分割済みで存在しないため、XAMLの消費税欄表示はこちらを使う。</summary>
	public long TaxTotal => CurrentEdit.Tax1 + CurrentEdit.Tax2 + CurrentEdit.Tax3;

	/// <summary>
	/// 請求単位（<see cref="EnumTaxCalcUnit.Billing"/>）かどうか。真なら伝票の消費税は 0 が正しく、
	/// 税額は請求計算が締請求期間で1回だけ丸めて確定する（全体設計 3.4）。画面の注記の表示切替に使う。
	/// </summary>
	public bool IsBillingUnitTax => (EnumTaxCalcUnit)CurrentEdit.TaxCalcUnit == EnumTaxCalcUnit.Billing;

	/// <summary>伝票サマリーの課税対象額内訳（税区分ごと1行）。課税対象額 0 の区分は含めない。</summary>
	[ObservableProperty]
	public partial IReadOnlyList<TaxBreakdownRow> TaxBreakdown { get; set; } = [];

	void UpdateTaxDisplay(TaxTotals totals) {
		List<TaxBreakdownRow> rows = [];
		AddTaxBreakdownRow(rows, 1, totals.TaxableAmount1, totals.Tax1);
		AddTaxBreakdownRow(rows, 2, totals.TaxableAmount2, totals.Tax2);
		AddTaxBreakdownRow(rows, 3, totals.TaxableAmount3, totals.Tax3);
		TaxBreakdown = rows;
		OnPropertyChanged(nameof(TaxTotal));
		OnPropertyChanged(nameof(IsBillingUnitTax));
	}

	void AddTaxBreakdownRow(List<TaxBreakdownRow> rows, long idTax, long taxableAmount, long tax) {
		if (taxableAmount == 0) return;
		rows.Add(new TaxBreakdownRow(idTax, TaxRateOf(idTax), taxableAmount, tax));
	}

	/// <summary>伝票サマリーの課税対象額内訳1行。</summary>
	/// <param name="IdTax">消費税区分（MasterSysTax.Id 1-3）</param>
	/// <param name="RatePercent">伝票日付時点の適用税率(%)</param>
	/// <param name="TaxableAmount">課税対象額（税抜・常に正値）</param>
	/// <param name="Tax">消費税額。請求単位では 0</param>
	public sealed record TaxBreakdownRow(long IdTax, int RatePercent, long TaxableAmount, long Tax) {
		/// <summary>"10% 対象"。税率0（税区分未解決・非課税）は明示して異常に気付けるようにする</summary>
		public string Label => RatePercent > 0 ? $"{RatePercent}% 対象" : "税率未設定";

		/// <summary>請求単位は課税対象額のみ、伝票単位は "課税対象額 → 税額" を出す</summary>
		public string AmountText => Tax > 0 ? $"{TaxableAmount:N0} → {Tax:N0}" : $"{TaxableAmount:N0}";
	}

	// 基底フック: 出荷売上はヘッダ区分を明細区分に反映（ロード時は同値のため実質不変、保存前に統一）。
	protected override int ResolveMeisaiKubun(Tran99Meisai m) => CurrentEdit.Kubun;

	// 基底フック: 出荷売上は行Noを振り直さない（元伝票の行対応を維持）。
	protected override void RenumberMeisaiNo() { }

	protected override object CreateInsertParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncMeisaiToCurrentEdit();
		return base.CreateUpdateParam();
	}

	[RelayCommand]
	void GoToDetail(Tran00Uriage? item) {
		if (item != null && item.Id > 0 && !ReferenceEquals(Current, item)) Current = item;
		if (Current.Id > 0) SelectedTabIndex = 1;
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

	// 基底フック: 出荷売上の新規行はヘッダ区分で作る。
	protected override Tran99Meisai CreateNewMeisai(int no) => new() { No = no, Kubun = CurrentEdit.Kubun };

	[RelayCommand]
	async Task DoInputBarcode() {
		var win = new Views.Sub.InputBarcodeView();
		if (win.DataContext is not InputBarcodeViewModel vm) return;
		// 上代一括変更の適用価格を引くための対象軸（本部売上なので本部売上用・当該得意先・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Honbu;
		vm.JodaiTenpoId = CurrentEdit.Id_Tokui;
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
			row.Kubun = CurrentEdit.Kubun;
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
				Kubun = CurrentEdit.Kubun,
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
	async Task DoSelectTokui() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), string.Empty, "Code", startPos: CurrentEdit.Id_Tokui);
		if (tokui == null) return;
		CurrentEdit.Id_Tokui = tokui.Id;
		CurrentEdit.VTokui = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };

		// 選択ダイアログはCode/Nameしか返さないため、掛率・税設定はIdで1件取得し直す。
		var fullTokui = await AppGlobal.LogicGetMasterById<MasterTokui>(tokui.Id);
		if (fullTokui != null) {
			CurrentEdit.Rate = fullTokui.RateProper;
			// 税計算単位・消費税端数処理は伝票作成時点のマスタ値をスナップショットする(Doc/spec/2026-09-01 2.2)。
			// 既存伝票の読込時は上書きしない(このコマンドは取引先を選び直したときにしか呼ばれない)。
			CurrentEdit.TaxCalcUnit = fullTokui.TaxCalcUnit;
			CurrentEdit.TaxRounding = fullTokui.TaxRounding;
		}
		else {
			// 得意先が引けない場合は自社既定の端数処理を使う(3.7の解決順3)
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
		// 上代一括変更の適用価格を引くための対象軸（本部売上なので本部売上用・当該得意先・伝票日付）
		vm.JodaiTaishoType = (int)EnumJodaiTaisho.Honbu;
		vm.JodaiTenpoId = CurrentEdit.Id_Tokui;
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

	// G0-4.3.1: 完了済み受注に紐付く出荷売上を編集したら気付き用の警告を出す（RelateNo1 = 受注Id）。
	protected override void AfterInsert(Tran00Uriage item) {
		base.AfterInsert(item);
		_ = WarnIfLinkedZanCompletedAsync(typeof(Tran12Jyuchu), item.RelateNo1, "出荷", "受注", "受注残完了設定");
	}

	protected override void AfterUpdate(Tran00Uriage item) {
		base.AfterUpdate(item);
		_ = WarnIfLinkedZanCompletedAsync(typeof(Tran12Jyuchu), item.RelateNo1, "出荷", "受注", "受注残完了設定");
	}

	protected override void AfterDelete(Tran00Uriage removedItem) {
		base.AfterDelete(removedItem);
		_ = WarnIfLinkedZanCompletedAsync(typeof(Tran12Jyuchu), removedItem.RelateNo1, "出荷", "受注", "受注残完了設定");
	}
}
