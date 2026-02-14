/*
# file name
BaseViewModel.cs

# description
新規作成のViewModelに、共通パラメタとExitコマンドを提供する

# example
ViewModel側:
namespace Cvnet10Wpfclient.ViewModels;
public partial class NewTargetViewModel : Helpers.BaseViewModel {
}
*/
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Cvnet10Wpfclient.ViewServices;
using System.Windows.Input;

namespace Cvnet10Wpfclient.Helpers;

public partial class BaseViewModel : ObservableObject, IBaseViewModel {
	public int InitParam { get; set; }
	public string? AddInfo { get; set; }


	// Base が提供する Exit コマンド（派生は OnExit をオーバーライドして振る舞いを変えられる）
	public ICommand ExitCommand { get; }

	public BaseViewModel() {
		ExitCommand = new RelayCommand(OnExit);
	}

	protected virtual void OnExit() {
		ClientLib.Exit(this);
		// デフォルト動作: Dialog を閉じる（false）
		//WeakReferenceMessenger.Default.Send(new DialogCloseMessage(false));
	}

	public void ExitWithResultTrue() {
		ClientLib.ExitDialogResult(this, true);
		//WeakReferenceMessenger.Default.Send(new DialogCloseMessage(true));
	}
	public void ExitWithResultFalse() {
		ClientLib.Exit(this);
		//WeakReferenceMessenger.Default.Send(new DialogCloseMessage(false));
	}
}

public record class DialogCloseMessage(bool DialogResult);
public class ShortMsg : ValueChangedMessage<string> {
	public ShortMsg(string value) : base(value) {
	}
}
public class ChangeSelectMsg : ValueChangedMessage<long> {
	public ChangeSelectMsg(long value) : base(value) {
	}
}



public interface IBaseViewModel {
	public int InitParam { get; set; }
	public string? AddInfo { get; set; }
}