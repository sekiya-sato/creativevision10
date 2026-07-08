using CvWpfclient.ViewModels._04Juchu;
using System.Windows.Input;

namespace CvWpfclient.Views._04Juchu;

public partial class JuchuInputView : Helpers.BaseWindow {
	public JuchuInputView() {
		InitializeComponent();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e) {
		if (e.Key == Key.Escape
			&& DataContext is JuchuInputViewModel vm
			&& vm.SelectedTabIndex == 1) {
			vm.SelectedTabIndex = 0;
			e.Handled = true;
			return;
		}

		base.OnPreviewKeyDown(e);
	}
}
