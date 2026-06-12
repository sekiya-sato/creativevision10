using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class RangeParamViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	SelectParameter parameter = new();

	public void Initialize(SelectParameter? param) {
		Parameter = param ?? new SelectParameter { MaxCount = AppGlobal.Limit };
		if (Parameter.MaxCount is null or 0) Parameter.MaxCount = AppGlobal.Limit;
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}

}
