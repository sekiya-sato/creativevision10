using CvWpfclient.ViewModels._06Uriage;
using System.Windows.Input;

namespace CvWpfclient.Views._06Uriage;

public partial class ShopUriageInputView : Helpers.BaseWindow {
	public ShopUriageInputView() {
		InitializeComponent();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape
			&& DataContext is ShopUriageInputViewModel vm
			&& vm.SelectedTabIndex == 1) {
			vm.SelectedTabIndex = 0;
			e.Handled = true;
			return;
		}

		base.OnPreviewKeyDown(e);
	}
}
