using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterShohinMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShohin> {
	long selectedShohinIdAfterList;

	[ObservableProperty]
	public partial string Title { get; set; } = "商品マスターメンテ";

	[ObservableProperty]
	public partial Uri? ShohinImageUri { get; set; }

	[ObservableProperty]
	public partial string ShohinImageStatusText { get; set; } = "画像なし";

	protected override string[] AdditionalLightweightColumns => ["VBrand"];
	protected override string? SelectCodeDisplayName => "商品";
	protected override string? FormFile => "MasterShohinMente.qfm";
	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = @$"
with TargetShohin as (
	select *
	from MasterShohin {query.AddWhereOrder()}
)
select M.Id, __serverdate__(M.Vdc) Vdcdate, __serverdate__(M.Vdu) Vdudate,
M.Code, M.Name, M.Ryaku, M.Kana,
trim(ifnull(json_extract(M.VBrand,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VBrand,'$.Mei'),'')) Brand,
trim(ifnull(json_extract(M.VItem,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VItem,'$.Mei'),'')) Item,
trim(ifnull(json_extract(M.VMaker,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VMaker,'$.Mei'),'')) Maker,
trim(ifnull(json_extract(M.VSeason,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VSeason,'$.Mei'),'')) Season,
trim(ifnull(json_extract(M.VMaterial,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VMaterial,'$.Mei'),'')) Material,
trim(ifnull(json_extract(M.VCountry,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VCountry,'$.Mei'),'')) Country,
trim(ifnull(json_extract(M.VSoko,'$.Cd'),'') || ' ' || ifnull(json_extract(M.VSoko,'$.Mei'),'')) Soko,
M.TankaJodaiOrg, M.TankaJodai, M.TankaGenka, M.TankaShiire,
M.MakerHin, M.DayShukka, M.DayNohin, M.DayTento, M.SizeKu,
case M.IsZaiko when 1 then 'する' else 'しない' end IsZaikoText,
M.Memo,
__serverimg__(M.Code) ImagePath,
trim(ifnull(json_extract(J.value,'$.Code_Col'),'') || ' ' || ifnull(json_extract(J.value,'$.Mei_Col'),'')) Color,
trim(ifnull(json_extract(J.value,'$.Code_Siz'),'') || ' ' || ifnull(json_extract(J.value,'$.Mei_Siz'),'')) Size,
ifnull(json_extract(J.value,'$.Jan1'), '') Jan1,
ifnull(json_extract(J.value,'$.Jan2'), '') Jan2,
ifnull(json_extract(J.value,'$.Jan3'), '') Jan3
from TargetShohin M, json_each(M.Jcolsiz) J
";
			return new QueryListSqlParam(typeof(MasterShohin), sql, query.Parameters);
		}
	}

	protected override bool TryShowSelectCodeDialog(SelectParameter? currentParameter, string displayName, out SelectParameter parameter) {
		var selWin = new Views.Sub.SelectShohinView();
		if (selWin.DataContext is not SelectShohinViewModel vm) {
			parameter = currentParameter ?? new SelectParameter { DisplayName = displayName, IdsDisplayName = "ブランド" };
			return true;
		}

		var initialParameter = (currentParameter ?? new SelectParameter { MaxCount = AppGlobal.Limit }) with {
			DisplayName = displayName,
			IdsDisplayName = "ブランド"
		};
		vm.IsConditionOnlyMode = true;
		vm.ApplySelectParameter(initialParameter);
		if (ClientLib.ShowDialogView(selWin, this, true) != true) {
			parameter = currentParameter ?? initialParameter;
			return false;
		}

		selectedShohinIdAfterList = vm.SelectedShohin?.Id ?? 0;
		parameter = NormalizeSelectParameter(vm.CreateSelectParameter(displayName), displayName) with {
			IdsDisplayName = "ブランド"
		};
		return true;
	}

	protected override string? BuildSelectCodeWhere(SelectParameter? parameter) {
		if (parameter == null) {
			return null;
		}

		List<string> clauses = [];
		List<string> parameters = [];
		AddSelectedIdInClause(clauses, "Id_Brand", parameter.Ids);
		AddSelectedIdInClause(clauses, "Id_Item", parameter.ItemIds);
		if (parameter.FromId.HasValue) {
			clauses.Add($"Id >= {parameter.FromId.Value}");
		}
		if (parameter.ToId.HasValue) {
			clauses.Add($"Id <= {parameter.ToId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.FromCode)) {
			clauses.Add($"Code >= {AddSqlParameter(parameters, parameter.FromCode.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.ToCode)) {
			clauses.Add($"Code <= {AddSqlParameter(parameters, parameter.ToCode.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.Name)) {
			clauses.Add($"Name LIKE {AddSqlParameter(parameters, $"%{EscapeSqlLikePattern(parameter.Name)}%")} ESCAPE '\\'");
		}
		if (!string.IsNullOrWhiteSpace(parameter.Ryaku)) {
			clauses.Add($"Ryaku LIKE {AddSqlParameter(parameters, $"%{EscapeSqlLikePattern(parameter.Ryaku)}%")} ESCAPE '\\'");
		}
		if (!string.IsNullOrWhiteSpace(parameter.Kana)) {
			clauses.Add($"Kana LIKE {AddSqlParameter(parameters, $"%{EscapeSqlLikePattern(parameter.Kana)}%")} ESCAPE '\\'");
		}
		if (!string.IsNullOrWhiteSpace(parameter.Jan)) {
			string janParameter = AddSqlParameter(parameters, $"%{EscapeSqlLikePattern(parameter.Jan)}%");
			clauses.Add($"""
				EXISTS (
					SELECT 1
					FROM DerivedShohinColSiz D
					WHERE D.Id_Shohin = MasterShohin.Id
						AND (D.Jan1 LIKE {janParameter} ESCAPE '\' OR D.Jan2 LIKE {janParameter} ESCAPE '\' OR D.Jan3 LIKE {janParameter} ESCAPE '\')
				)
				""");
		}

		SelectCodeWhereParameters = [.. parameters];
		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	protected override void AfterList(IList list) {
		if (selectedShohinIdAfterList <= 0) return;

		var selected = ListData.FirstOrDefault(x => x.Id == selectedShohinIdAfterList);
		selectedShohinIdAfterList = 0;
		if (selected != null) {
			Current = selected;
		}
	}

	[ObservableProperty]
	public partial MasterShohinColSiz? SelectedJcolsiz { get; set; }

	[ObservableProperty]
	public partial MasterShohinGenka? SelectedJgenka { get; set; }

	[ObservableProperty]
	public partial MasterShohinGrade? SelectedJgrade { get; set; }

	[ObservableProperty]
	public partial MasterGeneralMeisho? SelectedJsub { get; set; }

	[ObservableProperty]
	public partial int InteractionTriggersCount { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<MasterShohinGenka> EditJgenka { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterShohinColSiz> EditJcolsiz { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterShohinGrade> EditJgrade { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterGeneralMeisho> EditJsub { get; set; } = [];

	public ObservableCollection<string> KubunOptions { get; } = new(Enumerable.Range(1, 10).Select(i => $"{MasterMeisho.KubunTopShohin}{i:D2}"));
	public List<MasterMeisho> KubunList = [];

	/// <summary>
	/// 消費税区分(<see cref="MasterShohin.Id_Tax"/>)の選択肢。
	/// <para>
	/// <see cref="MasterSysTax"/> の定義から作り、税率を併記して運用時の取り違えを防ぐ。
	/// Id=0 は非課税で、明細税額の計算をしないことを表す。
	/// </para>
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<TaxKubunOption> TaxKubunList { get; set; } = [];

	/// <summary>消費税区分コンボの表示項目</summary>
	public sealed record TaxKubunOption(long Id, string Name);

	/// <summary>区分値コンボの共通表示項目（原価4項目 詳細設計 §2.5.8の各区分値）。</summary>
	public sealed record CodeOption(int Value, string Name);

	/// <summary>
	/// 仕入区分(<see cref="EnumPurchaseType"/>)の選択肢（原価4項目 詳細設計 §2.5.8）。
	/// </summary>
	public IReadOnlyList<CodeOption> PurchaseTypeOptions { get; } = [
		new((int)EnumPurchaseType.Normal, "通常仕入"),
		new((int)EnumPurchaseType.Consumption, "消化仕入"),
	];

	/// <summary>消化仕入計算区分(<see cref="EnumConsumptionCalcType"/>)の選択肢（§2.5.8・§4.4）。</summary>
	public IReadOnlyList<CodeOption> ConsumptionCalcTypeOptions { get; } = [
		new((int)EnumConsumptionCalcType.CostBased, "原価代用"),
		new((int)EnumConsumptionCalcType.RateBased, "上代×掛率"),
	];

	/// <summary>消化仕入の端数単位の選択肢（§2.5.8「1、10、100、1000円のみ」）。</summary>
	public IReadOnlyList<CodeOption> ConsumptionRoundingUnitOptions { get; } = [
		new(1, "1円"),
		new(10, "10円"),
		new(100, "100円"),
		new(1000, "1000円"),
	];

	/// <summary>消化仕入の端数処理(<see cref="EnumRounding"/>)の選択肢（§2.5.8）。</summary>
	public IReadOnlyList<CodeOption> ConsumptionRoundingOptions { get; } = [
		new((int)EnumRounding.Round, "四捨五入"),
		new((int)EnumRounding.Ceiling, "切上"),
		new((int)EnumRounding.Floor, "切捨"),
	];

	/// <summary>
	/// 仕入区分が消化仕入(<see cref="EnumPurchaseType.Consumption"/>)かどうか。
	/// 消化仕入設定タブの委託仕入先以下の入力欄を、通常仕入時にグレーアウトして誤入力を防ぐために使う
	/// (§2.5.8「PurchaseType=0の場合、他の消化仕入用列は計算に使用しない」)。値自体は消さずに保持する。
	/// </summary>
	public bool IsConsumptionPurchase => CurrentEdit.PurchaseType == (int)EnumPurchaseType.Consumption;

	/// <summary>
	/// 原価履歴(<see cref="TranGenka"/>)。商品マスタから参照専用で表示する（原価4項目 詳細設計 §2.6・§9.4）。
	/// 旧 <see cref="MasterShohin.Jgenka"/>（旧HC$MASTER_SHOHIN_GENKAの移行保持データ）とは別物であり、
	/// 「原価履歴」タブで明確に区別して表示する。専用APIは設けず既存のSQL照会経路で取得する。
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<CostGenkaHistoryRow> CostGenkaHistory { get; set; } = [];

	/// <summary>原価履歴グリッド1行の表示用DTO。列は原価4項目 詳細設計 §9.4・§16.11(ChangeKind)を参照する。</summary>
	public sealed record CostGenkaHistoryRow(
		string SumMonthText,
		string EffectiveDay,
		string CostMethodText,
		string ChangeKindText,
		int BeforeCost,
		int AfterCost,
		long OpeningQty,
		long OpeningAmount,
		long PurchaseQty,
		long PurchaseAmount,
		long SundryAmount,
		long Vdu,
		CodeNameView VShain);

	protected override void OnCurrentEditChangedCore(MasterShohin? oldValue, MasterShohin newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) {
			ShohinImageUri = null;
			ShohinImageStatusText = "画像なし";
			return;
		}
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;

		ApplySubListsFromCurrentEdit();
		CostGenkaHistory = [];
		OnPropertyChanged(nameof(IsConsumptionPurchase));

		var code = newValue.Code?.Trim();
		if (string.IsNullOrWhiteSpace(code)) {
			ShohinImageUri = null;
			ShohinImageStatusText = "画像なし";
		}
		else {
			ShohinImageUri = new Uri($"{AppGlobal.Url.TrimEnd('/')}/img/{Uri.EscapeDataString(code)}.jpg");
			ShohinImageStatusText = string.Empty;
		}
	}

	void OnCurrentEditPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(MasterShohin.PurchaseType)) {
			OnPropertyChanged(nameof(IsConsumptionPurchase));
		}
	}

	void ApplySubListsFromCurrentEdit() {
		EditJgenka = new ObservableCollection<MasterShohinGenka>(
			CurrentEdit.Jgenka?.Select(Common.CloneObject) ?? []);

		EditJcolsiz = new ObservableCollection<MasterShohinColSiz>(
			CurrentEdit.Jcolsiz?.Select(Common.CloneObject) ?? []);

		EditJgrade = new ObservableCollection<MasterShohinGrade>(
			CurrentEdit.Jgrade?.Select(Common.CloneObject) ?? []);

		var jsubClones = (CurrentEdit.Jsub?.Select(Common.CloneObject) ?? []).ToList();
		foreach (var item in jsubClones) item.SetBaseList(KubunList);
		EditJsub = new ObservableCollection<MasterGeneralMeisho>(jsubClones);
	}

	void SyncSubListsToCurrentEdit() {
		CurrentEdit.Jgenka = [.. EditJgenka];
		CurrentEdit.Jcolsiz = [.. EditJcolsiz];
		CurrentEdit.Jgrade = [.. EditJgrade];
		CurrentEdit.Jsub = [.. EditJsub];
	}

	protected override object CreateInsertParam() {
		SyncSubListsToCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		SyncSubListsToCurrentEdit();
		return base.CreateUpdateParam();
	}

	[RelayCommand]
	async Task Init() {
		await DoGetKubun(CancellationToken.None);
		await LoadTaxKubunAsync();
		await DoList(CancellationToken.None);
	}

	/// <summary>
	/// 消費税区分の選択肢を <see cref="MasterSysTax"/> から作る。
	/// 表示税率は「今日時点で適用される率」で、伝票の税額は伝票日付時点の率で別途計算される。
	/// </summary>
	async Task LoadTaxKubunAsync() {
		if (TaxKubunList.Count > 0) return;
		var today = DateTime.Now.ToString("yyyyMMdd");
		var options = new List<TaxKubunOption> { new(0, "0: 非課税") };
		var sysman = await AppGlobal.LogicGetSysman();
		foreach (var systax in (sysman.Jsub ?? []).OrderBy(x => x.Id)) {
			var rate = await AppGlobal.LogicGetTax((int)systax.Id, today);
			options.Add(new TaxKubunOption(systax.Id, $"{systax.Id}: {rate}%"));
		}
		TaxKubunList = new ObservableCollection<TaxKubunOption>(options);
	}

	async Task DoGetKubun(CancellationToken ct) {
		if (KubunList.Count > 0) return;
		try {
			ClientLib.Cursor2Wait();
			var param = new QueryListParam(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunIndex}' and Code between '{MasterMeisho.KubunTopShohin}01' and '{MasterMeisho.KubunTopShohin}10'", "Code");
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(param)
			};
			var reply = await SendMessageAsync(msg, ct);
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				KubunList.Clear();
				foreach (var item in list.Cast<MasterMeisho>()) KubunList.Add(item);
			}
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	void DoSelectBrand() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunBrand}'", "Code", startPos: CurrentEdit.Id_Brand);
		if (meisho == null) return;
		CurrentEdit.Id_Brand = meisho.Id;
		CurrentEdit.VBrand = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectItem() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunItem}'", "Code", startPos: CurrentEdit.Id_Item);
		if (meisho == null) return;
		CurrentEdit.Id_Item = meisho.Id;
		CurrentEdit.VItem = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectMaker() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunMaker}'", "Code", startPos: CurrentEdit.Id_Maker);
		if (meisho == null) return;
		CurrentEdit.Id_Maker = meisho.Id;
		CurrentEdit.VMaker = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectTenji() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunTenji}'", "Code", startPos: CurrentEdit.Id_Tenji);
		if (meisho == null) return;
		CurrentEdit.Id_Tenji = meisho.Id;
		CurrentEdit.VTenji = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectSeason() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunSeason}'", "Code", startPos: CurrentEdit.Id_Season);
		if (meisho == null) return;
		CurrentEdit.Id_Season = meisho.Id;
		CurrentEdit.VSeason = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectMaterial() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunMaterial}'", "Code", startPos: CurrentEdit.Id_Material);
		if (meisho == null) return;
		CurrentEdit.Id_Material = meisho.Id;
		CurrentEdit.VMaterial = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectCountry() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunCountry}'", "Code", startPos: CurrentEdit.Id_Country);
		if (meisho == null) return;
		CurrentEdit.Id_Country = meisho.Id;
		CurrentEdit.VCountry = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectSizeKu() {
		var selWin = new Views.Sub.SelectKubunView();
		var vm = selWin.DataContext as Sub.SelectKubunViewModel;
		if (vm == null) return;
		vm.SetParam($"Kubun='{MasterMeisho.KubunIndex}' and (Code='SIZ' or Code Like 'US%')", CurrentEdit.SizeKu);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		CurrentEdit.SizeKu = meisho.Code ?? "";
	}

	[RelayCommand]
	void DoSelectSoko() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Soko);
		if (tokui == null) return;
		CurrentEdit.Id_Soko = tokui.Id;
		CurrentEdit.VSoko = new() { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}

	/// <summary>
	/// 委託仕入先の選択（原価4項目 詳細設計 §2.5.8）。<see cref="MasterShohin.VConsignmentShiire"/>は
	/// Master系のV*列で、既存の<see cref="DoSelectBrand"/>等と同じ作法(Id_*とV*を両方設定)に倣う。
	/// <see cref="MasterShohin.VConsignmentShiire"/>はマスタ改名を伝播するV*列のため、
	/// <c>MasterCascadeDb.VRules</c>登録済みの名称変更カスケードが以後の改名を追随させる。
	/// </summary>
	[RelayCommand]
	void DoSelectConsignmentShiire() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_ConsignmentShiire);
		if (shiire == null) return;
		CurrentEdit.Id_ConsignmentShiire = shiire.Id;
		CurrentEdit.VConsignmentShiire = new() { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };
	}

	/// <summary>
	/// 原価履歴(<see cref="TranGenka"/>)を選択中の商品で取得する（原価4項目 詳細設計 §2.6・§9.4）。
	/// サーバー側に専用APIは無いため、既存のSQL照会経路(<see cref="QuerySqlListAsync{TRow}"/>)を使う。
	/// 旧<see cref="MasterShohin.Jgenka"/>とは別のテーブルであり、本コマンドはTranGenkaだけを対象にする。
	/// </summary>
	[RelayCommand]
	async Task DoLoadCostHistory(CancellationToken ct) {
		if (CurrentEdit.Id <= 0) {
			CostGenkaHistory = [];
			return;
		}
		try {
			ClientLib.Cursor2Wait();
			var sql = $"select * from TranGenka where Id_Shohin={CurrentEdit.Id} order by EffectiveDay desc, SumMonth desc, ChangeKind desc, Vdu desc, Id desc";
			var rows = await QuerySqlListAsync<TranGenka>(sql, ct);
			CostGenkaHistory = new ObservableCollection<CostGenkaHistoryRow>(rows.Select(r => new CostGenkaHistoryRow(
				CostPreviewDisplay.FormatYm6ToSlash(r.SumMonth),
				r.EffectiveDay,
				CostPreviewDisplay.FormatCostMethod((EnumCostMethod)r.CostMethod),
				CostPreviewDisplay.FormatCostChangeKind((EnumCostChangeKind)r.ChangeKind),
				r.BeforeCost,
				r.AfterCost,
				r.OpeningQty,
				r.OpeningAmount,
				r.PurchaseQty,
				r.PurchaseAmount,
				r.SundryAmount,
				r.Vdu,
				r.VShain)));
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
		}
		catch (Exception ex) {
			Message = $"原価履歴取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 追加・修正の保存前に消化仕入設定を検査する（保存前・ブロック。原価4項目 詳細設計 §4.2）。
	/// <see cref="MasterTokuiMenteViewModel.ConfirmAction"/>と同じ作法で、削除確認では検査しない。
	/// </summary>
	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !ValidateConsumptionSettings()) {
			return false;
		}
		return base.ConfirmAction(message);
	}

	bool ValidateConsumptionSettings() {
		var error = CostPreviewDisplay.ValidateShohinConsumptionSettings(
			(EnumPurchaseType)CurrentEdit.PurchaseType,
			CurrentEdit.Id_ConsignmentShiire,
			(EnumConsumptionCalcType)CurrentEdit.ConsumptionCalcType,
			CurrentEdit.TankaShiire,
			CurrentEdit.TankaGenka,
			CurrentEdit.ConsumptionRateBasisPoints,
			CurrentEdit.ConsumptionRoundingUnit);
		if (error == null) return true;
		MessageEx.ShowWarningDialog(error, owner: ActiveWindow);
		return false;
	}

	[RelayCommand]
	void DoSelectCol(long? id) {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{MasterMeisho.KubunColor}'", "Code", startPos: SelectedJcolsiz?.Id_Col ?? 0);
		if (meisho == null || SelectedJcolsiz == null) return;
		SelectedJcolsiz.Id_Col = meisho.Id;
		SelectedJcolsiz.Code_Col = meisho.Code ?? "";
		SelectedJcolsiz.Mei_Col = meisho.Name ?? "";
	}

	[RelayCommand]
	void DoSelectSiz(long? id) {
		var sizeKu = (CurrentEdit.SizeKu ?? string.Empty).Replace("'", "''");
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{sizeKu}'", "Code", startPos: SelectedJcolsiz?.Id_Siz ?? 0);
		if (meisho == null || SelectedJcolsiz == null) return;
		SelectedJcolsiz.Id_Siz = meisho.Id;
		SelectedJcolsiz.Code_Siz = meisho.Code ?? "";
		SelectedJcolsiz.Mei_Siz = meisho.Name ?? "";
	}

	[RelayCommand]
	void AddJgenka() {
		var nextNo = EditJgenka.Count > 0 ? EditJgenka.Max(x => x.No) + 1 : 1;
		var newItem = new MasterShohinGenka { No = nextNo };
		EditJgenka.Add(newItem);
		SelectedJgenka = newItem;
	}

	[RelayCommand]
	void DeleteJgenka() {
		if (SelectedJgenka == null) return;
		EditJgenka.Remove(SelectedJgenka);
		SelectedJgenka = EditJgenka.LastOrDefault();
	}

	[RelayCommand]
	void AddJcolsiz() {
		var newItem = new MasterShohinColSiz();
		EditJcolsiz.Add(newItem);
		SelectedJcolsiz = newItem;
	}

	[RelayCommand]
	void DeleteJcolsiz() {
		if (SelectedJcolsiz == null) return;
		EditJcolsiz.Remove(SelectedJcolsiz);
		SelectedJcolsiz = EditJcolsiz.LastOrDefault();
	}

	[RelayCommand]
	void AddJgrade() {
		var nextNo = EditJgrade.Count > 0 ? EditJgrade.Max(x => x.No) + 1 : 1;
		var newItem = new MasterShohinGrade { No = nextNo };
		EditJgrade.Add(newItem);
		SelectedJgrade = newItem;
	}

	[RelayCommand]
	void DeleteJgrade() {
		if (SelectedJgrade == null) return;
		EditJgrade.Remove(SelectedJgrade);
		SelectedJgrade = EditJgrade.LastOrDefault();
	}

	[RelayCommand]
	void DoSelectHinshitu() {
		if (SelectedJgrade == null) return;
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='HIN'", "Code", startPos: 0);
		if (meisho == null) return;
		SelectedJgrade.Hinshitu = meisho.Name ?? "";
	}

	[RelayCommand]
	void AddJsub() {
		var newItem = new MasterGeneralMeisho (KubunList);
		EditJsub.Add(newItem);
		SortJsub();
		SelectedJsub = newItem;
	}

	[RelayCommand]
	void DeleteJsub() {
		if (SelectedJsub == null) return;
		EditJsub.Remove(SelectedJsub);
		SelectedJsub = EditJsub.LastOrDefault();
	}

	[RelayCommand]
	void DoSelectJsubCode() {
		if (SelectedJsub == null) return;
		var kb = (SelectedJsub.Kb ?? string.Empty).Replace("'", "''");
		if (string.IsNullOrEmpty(kb)) return;
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{kb}'", "Code", startPos: SelectedJsub.Sid);
		if (meisho == null) return;
		SelectedJsub.Cd = meisho.Code ?? "";
		SelectedJsub.Mei = meisho.Name ?? "";
	}

	void SortJsub() {
		var sorted = EditJsub.OrderBy(x => x.Kb).ToList();
		EditJsub.Clear();
		foreach (var item in sorted) EditJsub.Add(item);
	}

}
