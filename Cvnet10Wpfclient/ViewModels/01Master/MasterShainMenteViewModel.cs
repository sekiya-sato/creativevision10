using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterShainMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterShain> {
	[ObservableProperty]
	string title = "社員マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["Mail", "VTenpo", "VBumon"];

	protected override string? SelectCodeDisplayName => "社員";

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand]
	void DoSelectTenpo() {
		var meisho = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code", startPos: CurrentEdit.Id_Tenpo);
		CurrentEdit.Id_Tenpo = meisho?.Id ?? 0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	[RelayCommand]
	void DoSelectBumon() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BMN'", "Code", startPos: CurrentEdit.Id_Bumon);
		CurrentEdit.Id_Bumon = meisho?.Id ?? 0;
		CurrentEdit.VBumon = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

}
