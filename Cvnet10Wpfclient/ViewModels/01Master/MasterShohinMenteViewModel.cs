using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using Cvnet10Wpfclient.ViewModels.Sub;
using System.Collections;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterShohinMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShohin> {
	[ObservableProperty]
	string title = "商品マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["VBrand"];

	protected override string? SelectCodeDisplayName => "商品";

	[ObservableProperty]
	MasterShohinColSiz? selectedJcolsiz;

	[ObservableProperty]
	MasterShohinGenka? selectedJgenka;

	[ObservableProperty]
	MasterShohinGrade? selectedJgrade;

	[ObservableProperty]
	MasterGeneralMeisho? selectedJsub;

	[ObservableProperty]
	int interactionTriggersCount;

	[ObservableProperty]
	ObservableCollection<MasterShohinGenka> editJgenka = [];

	[ObservableProperty]
	ObservableCollection<MasterShohinColSiz> editJcolsiz = [];

	[ObservableProperty]
	ObservableCollection<MasterShohinGrade> editJgrade = [];

	[ObservableProperty]
	ObservableCollection<MasterGeneralMeisho> editJsub = [];

	public ObservableCollection<string> KubunOptions { get; } = new([
		"B01", "B02", "B03", "B04", "B05",
		"B06", "B07", "B08", "B09", "B10"
	]);
	public List<MasterMeisho> KubunList = [];


	protected override void OnCurrentEditChangedCore(MasterShohin? oldValue, MasterShohin newValue) {
		if (newValue == null) return;
		ApplySubListsFromCurrentEdit();
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
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
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
		EditJgenka.Add(new MasterShohinGenka { No = nextNo });
	}

	[RelayCommand]
	void DeleteJgenka() {
		if (SelectedJgenka == null) return;
		EditJgenka.Remove(SelectedJgenka);
		SelectedJgenka = null;
	}

	[RelayCommand]
	void AddJcolsiz() {
		EditJcolsiz.Add(new MasterShohinColSiz());
	}

	[RelayCommand]
	void DeleteJcolsiz() {
		if (SelectedJcolsiz == null) return;
		EditJcolsiz.Remove(SelectedJcolsiz);
		SelectedJcolsiz = null;
	}

	[RelayCommand]
	void AddJgrade() {
		var nextNo = EditJgrade.Count > 0 ? EditJgrade.Max(x => x.No) + 1 : 1;
		EditJgrade.Add(new MasterShohinGrade { No = nextNo });
	}

	[RelayCommand]
	void DeleteJgrade() {
		if (SelectedJgrade == null) return;
		EditJgrade.Remove(SelectedJgrade);
		SelectedJgrade = null;
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
	}

	[RelayCommand]
	void DeleteJsub() {
		if (SelectedJsub == null) return;
		EditJsub.Remove(SelectedJsub);
		SelectedJsub = null;
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
