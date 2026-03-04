using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.ViewServices;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class SelectShainViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string fromCode = string.Empty;

	[ObservableProperty]
	string toCode = string.Empty;

	public void Initialize(string? fromCode, string? toCode) {
		FromCode = fromCode?.Trim() ?? string.Empty;
		ToCode = toCode?.Trim() ?? string.Empty;
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}

	[RelayCommand]
	void Cancel() {
		ClientLib.ExitDialogResult(this, false);
	}
}
