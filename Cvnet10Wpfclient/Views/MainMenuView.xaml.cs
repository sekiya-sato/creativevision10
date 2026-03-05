using Cvnet10Wpfclient.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Cvnet10Wpfclient.Views;

public partial class MainMenuView : Window {
	public MainMenuView() {
		InitializeComponent();
	}

	private void MenuTree_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) {
			return;
		}
		if (DataContext is not MainMenuViewModel vm) {
			return;
		}
		if (vm.SelectedMenu?.ViewType == null) {
			return;
		}
		if (vm.DoMenuCommand.CanExecute(null)) {
			vm.DoMenuCommand.Execute(null);
			e.Handled = true;
		}
	}
}
