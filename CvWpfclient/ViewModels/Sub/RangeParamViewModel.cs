using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class RangeParamViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	SelectParameter parameter = new();

	public void Initialize(SelectParameter? param) {
		Parameter = param ?? new();
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}

}
