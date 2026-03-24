using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewModels.Sub;
using Cvnet10Wpfclient.ViewServices;
using System.Collections;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterShohinMenteViewModel : Helpers.BaseMenteViewModel<MasterShohin> {
	[ObservableProperty]
	string title = "商品マスターメンテ";

	protected override string? ListWhere => BuildSelectCodeWhere(selectCodeParam);
	protected override string? ListOrder => "Code";

	SelectParameter? selectCodeParam;

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

	public ObservableCollection<string> KubunOptions { get; } = new([
		"B01", "B02", "B03", "B04", "B05",
		"B06", "B07", "B08", "B09", "B10"
	]);
	public List<MasterMeisho> KubunList = [];

	protected override int? ListMaxCount => selectCodeParam?.MaxCount;


	protected override void OnCurrentEditChangedCore(MasterShohin? oldValue, MasterShohin newValue) {
		if (newValue == null) return;
		if (newValue.Jsub != null) {
			foreach (var item in newValue.Jsub) item.BaseList = KubunList;
		}
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


	protected override string GetInsertConfirmMessage() =>
		$"追加しますか？ (CD={CurrentEdit.Code})";

	protected override string GetUpdateConfirmMessage() =>
		$"修正しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() =>
		$"削除しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})";

	protected override void AfterInsert(MasterShohin item) {
		Message = $"追加しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterUpdate(MasterShohin item) {
		Message = $"修正しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterDelete(MasterShohin removedItem) {
		Message = $"削除しました (CD={removedItem.Code}, Id={removedItem.Id})";
	}

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		if (!TryShowSelectCodeDialog(selectCodeParam, "商品", out var parameter)) {
			return new ValueTask<bool>(false);
		}

		selectCodeParam = parameter;
		return new ValueTask<bool>(true);
	}
	[RelayCommand]
	void DoSelectBrand() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='BRD'", "Code", startPos: CurrentEdit.Id_Brand);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		CurrentEdit.Id_Brand = meisho?.Id ?? 0;
		CurrentEdit.VBrand = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectItem() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='ITM'", "Code", startPos: CurrentEdit.Id_Item);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		CurrentEdit.Id_Item = meisho?.Id ?? 0;
		CurrentEdit.VItem = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectMaker() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='MKR'", "Code", startPos: CurrentEdit.Id_Maker);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		CurrentEdit.Id_Maker = meisho?.Id ?? 0;
		CurrentEdit.VMaker = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
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
		CurrentEdit.SizeKu = meisho?.Code ?? "";
	}

	[RelayCommand]
	void DoSelectSoko() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterTokui), "TenType=0", "Code", startPos: CurrentEdit.Id_Soko);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var tokui = vm.Current as MasterTokui;
		if (tokui == null) return;
		CurrentEdit.Id_Soko = tokui?.Id ?? 0;
		CurrentEdit.VSoko = new() { Sid = tokui?.Id ?? 0, Cd = tokui?.Code ?? "", Mei = tokui?.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectCol(long? id) {

		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='COL'", "Code", startPos: SelectedJcolsiz?.Id_Col ?? 0);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null || SelectedJcolsiz == null) return;
		SelectedJcolsiz.Id_Col = meisho.Id;
		SelectedJcolsiz.Code_Col = meisho.Code ?? "";
		SelectedJcolsiz.Mei_Col = meisho.Name ?? "";
	}

	[RelayCommand]
	void DoSelectSiz(long? id) {

		var sizeKu = (CurrentEdit.SizeKu ?? string.Empty).Replace("'", "''");
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), $"Kubun='{sizeKu}'", "Code", startPos: SelectedJcolsiz?.Id_Siz ?? 0);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null || SelectedJcolsiz == null) return;
		SelectedJcolsiz.Id_Siz = meisho.Id;
		SelectedJcolsiz.Code_Siz = meisho.Code ?? "";
		SelectedJcolsiz.Mei_Siz = meisho.Name ?? "";
	}

	[RelayCommand]
	void AddJgenka() {
		CurrentEdit.Jgenka ??= [];
		var nextNo = CurrentEdit.Jgenka.Count > 0 ? CurrentEdit.Jgenka.Max(x => x.No) + 1 : 1;
		CurrentEdit.Jgenka.Add(new MasterShohinGenka { No = nextNo });
	}

	[RelayCommand]
	void DeleteJgenka() {
		if (SelectedJgenka == null || CurrentEdit.Jgenka == null) return;
		CurrentEdit.Jgenka.Remove(SelectedJgenka);
		SelectedJgenka = null;
	}

	[RelayCommand]
	void AddJcolsiz() {
		CurrentEdit.Jcolsiz ??= [];
		CurrentEdit.Jcolsiz.Add(new MasterShohinColSiz());
	}

	[RelayCommand]
	void DeleteJcolsiz() {
		if (SelectedJcolsiz == null || CurrentEdit.Jcolsiz == null) return;
		CurrentEdit.Jcolsiz.Remove(SelectedJcolsiz);
		SelectedJcolsiz = null;
	}

	[RelayCommand]
	void AddJgrade() {
		CurrentEdit.Jgrade ??= [];
		var nextNo = CurrentEdit.Jgrade.Count > 0 ? CurrentEdit.Jgrade.Max(x => x.No) + 1 : 1;
		CurrentEdit.Jgrade.Add(new MasterShohinGrade { No = nextNo });
	}

	[RelayCommand]
	void DeleteJgrade() {
		if (SelectedJgrade == null || CurrentEdit.Jgrade == null) return;
		CurrentEdit.Jgrade.Remove(SelectedJgrade);
		SelectedJgrade = null;
	}

	[RelayCommand]
	void DoSelectHinshitu() {
		if (SelectedJgrade == null) return;
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='HIN'", "Code", startPos: 0);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		SelectedJgrade.Hinshitu = meisho.Name ?? "";
	}

	[RelayCommand]
	void AddJsub() {
		CurrentEdit.Jsub ??= [];
		var newItem = new MasterGeneralMeisho();
		CurrentEdit.Jsub.Add(newItem);
		SortJsub();
	}

	[RelayCommand]
	void DeleteJsub() {
		if (SelectedJsub == null || CurrentEdit.Jsub == null) return;
		CurrentEdit.Jsub.Remove(SelectedJsub);
		SelectedJsub = null;
	}

	[RelayCommand]
	void DoSelectJsubCode() {
		if (SelectedJsub == null) return;
		var kb = (SelectedJsub.Kb ?? string.Empty).Replace("'", "''");
		if (string.IsNullOrEmpty(kb)) return;
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), $"Kubun='{kb}'", "Code", startPos: SelectedJsub.Sid);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		SelectedJsub.Cd = meisho.Code ?? "";
		SelectedJsub.Mei = meisho.Name ?? "";
	}

	void SortJsub() {
		if (CurrentEdit.Jsub == null) return;
		var sorted = CurrentEdit.Jsub.OrderBy(x => x.Kb).ToList();
		CurrentEdit.Jsub.Clear();
		foreach (var item in sorted) CurrentEdit.Jsub.Add(item);
	}

}
