using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewModels.Sub;
using Cvnet10Wpfclient.ViewServices;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterShohinMenteViewModel : Helpers.BaseMenteViewModel<MasterShohin> {
	[ObservableProperty]
	string title = "商品マスターメンテ";

	protected override string? ListWhere => BuildSelectCodeWhere(selectCodeParam);
	protected override string? ListOrder => "Code";

	SelectParameter? selectCodeParam;

	[ObservableProperty]
	MasterShohinColSiz? selectedJcolsiz;

	protected override int? ListMaxCount => selectCodeParam?.MaxCount;

	[RelayCommand]
	async Task Init() {
		await DoList(CancellationToken.None);
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
	void DoSelectCol(MasterShohinColSiz? target) {
		target ??= SelectedJcolsiz;
		if (target == null) return;

		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='COL'", "Code", startPos: target.Id_Col);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		target.Id_Col = meisho.Id;
		target.Code_Col = meisho.Code ?? "";
		target.Mei_Col = meisho.Name ?? "";
	}

	[RelayCommand]
	void DoSelectSiz(MasterShohinColSiz? target) {
		target ??= SelectedJcolsiz;
		if (target == null) return;

		var sizeKu = (CurrentEdit.SizeKu ?? string.Empty).Replace("'", "''");
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), $"Kubun='{sizeKu}'", "Code", startPos: target.Id_Siz);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		target.Id_Siz = meisho.Id;
		target.Code_Siz = meisho.Code ?? "";
		target.Mei_Siz = meisho.Name ?? "";
	}
}
