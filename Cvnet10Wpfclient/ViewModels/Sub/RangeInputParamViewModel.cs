using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class RangeInputParamViewModel : Helpers.BaseMenteViewModel<TranAllHeader> {
	[ObservableProperty]
	SelectInputParameter parameter = new();

	public void Initialize(SelectInputParameter? param) {
		Parameter = param ?? new SelectInputParameter();
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}

	[RelayCommand]
	void DoSelectFromTori() {
		var where = Parameter.ToriSearchWhere ?? "TenType>=0";
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), where, "Code");
		if (tokui == null) return;
		Parameter.FromToriCd = tokui.Code ?? "";
		Parameter.FromToriName = tokui.Name ?? "";
	}

	[RelayCommand]
	void DoSelectToTori() {
		var where = Parameter.ToriSearchWhere ?? "TenType>=0";
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), where, "Code");
		if (tokui == null) return;
		Parameter.ToToriCd = tokui.Code ?? "";
		Parameter.ToToriName = tokui.Name ?? "";
	}

	[RelayCommand]
	void DoSelectFromSoko() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code");
		if (tokui == null) return;
		Parameter.FromSokoCd = tokui.Code ?? "";
		Parameter.FromSokoName = tokui.Name ?? "";
	}

	[RelayCommand]
	void DoSelectToSoko() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code");
		if (tokui == null) return;
		Parameter.ToSokoCd = tokui.Code ?? "";
		Parameter.ToSokoName = tokui.Name ?? "";
	}
}
