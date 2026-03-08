using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.ViewServices;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class SelectCodeViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string fromId = string.Empty;

	[ObservableProperty]
	string toId = string.Empty;

	[ObservableProperty]
	string fromCode = string.Empty;

	[ObservableProperty]
	string toCode = string.Empty;

	[ObservableProperty]
	string cdName = string.Empty;

	[ObservableProperty]
	string name = string.Empty;

	[ObservableProperty]
	string maxCount = string.Empty;

	public void Initialize(string? fromId, string? toId, string? fromCode, string? toCode, string? cdName, string? name, string? maxCount = null) {
		FromId = fromId?.Trim() ?? string.Empty;
		ToId = toId?.Trim() ?? string.Empty;
		FromCode = fromCode?.Trim() ?? string.Empty;
		ToCode = toCode?.Trim() ?? "99999999";
		CdName = cdName?.Trim() ?? "社員";
		Name = name?.Trim() ?? string.Empty;
		MaxCount = maxCount?.Trim() ?? string.Empty;
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}

}
