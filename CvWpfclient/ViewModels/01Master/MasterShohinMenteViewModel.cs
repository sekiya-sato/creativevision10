using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
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
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Code, Name, Ryaku, Kana,
trim(ifnull(json_extract(VBrand,'$.Cd'),'') || ' ' || ifnull(json_extract(VBrand,'$.Mei'),'')) Brand,
trim(ifnull(json_extract(VItem,'$.Cd'),'') || ' ' || ifnull(json_extract(VItem,'$.Mei'),'')) Item,
trim(ifnull(json_extract(VMaker,'$.Cd'),'') || ' ' || ifnull(json_extract(VMaker,'$.Mei'),'')) Maker,
trim(ifnull(json_extract(VSeason,'$.Cd'),'') || ' ' || ifnull(json_extract(VSeason,'$.Mei'),'')) Season,
trim(ifnull(json_extract(VMaterial,'$.Cd'),'') || ' ' || ifnull(json_extract(VMaterial,'$.Mei'),'')) Material,
trim(ifnull(json_extract(VCountry,'$.Cd'),'') || ' ' || ifnull(json_extract(VCountry,'$.Mei'),'')) Country,
trim(ifnull(json_extract(VSoko,'$.Cd'),'') || ' ' || ifnull(json_extract(VSoko,'$.Mei'),'')) Soko,
TankaJodaiOrg, TankaJodai, TankaGenka, TankaShiire,
MakerHin, DayShukka, DayNohin, DayTento, SizeKu,
case IsZaiko when 1 then 'する' else 'しない' end IsZaikoText,
Memo,
__serverimg__(Code) ImagePath
from MasterShohin {query.AddWhereOrder()}
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

	public ObservableCollection<string> KubunOptions { get; } = new(Enumerable.Range(1, 10).Select(i => $"B{i:D2}"));
	public List<MasterMeisho> KubunList = [];


	protected override void OnCurrentEditChangedCore(MasterShohin? oldValue, MasterShohin newValue) {
		if (newValue == null) {
			ShohinImageUri = null;
			ShohinImageStatusText = "画像なし";
			return;
		}

		ApplySubListsFromCurrentEdit();

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

	void ApplySubListsFromCurrentEdit() {
		EditJgenka = new ObservableCollection<MasterShohinGenka>(
			CurrentEdit.Jgenka?.Select(Common.CloneObject) ?? []);

		EditJcolsiz = new ObservableCollection<MasterShohinColSiz>(
			CurrentEdit.Jcolsiz?.Select(Common.CloneObject) ?? []);

		EditJgrade = new ObservableCollection<MasterShohinGrade>(
			CurrentEdit.Jgrade?.Select(Common.CloneObject) ?? []);

		var jsubClones = (CurrentEdit.Jsub?.Select(Common.CloneObject) ?? []).ToList();
		foreach (var item in jsubClones) item.BaseList = KubunList;
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
		await DoList(CancellationToken.None);
	}

	async Task DoGetKubun(CancellationToken ct) {
		if (KubunList.Count > 0) return;
		try {
			ClientLib.Cursor2Wait();
			var param = new QueryListParam(typeof(MasterMeisho), "Kubun='IDX' and Code between 'B01' and 'B10'", "Code");
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
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code", startPos: CurrentEdit.Id_Brand);
		if (meisho == null) return;
		CurrentEdit.Id_Brand = meisho.Id;
		CurrentEdit.VBrand = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectItem() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='ITM'", "Code", startPos: CurrentEdit.Id_Item);
		if (meisho == null) return;
		CurrentEdit.Id_Item = meisho.Id;
		CurrentEdit.VItem = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectMaker() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='MKR'", "Code", startPos: CurrentEdit.Id_Maker);
		if (meisho == null) return;
		CurrentEdit.Id_Maker = meisho.Id;
		CurrentEdit.VMaker = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectSizeKu() {
		var selWin = new Views.Sub.SelectKubunView();
		var vm = selWin.DataContext as Sub.SelectKubunViewModel;
		if (vm == null) return;
		vm.SetParam("Kubun='IDX' and (Code='SIZ' or Code Like 'US%')", CurrentEdit.SizeKu);
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

	[RelayCommand]
	void DoSelectCol(long? id) {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='COL'", "Code", startPos: SelectedJcolsiz?.Id_Col ?? 0);
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
		var newItem = new MasterGeneralMeisho { BaseList = KubunList };
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
