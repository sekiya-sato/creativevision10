using CvWpfclient.ViewModels._08Zaiko;
using System.Windows.Input;

namespace CvWpfclient.Views._08Zaiko;

public partial class StockInputView : Helpers.BaseWindow {
	public StockInputView() {
		InitializeComponent();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape
			&& DataContext is StockInputViewModel vm
			&& vm.SelectedTabIndex == 1) {
			vm.SelectedTabIndex = 0;
			e.Handled = true;
			return;
		}

		base.OnPreviewKeyDown(e);
	}
}
