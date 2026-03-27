using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;

namespace Cvnet10Wpfclient.ViewModels._01Master;

/// <summary>
/// 仕入先マスターメンテ ViewModel
/// </summary>
public partial class MasterShiireMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShiire> {
	[ObservableProperty]
	string title = "仕入先マスターメンテ";

	protected override string? SelectCodeDisplayName => "仕入先";

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectPayMethod() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='PAY'", "Code", startPos: CurrentEdit.Id_PayMethod);
		if (meisho == null) return;
		CurrentEdit.Id_PayMethod = meisho.Id;
		CurrentEdit.VPayMethod = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectPaysaki() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Paysaki);
		if (shiire == null) return;
		CurrentEdit.Id_Paysaki = shiire.Id;
		CurrentEdit.VPaysaki = new() { Sid = shiire.Id, Cd = shiire.Code ?? "", Mei = shiire.Name ?? "" };
	}
}
