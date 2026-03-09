using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.ViewServices;
using System.Windows.Input;

namespace Cvnet10Wpfclient.Helpers;

public partial class BaseViewModel : ObservableObject, IBaseViewModel {
	public int InitParam { get; set; }
	public string? AddInfo { get; set; }

	public ICommand ExitCommand { get; }

	public BaseViewModel() {
		ExitCommand = new RelayCommand(OnExit);
	}

	protected virtual void OnExit() {
		ClientLib.Exit(this);
	}

	public void ExitWithResultTrue() {
		ClientLib.ExitDialogResult(this, true);
	}

	public void ExitWithResultFalse() {
		ClientLib.Exit(this);
	}
}

public interface IBaseViewModel {
	public int InitParam { get; set; }
	public string? AddInfo { get; set; }
}
