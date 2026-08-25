using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class CategoryRangeViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial CategoryRangeParameter Parameter { get; set; } = new();

	public void Initialize(CategoryRangeParameter? param) {
		Parameter = param ?? new CategoryRangeParameter();
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}
}
