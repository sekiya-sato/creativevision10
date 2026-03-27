using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterEndCustomerMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterEndCustomer> {
	[ObservableProperty]
	string title = "顧客マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["Rank", "VTenpo"];

	protected override string? SelectCodeDisplayName => "顧客";

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand]
	void DoSelectTenpo() {
		var meisho = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code", startPos: CurrentEdit.Id_Tenpo);
		CurrentEdit.Id_Tenpo = meisho?.Id ?? 0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

}
