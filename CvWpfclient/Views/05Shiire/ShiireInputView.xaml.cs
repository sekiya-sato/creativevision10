using CvWpfclient.ViewModels._05Shiire;
using System.Windows.Input;

namespace CvWpfclient.Views._05Shiire;

public partial class ShiireInputView : Helpers.BaseWindow {
	public ShiireInputView() {
		InitializeComponent();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape
			&& DataContext is ShiireInputViewModel vm
			&& vm.SelectedTabIndex == 1) {
			vm.SelectedTabIndex = 0;
			e.Handled = true;
			return;
		}

		base.OnPreviewKeyDown(e);
	}
}
