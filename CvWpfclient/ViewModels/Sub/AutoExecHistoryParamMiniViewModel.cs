using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class AutoExecHistoryParamMiniViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	AutoExecHistorySelectParameter parameter = new();

	public void Initialize(AutoExecHistorySelectParameter? param) {
		Parameter = param ?? new AutoExecHistorySelectParameter { DisplayName = "自動実行履歴", MaxCount = 400 };
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}
}
