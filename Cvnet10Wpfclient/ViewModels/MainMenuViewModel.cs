using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10AppShared;
using Cvnet8client.Views.Sub;
using Cvnet10Wpfclient.Util;
using Cvnet10Wpfclient.Views;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels {
    public partial class MainMenuViewModel : ObservableObject {

		[RelayCommand]
		public void Exit() {
			if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				ClientLib.Exit(this);
			}
		}

		[RelayCommand]
		public void OpenTest20260203() {
			var view = new Test20260203View();
			ClientLib.ShowDialogView(view, this);
		}
	}
}
