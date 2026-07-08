using CvWpfclient.ViewModels._03Hatchu;
using System.Windows.Input;

namespace CvWpfclient.Views._03Hatchu;

public partial class HachuInputView : Helpers.BaseWindow {
	public HachuInputView() {
		InitializeComponent();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape
			&& DataContext is HachuInputViewModel vm
			&& vm.SelectedTabIndex == 1) {
			vm.SelectedTabIndex = 0;
			e.Handled = true;
			return;
		}

		base.OnPreviewKeyDown(e);
	}
}
