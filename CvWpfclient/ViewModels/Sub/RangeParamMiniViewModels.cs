using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

/// <summary>範囲指定ミニ版 ViewModel（ID・件数のみ）</summary>
public partial class RangeParamMiniViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	SelectParameter parameter = new();

	/// <summary>表示前に呼び出す初期化メソッド</summary>
	public void Initialize(SelectParameter? param) {
		Parameter = param ?? new();
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}
}
