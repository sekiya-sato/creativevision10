using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MasterShainMenteViewModel : Helpers.BaseMenteViewModel<MasterShain> {
	[ObservableProperty]
	string title = "社員マスターメンテ";

	protected override string? ListOrder => "Code";

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

	protected override void AfterInsert(MasterShain item) {
		Message = $"追加しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterUpdate(MasterShain item) {
		Message = $"修正しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterDelete(MasterShain removedItem) {
		Message = $"削除しました (CD={removedItem.Code}, Id={removedItem.Id})";
	}
	[RelayCommand]
	void DoSelectTenpo() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterTokui), "TenType=6", "Code");
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterTokui;
		CurrentEdit.Id_Tenpo = meisho?.Id??0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei= meisho?.Name ?? "" };
	}
	[RelayCommand]
	void DoSelectBumon() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='BMN'", "Code");
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		CurrentEdit.Id_Bumon = meisho?.Id ?? 0;
		CurrentEdit.VBumon = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}
}