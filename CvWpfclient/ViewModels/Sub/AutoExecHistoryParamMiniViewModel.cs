using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class AutoExecHistoryParamMiniViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial AutoExecHistorySelectParameter Parameter { get; set; } = new();

	public void Initialize(AutoExecHistorySelectParameter? param) {
		Parameter = param ?? new AutoExecHistorySelectParameter { DisplayName = "自動実行履歴", MaxCount = AppGlobal.Limit };
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}
}
