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
}
