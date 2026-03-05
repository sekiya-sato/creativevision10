using System.Windows;
using System.Windows.Input;
using Cvnet10Wpfclient.ViewModels;

namespace Cvnet10Wpfclient.Views;

public partial class ZzMainMenuView : Window {
    public ZzMainMenuView() {
        InitializeComponent();
	}

	private void MenuTree_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) {
			return;
		}
		if (DataContext is not ZzMainMenuViewModel vm) {
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
